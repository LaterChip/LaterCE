using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using LaterCE.Models;

namespace LaterCE.Services;

public class GameLauncher
{
    private readonly MinecraftVersionService _versions;

    public GameLauncher(MinecraftVersionService versions) => _versions = versions;

    public Process? RunningProcess { get; private set; }

    /// <summary>最近一次启动失败的原因（供 UI 弹窗展示）</summary>
    public string? LastError { get; private set; }

    public event Action<string>? OutputReceived;
    public event Action<int>? Exited;

    /// <summary>按继承链合并版本信息（Fabric / Forge 版本会继承原版）</summary>
    public async Task<VersionDetail?> ResolveDetailAsync(string versionId, CancellationToken ct = default)
    {
        var detail = await _versions.GetVersionDetailAsync(versionId, ct: ct);
        if (detail == null) return null;

        var chain = new List<VersionDetail> { detail };
        var current = detail;
        var guard = 0;

        while (!string.IsNullOrEmpty(current.InheritsFrom) && guard++ < 5)
        {
            var parent = await _versions.GetVersionDetailAsync(current.InheritsFrom, ct: ct);
            if (parent == null) break;
            chain.Add(parent);
            current = parent;
        }

        if (chain.Count == 1) return detail;

        for (var i = 1; i < chain.Count; i++)
        {
            var p = chain[i];
            if (string.IsNullOrEmpty(detail.MainClass)) detail.MainClass = p.MainClass;
            if (string.IsNullOrEmpty(detail.Assets)) detail.Assets = p.Assets;
            if (string.IsNullOrEmpty(detail.AssetIndex.Url)) detail.AssetIndex = p.AssetIndex;
            if (detail.Downloads.Client == null) detail.Downloads.Client = p.Downloads.Client;
            if (detail.JavaVersion.MajorVersion == 0) detail.JavaVersion = p.JavaVersion;
            if (detail.Logging == null) detail.Logging = p.Logging;
            if (string.IsNullOrEmpty(detail.MinecraftArguments)) detail.MinecraftArguments = p.MinecraftArguments;

            detail.Libraries.AddRange(p.Libraries);
            detail.Arguments.Jvm.InsertRange(0, p.Arguments.Jvm);
            detail.Arguments.Game.InsertRange(0, p.Arguments.Game);
        }

        // 子版本优先，按 group:artifact 去重（避免 asm 9.10 与 9.3 同时出现在 classpath 引发 duplicate ASM classes）
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        detail.Libraries = detail.Libraries.Where(l => seen.Add(LibraryKey(l.Name))).ToList();

        return detail;
    }

    /// <summary>group:artifact:classifier —— 忽略版本号，用于 classpath 去重（保留 classifier，避免误删 natives 条目）</summary>
    private static string LibraryKey(string name)
    {
        if (string.IsNullOrEmpty(name)) return "";
        var at = name.IndexOf('@');
        var core = at > 0 ? name[..at] : name;
        var parts = core.Split(':');
        if (parts.Length < 2) return core;
        var classifier = parts.Length >= 4 ? parts[3] : "";
        return parts[0] + ":" + parts[1] + ":" + classifier;
    }

