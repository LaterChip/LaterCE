using System.Diagnostics;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace LaterCE.Services;

public class JavaInstall
{
    public string Path { get; set; } = "";
    public int Major { get; set; }
    public string Display => $"Java {Major}  ·  {Path}";
}

public static class JavaFinder
{
    public static List<JavaInstall> Installed { get; } = new();

    public static event Action? Scanned;

    public static void Initialize() => Scan();

    public static void Scan()
    {
        var found = new Dictionary<string, JavaInstall>(StringComparer.OrdinalIgnoreCase);

        void TryAdd(string? exe)
        {
            if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) return;
            if (found.ContainsKey(exe)) return;
            var major = ReadMajor(exe);
            found[exe] = new JavaInstall { Path = exe, Major = major };
        }

        // 1. PATH 中的 java
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathVar.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            try { TryAdd(System.IO.Path.Combine(dir.Trim(), "java.exe")); } catch { }
        }

        // 2. 常见 JDK 安装目录
        var roots = new[]
        {
            @"C:\Program Files\Java",
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Zulu",
            @"C:\Program Files\BellSoft",
            @"C:\Program Files\Amazon Corretto",
            @"C:\Program Files (x86)\Java",
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
            AppPaths.JavaDir
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root)) continue;
            try
            {
                foreach (var dir in Directory.GetDirectories(root))
                    TryAdd(System.IO.Path.Combine(dir, "bin", "java.exe"));
            }
            catch { }
        }

        // 2.1 本启动器自动安装的 JRE（解压后可能多一层目录，需递归）
        try
        {
            if (Directory.Exists(AppPaths.JavaDir))
            {
                foreach (var exe in Directory.GetFiles(AppPaths.JavaDir, "java.exe", SearchOption.AllDirectories))
                    TryAdd(exe);
            }
        }
        catch { }

        // 3. Mojang 官方启动器自带的 runtime
        var mojangRuntime = System.IO.Path.Combine(AppPaths.GameDir, "runtime");
        if (Directory.Exists(mojangRuntime))
        {
            try
            {
                foreach (var exe in Directory.GetFiles(mojangRuntime, "java.exe", SearchOption.AllDirectories))
                    TryAdd(exe);
            }
            catch { }
        }

        // 4. 用户手动指定的路径
        if (!string.IsNullOrWhiteSpace(ConfigService.Current.JavaPath))
            TryAdd(ConfigService.Current.JavaPath);

        Installed.Clear();
        Installed.AddRange(found.Values.OrderByDescending(j => j.Major));

        Log.Info(Installed.Count == 0
            ? "未检测到已安装的 Java，可在设置中一键安装 JRE。"
            : $"已检测到 {Installed.Count} 个 Java：{string.Join(", ", Installed.Select(j => $"Java {j.Major}"))}");

        Scanned?.Invoke();
    }

    /// <summary>
    /// 按版本要求的 Java 大版本严格选择运行时：
    /// 只接受 Major &gt;= requiredMajor 的运行时，绝不把低版本 Java 交给高版本游戏
    /// （否则会报 UnsupportedClassVersionError）。requiredMajor &lt;= 0 表示版本未声明要求。
    /// </summary>
    public static string? Resolve(int requiredMajor)
    {
        if (Installed.Count == 0) Scan();

        if (Installed.Count == 0) return null;

        // 优先使用与要求大版本完全一致的运行时（避免 1.21.2 被 Java 25 误用导致兼容问题）
        var exact = Installed.Where(j => j.Major == requiredMajor).OrderBy(j => j.Path).FirstOrDefault();
        if (exact != null) return exact.Path;

        // 用户手动指定的 Java：满足版本要求时采用
        var manual = ConfigService.Current.JavaPath;
        if (!string.IsNullOrWhiteSpace(manual) && File.Exists(manual))
        {
            var manualMajor = MajorOf(manual);
            if (requiredMajor <= 0 || manualMajor <= 0 || manualMajor >= requiredMajor)
                return manual;
        }

        if (requiredMajor <= 8)
        {
            var j8 = Installed.Where(j => j.Major == 8).OrderByDescending(j => j.Major).FirstOrDefault();
            return (j8 ?? Installed.OrderBy(j => j.Major).First()).Path;
        }

        var suitable = Installed.Where(j => j.Major >= requiredMajor).OrderBy(j => j.Major).FirstOrDefault();
        if (suitable != null) return suitable.Path;

        // 有硬性要求但本机没有匹配运行时：返回 null，由上层触发自动安装
        return null;
    }

    /// <summary>读取 java.exe 的大版本号（0 表示无法识别）</summary>
    public static int MajorOf(string javaExe)
        => string.IsNullOrWhiteSpace(javaExe) ? 0 : ReadMajor(javaExe);

    /// <summary>
    /// 确保存在满足版本要求的 Java：找不到时自动下载安装对应 JRE 并返回 java.exe 路径。
    /// </summary>
    public static async Task<string?> EnsureAsync(int requiredMajor, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var exe = Resolve(requiredMajor);
        if (!string.IsNullOrEmpty(exe)) return exe;

        var major = requiredMajor <= 0 ? 21 : requiredMajor;
        Log.Info($"未找到满足要求的 Java（需要 Java {major}），开始自动安装…");
        var installed = await InstallJreAsync(major, progress, ct);
        if (!string.IsNullOrEmpty(installed)) return installed;

        // 安装失败后再扫一次，避免路径写盘/扫描时序问题
        Scan();
        return Resolve(requiredMajor);
    }

    private static int ReadMajor(string javaExe)
    {
        var dir = System.IO.Path.GetDirectoryName(javaExe);
        for (var i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
        {
            var release = System.IO.Path.Combine(dir, "release");
            if (File.Exists(release))
            {
                try
                {
                    foreach (var line in File.ReadAllLines(release))
                    {
                        if (!line.StartsWith("JAVA_VERSION=", StringComparison.Ordinal)) continue;
                        var v = line.Split('=', 2)[1].Trim().Trim('"');
                        if (v.StartsWith("1."))
                            return int.TryParse(v.Split('.')[1], out var m8) ? m8 : 8;
                        return int.TryParse(v.Split('.')[0], out var m) ? m : 0;
                    }
                }
                catch { }
            }

            dir = System.IO.Path.GetDirectoryName(dir);
        }

        // 回退：从路径中解析版本号
        var match = Regex.Match(javaExe, @"(?:jdk|jre|java)[- ]?(\d+)(?:\.(\d+))?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            var first = int.Parse(match.Groups[1].Value);
            if (first == 1 && match.Groups[2].Success) return int.Parse(match.Groups[2].Value);
            return first;
        }

        return 0;
    }

    /// <summary>获取 JRE 下载候选地址：国内镜像优先，官方兜底</summary>
    private static async Task<List<string>> GetJreUrlsAsync(int major, CancellationToken ct)
    {
        var urls = new List<string>();

        // 1) 清华 TUNA 镜像（国内直连可用，不受 GitHub 干扰）
        try
        {
            var index = $"https://mirrors.tuna.tsinghua.edu.cn/Adoptium/{major}/jre/x64/windows/";
            var html = await Http.Client.GetStringAsync(index, ct);
            var matches = Regex.Matches(html, @"href=""([^""?]+\.zip)""");
            if (matches.Count > 0)
            {
                var name = matches[matches.Count - 1].Groups[1].Value;
                urls.Add(index + name);
            }
        }
        catch (Exception ex)
        {
            Log.Warn("清华镜像源解析失败：" + ex.Message);
        }

        // 2) 官方 Adoptium（会 302 到 GitHub，国内可能不可用）
        urls.Add($"https://api.adoptium.net/v3/binary/latest/{major}/ga/windows/x64/jre/hotspot/normal/eclipse");
        return urls;
    }

    /// <summary>下载并解压 JRE</summary>
    public static async Task<string?> InstallJreAsync(int major, IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        Directory.CreateDirectory(AppPaths.JavaDir);
        var zip = System.IO.Path.Combine(AppPaths.JavaDir, $"jre-{major}.zip");

        var ok = false;
        foreach (var url in await GetJreUrlsAsync(major, ct))
        {
            Log.Info($"开始下载 Java {major} 运行时：{url}");
            try
            {
                ok = await Downloader.DownloadFileAsync(url, zip, 0, (d, t) =>
                {
                    if (t > 0) progress?.Report((double)d / t);
                }, ct);
            }
            catch (Exception ex)
            {
                Log.Warn("下载异常：" + ex.Message);
                ok = false;
            }

            if (ok && File.Exists(zip) && new FileInfo(zip).Length > 1024 * 1024) break;
            ok = false;
        }

        if (!ok)
        {
            Log.Error("Java 运行时下载失败，请检查网络或手动安装 JDK。");
            return null;
        }

        var target = System.IO.Path.Combine(AppPaths.JavaDir, $"jre-{major}");
        try
        {
            if (Directory.Exists(target)) Directory.Delete(target, true);
            ZipFile.ExtractToDirectory(zip, target);
        }
        catch (Exception ex)
        {
            Log.Error("解压 Java 运行时失败：" + ex.Message);
            return null;
        }

        var exe = Directory.GetFiles(target, "java.exe", SearchOption.AllDirectories).FirstOrDefault();
        if (exe == null)
        {
            Log.Error("解压后未找到 java.exe");
            return null;
        }

        try { File.Delete(zip); } catch { }

        // 不覆盖用户手动指定的 Java；仅在未指定时写入默认路径
        if (string.IsNullOrWhiteSpace(ConfigService.Current.JavaPath) ||
            !File.Exists(ConfigService.Current.JavaPath))
        {
            ConfigService.Current.JavaPath = exe;
            ConfigService.Save();
        }

        Scan();
        Log.Info($"Java {major} 安装完成：{exe}");
        return exe;
    }

    /// <summary>启动一次 java -version 验证可用性</summary>
    public static async Task<string> GetVersionOutputAsync(string javaExe)
    {
        try
        {
            var psi = new ProcessStartInfo(javaExe)
            {
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("-version");

            using var p = Process.Start(psi);
            if (p == null) return "";
            var text = await p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync();
            return text.Trim();
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }
}