    public async Task<bool> LaunchAsync(string versionId, Account account, CancellationToken ct = default)
    {
        try
        {
            var detail = await ResolveDetailAsync(versionId, ct);
            if (detail == null)
            {
                Log.Error($"启动失败：找不到版本 {versionId}，请先下载。");
                return false;
            }

            var required = detail.JavaVersion.MajorVersion;
            var java = await ResolveJavaAsync(required, ct);
            if (string.IsNullOrEmpty(java))
            {
                LastError = $"该版本要求 Java {Math.Max(required, 21)}，本机未找到匹配运行时，自动安装也失败了。\n" +
                            "请到「设置」页一键安装 JRE 或手动指定 java.exe 后重试。";
                Log.Error("启动失败：" + LastError);
                return false;
            }

            var javaMajor = JavaFinder.MajorOf(java);
            if (required > 0 && javaMajor > 0 && javaMajor < required)
            {
                LastError = $"Java 版本不匹配：该版本要求 Java {required}，当前 Java {javaMajor} 无法运行" +
                            "（会抛 UnsupportedClassVersionError）。";
                Log.Error("启动失败：" + LastError);
                return false;
            }

            if (required > 0 && javaMajor > 0)
                Log.Info($"Java 校验通过：要求 Java {required}，实际 Java {javaMajor}");

            var versionDir = Path.Combine(AppPaths.VersionsDir, versionId);
            var instanceDir = AppPaths.InstanceDir(versionId);
            Directory.CreateDirectory(instanceDir);
            Directory.CreateDirectory(versionDir);

            // 关键依赖库兜底：缺失时按「BMCLAPI 镜像 → 官方」补下载，避免启动即 NoClassDefFoundError
            await EnsureLibrariesAsync(detail, ct);

            // 解压 natives
            var nativesDir = Path.Combine(versionDir, "natives");
            Directory.CreateDirectory(nativesDir);
            foreach (var lib in detail.Libraries.Where(l => IsNativesLibrary(l) && VersionInstaller.IsRulesAllowed(l.Rules)))
            {
                var jarPath = Path.Combine(AppPaths.LibrariesDir,
                    VersionInstaller.ResolveLibraryPath(lib).Replace('/', Path.DirectorySeparatorChar));
                ExtractNatives(jarPath, nativesDir);
            }

            // 组装 classpath
            var classpathParts = new List<string>();
            foreach (var lib in detail.Libraries.Where(l => VersionInstaller.IsRulesAllowed(l.Rules)))
            {
                var p = Path.Combine(AppPaths.LibrariesDir,
                    VersionInstaller.ResolveLibraryPath(lib).Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(p)) classpathParts.Add(p);
            }

            var clientJar = Path.Combine(versionDir, versionId + ".jar");
            if (!File.Exists(clientJar))
            {
                // Fabric / Quilt 等继承版本自身没有客户端 jar，需沿继承链回退到父版本
                var parentId = detail.InheritsFrom;
                var guardId = 0;
                while (!string.IsNullOrEmpty(parentId) && guardId++ < 5)
                {
                    var candidate = Path.Combine(AppPaths.VersionsDir, parentId, parentId + ".jar");
                    if (File.Exists(candidate)) { clientJar = candidate; break; }

                    var parent = await _versions.GetVersionDetailAsync(parentId, ct: ct);
                    parentId = parent?.InheritsFrom ?? "";
                }
            }

            if (File.Exists(clientJar)) classpathParts.Add(clientJar);
            else if (detail.Downloads.Client != null)
                Log.Warn("未找到客户端 jar，将尝试仅用依赖库启动。");

            if (classpathParts.Count == 0)
            {
                Log.Error("启动失败：classpath 为空，请重新下载该版本。");
                return false;
            }

            var classpath = string.Join(";", classpathParts);
            var cfg = ConfigService.Current;

            var ctx = new Dictionary<string, string>
            {
                ["auth_player_name"] = account.Name,
                ["version_name"] = versionId,
                ["game_directory"] = instanceDir,
                ["assets_root"] = AppPaths.AssetsDir,
                ["assets_index_name"] = string.IsNullOrEmpty(detail.Assets) ? detail.AssetIndex.Id : detail.Assets,
                ["auth_uuid"] = string.IsNullOrEmpty(account.Uuid) ? OfflineUuid(account.Name) : account.Uuid,
                ["auth_access_token"] = string.IsNullOrEmpty(account.AccessToken) ? "0" : account.AccessToken,
                ["clientid"] = "",
                ["auth_xuid"] = "",
                ["user_type"] = account.Type == "microsoft" ? "msa" : "mojang",
                ["version_type"] = string.IsNullOrEmpty(detail.Type) ? "release" : detail.Type,
                ["user_properties"] = "{}",
                ["natives_directory"] = nativesDir,
                ["launcher_name"] = "LaterCE",
                ["launcher_version"] = "1.0.0",
                ["classpath"] = classpath,
                ["classpath_separator"] = ";",
                ["library_directory"] = AppPaths.LibrariesDir.Replace('\\', '/'),
                ["resolution_width"] = "854",
                ["resolution_height"] = "480"
            };

            var logArgument = detail.Logging?.Client.Argument ?? "";
            var logFileId = detail.Logging?.Client.File.Id ?? "";
            if (!string.IsNullOrEmpty(logFileId))
            {
                var logConfigPath = Path.Combine(AppPaths.AssetsDir, "log_configs", logFileId);
                // 日志配置缺失（404 等）不应阻断启动，跳过该 JVM 参数即可
                if (File.Exists(logConfigPath))
                    // log4j 需要合法 URL，裸路径会被当作 unknown protocol: c
                    ctx["path"] = new Uri(logConfigPath).AbsoluteUri;
                else
                    Log.Warn($"日志配置文件缺失，已跳过：{logFileId}");
            }

            // JVM 参数
            var jvmArgs = new List<string>
            {
                $"-Xms{Math.Max(256, cfg.MinMemoryMb)}M",
                $"-Xmx{Math.Max(512, cfg.MaxMemoryMb)}M",
                "-Dfile.encoding=UTF-8",
                "-Dstdout.encoding=UTF-8",
                "-Dstderr.encoding=UTF-8",
                "-Dminecraft.launcher.brand=LaterCE",
                "-Dminecraft.launcher.version=1.0.0",
                "-Djava.library.path=" + nativesDir,
                "-cp",
                classpath
            };

            jvmArgs.AddRange(BuildArguments(detail.Arguments.Jvm, ctx));

            if (!string.IsNullOrEmpty(logArgument) && ctx.ContainsKey("path"))
                jvmArgs.Add(ReplaceVars(logArgument, ctx));

            if (!string.IsNullOrWhiteSpace(cfg.ExtraJvmArgs))
                jvmArgs.AddRange(SplitArgs(cfg.ExtraJvmArgs));

            // 游戏参数
            var gameArgs = new List<string>();
            if (detail.Arguments.Game.Count > 0)
                gameArgs.AddRange(BuildArguments(detail.Arguments.Game, ctx));
            else
                gameArgs.AddRange(SplitArgs(ReplaceVars(detail.MinecraftArguments, ctx)));

            if (!string.IsNullOrWhiteSpace(cfg.ExtraGameArgs))
                gameArgs.AddRange(SplitArgs(cfg.ExtraGameArgs));

            gameArgs = gameArgs.Where(a => !string.IsNullOrWhiteSpace(a)).ToList();

            var psi = new ProcessStartInfo(java)
            {
                WorkingDirectory = instanceDir,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            // 去掉 arguments.jvm 中可能存在的内存参数，避免与用户设置冲突
            var filtered = jvmArgs.Where(a => !a.StartsWith("-Xmx") && !a.StartsWith("-Xms")).ToList();
            filtered.Insert(0, $"-Xms{Math.Max(256, cfg.MinMemoryMb)}M");
            filtered.Insert(1, $"-Xmx{Math.Max(512, cfg.MaxMemoryMb)}M");

            foreach (var a in filtered) psi.ArgumentList.Add(a);
            psi.ArgumentList.Add(detail.MainClass);
            foreach (var a in gameArgs) psi.ArgumentList.Add(a);

            Log.Info($"使用 Java：{java}");
            Log.Info($"启动版本 {versionId}（{detail.MainClass}），账户：{account.Name}");
            Log.Info("游戏目录：" + instanceDir);

            var process = new Process
            {
                StartInfo = psi,
                EnableRaisingEvents = true
            };

            process.OutputDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) Emit(e.Data); };
            process.Exited += (_, _) =>
            {
                var code = -1;
                try { code = process.ExitCode; } catch { }
                Log.Info($"游戏进程已退出，退出码 {code}。");
                try { Exited?.Invoke(code); } catch { }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            RunningProcess = process;

            Log.Info("游戏进程已启动。");
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("启动失败：" + ex);
            return false;
        }
    }

    /// <summary>严格解析 Java：版本不匹配时自动安装对应 JRE 并重试</summary>
    private static async Task<string?> ResolveJavaAsync(int requiredMajor, CancellationToken ct)
    {
        var java = JavaFinder.Resolve(requiredMajor);
        if (!string.IsNullOrEmpty(java)) return java;

        var major = requiredMajor <= 0 ? 21 : requiredMajor;
        Log.Warn($"未找到匹配的 Java {major} 运行时，开始自动下载安装（TUNA Adoptium 镜像优先）…");

        var progress = new Progress<double>(p =>
        {
            if (p > 0 && p < 1) Log.Info($"Java {major} 运行时下载中：{p * 100:F0}%");
        });

        var installed = await JavaFinder.InstallJreAsync(major, progress, ct);
        if (!string.IsNullOrEmpty(installed))
        {
            Log.Info($"已自动安装 Java {major}：{installed}");
            return installed;
        }

        JavaFinder.Scan();
        return JavaFinder.Resolve(requiredMajor);
    }

    /// <summary>启动前校验关键依赖库，缺失则按镜像链补下载</summary>
    private static async Task EnsureLibrariesAsync(VersionDetail detail, CancellationToken ct)
    {
        var missing = new List<(Library lib, string path)>();
        foreach (var lib in detail.Libraries.Where(l => VersionInstaller.IsRulesAllowed(l.Rules)))
        {
            var path = Path.Combine(AppPaths.LibrariesDir,
                VersionInstaller.ResolveLibraryPath(lib).Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) missing.Add((lib, path));
        }

        if (missing.Count == 0) return;

        Log.Warn($"检测到 {missing.Count} 个依赖库缺失，尝试自动补下载…");

        var items = new List<DownloadItem>();
        foreach (var (lib, path) in missing)
        {
            var url = lib.Downloads.Artifact?.Url;
            if (string.IsNullOrEmpty(url))
            {
                var rel = VersionInstaller.ResolveLibraryPath(lib);
                if (string.IsNullOrEmpty(rel)) continue;
                url = "https://libraries.minecraft.net/" + rel;
            }
            items.Add(new DownloadItem(url, path, lib.Downloads.Artifact?.Size ?? 0));
        }

        var result = await Downloader.DownloadBatchAsync(items, 8, null, ct);
        Log.Info($"依赖库补齐完成：成功 {result.ok}，失败 {result.failed}");

        var stillMissing = items.Where(i => !File.Exists(i.DestPath)).ToList();
        if (stillMissing.Count > 0)
        {
            var names = string.Join("、", stillMissing.Take(3).Select(i => Path.GetFileName(i.DestPath)));
            Log.Warn($"仍有 {stillMissing.Count} 个依赖库缺失（{names} 等），游戏可能启动失败。");
        }
    }

    private void Emit(string line)
    {
        try { OutputReceived?.Invoke(line); } catch { }
    }

    private static bool IsNativesLibrary(Library lib)
    {
        if (lib.Natives is { Count: > 0 }) return true;

        var artifactPath = lib.Downloads.Artifact?.Path ?? "";
        return artifactPath.Contains("natives-windows", StringComparison.OrdinalIgnoreCase);
    }

    private static void ExtractNatives(string jarPath, string destDir)
    {
        if (!File.Exists(jarPath)) return;

        try
        {
            using var zip = ZipFile.OpenRead(jarPath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;

                var name = entry.Name;
                var isNative = name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                               name.EndsWith(".so", StringComparison.OrdinalIgnoreCase) ||
                               name.EndsWith(".dylib", StringComparison.OrdinalIgnoreCase);

                if (!isNative) continue;
                if (name.Contains("META-INF", StringComparison.OrdinalIgnoreCase)) continue;

                var target = Path.Combine(destDir, name);
                entry.ExtractToFile(target, true);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"解压 natives 失败 {Path.GetFileName(jarPath)}：{ex.Message}");
        }
    }

    private static List<string> BuildArguments(List<JsonElement> source, Dictionary<string, string> ctx)
    {
        var result = new List<string>();

        foreach (var element in source)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                var value = element.GetString();
                if (!string.IsNullOrEmpty(value))
                    result.Add(ReplaceVars(value, ctx));
            }
            else if (element.ValueKind == JsonValueKind.Object)
            {
                if (!JsonRuleAllowed(element)) continue;

                if (element.TryGetProperty("value", out var valueElement))
                {
                    if (valueElement.ValueKind == JsonValueKind.String)
                    {
                        var v = valueElement.GetString();
                        if (!string.IsNullOrEmpty(v)) result.Add(ReplaceVars(v, ctx));
                    }
                    else if (valueElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in valueElement.EnumerateArray())
                        {
                            var v = item.GetString();
                            if (!string.IsNullOrEmpty(v)) result.Add(ReplaceVars(v, ctx));
                        }
                    }
                }
            }
        }

        return result;
    }

    private static bool JsonRuleAllowed(JsonElement element)
    {
        if (!element.TryGetProperty("rules", out var rules) || rules.ValueKind != JsonValueKind.Array)
            return true;

        var allowed = false;
        foreach (var rule in rules.EnumerateArray())
        {
            var matches = true;

            if (rule.TryGetProperty("os", out var os) && os.ValueKind == JsonValueKind.Object)
            {
                if (os.TryGetProperty("name", out var osName))
                {
                    var n = osName.GetString();
                    if (!string.IsNullOrEmpty(n) && n != "windows") matches = false;
                }

                if (matches && os.TryGetProperty("arch", out var osArch))
                {
                    var a = osArch.GetString();
                    var arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
                    if (!string.IsNullOrEmpty(a) && !string.Equals(a, arch, StringComparison.OrdinalIgnoreCase))
                        matches = false;
                }
            }

            if (matches && rule.TryGetProperty("features", out var features) &&
                features.ValueKind == JsonValueKind.Object)
            {
                foreach (var f in features.EnumerateObject())
                    if (f.Value.ValueKind == JsonValueKind.True) matches = false;
            }

            if (!matches) continue;

            var action = rule.TryGetProperty("action", out var act) ? act.GetString() : "allow";
            allowed = action == "allow";
        }

        return allowed;
    }

    private static string ReplaceVars(string input, Dictionary<string, string> ctx)
    {
        if (string.IsNullOrEmpty(input)) return input;

        foreach (var kv in ctx)
            input = input.Replace("${" + kv.Key + "}", kv.Value ?? "");

        // 清理未识别的变量（如 ${quickPlayPath}）
        input = Regex.Replace(input, @"\$\{[a-zA-Z0-9_]+\}", "");
        return input;
    }

    /// <summary>简单命令行拆分，支持双引号包裹</summary>
    public static List<string> SplitArgs(string input)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return result;

        var current = new System.Text.StringBuilder();
        var inQuotes = false;

        foreach (var c in input)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }

            if (c == ' ' && !inQuotes)
            {
                if (current.Length > 0) { result.Add(current.ToString()); current.Clear(); }
                continue;
            }

            current.Append(c);
        }

        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    /// <summary>离线模式 UUID：MD5("OfflinePlayer:" + name)，设置版本位</summary>
    public static string OfflineUuid(string name)
    {
        var hash = System.Security.Cryptography.MD5.HashData(
            System.Text.Encoding.UTF8.GetBytes("OfflinePlayer:" + name));
        hash[6] = (byte)((hash[6] & 0x0F) | 0x30);
        hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
