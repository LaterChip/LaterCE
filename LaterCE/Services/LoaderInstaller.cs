using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using LaterCE.Models;

namespace LaterCE.Services;

/// <summary>可选模组加载器</summary>
public class LoaderOption
{
    public string Name { get; set; } = "";
    public string Key { get; set; } = "";
    public bool NeedsLoaderVersion { get; set; }

    public override string ToString() => Name;
}

/// <summary>某个加载器在指定游戏版本下可用的具体版本</summary>
public class LoaderVersionOption
{
    public string Version { get; set; } = "";
    public bool Stable { get; set; }
    public string Display => Stable ? $"{Version}（推荐）" : Version;

    public override string ToString() => Display;
}

/// <summary>
/// 模组加载器安装器：
/// Vanilla 直接装原版；Fabric / Quilt 通过官方 meta 生成可继承的版本 json；
/// Forge / NeoForge 下载 installer 并执行 --installClient（官方源超时自动走 BMCLAPI 镜像下载 installer）。
/// </summary>
public class LoaderInstaller
{
    private readonly MinecraftVersionService _versions;
    private readonly VersionInstaller _installer;

    public LoaderInstaller(MinecraftVersionService versions, VersionInstaller installer)
    {
        _versions = versions;
        _installer = installer;
    }

    public static readonly LoaderOption[] Loaders =
    {
        new() { Name = "Vanilla（原版）", Key = "", NeedsLoaderVersion = false },
        new() { Name = "Fabric", Key = "fabric", NeedsLoaderVersion = true },
        new() { Name = "Quilt", Key = "quilt", NeedsLoaderVersion = true },
        new() { Name = "Forge", Key = "forge", NeedsLoaderVersion = true },
        new() { Name = "NeoForge", Key = "neoforge", NeedsLoaderVersion = true }
    };

    // ---------------------------------------------------------------- 查询可用加载器版本

    public async Task<List<LoaderVersionOption>> GetLoaderVersionsAsync(string? loaderKey, string gameVersion,
        CancellationToken ct = default)
    {
        var key = (loaderKey ?? "").ToLowerInvariant();
        var list = new List<LoaderVersionOption>();
        if (string.IsNullOrWhiteSpace(gameVersion)) return list;

        try
        {
            if (key is "fabric" or "quilt")
            {
                var baseUrl = key == "quilt" ? "https://meta.quiltmc.org/v3" : "https://meta.fabricmc.net/v2";
                var url = $"{baseUrl}/versions/loader/{gameVersion}";
                using var doc = JsonDocument.Parse(await Http.GetStringAsync(url, ct));
                var index = 0;

                foreach (var element in doc.RootElement.EnumerateArray())
                {
                    var loader = element.TryGetProperty("loader", out var l) ? l : element;
                    var ver = loader.TryGetProperty("version", out var v) ? v.GetString() : null;
                    if (string.IsNullOrEmpty(ver)) continue;

                    var stable = loader.TryGetProperty("stable", out var s) && s.ValueKind == JsonValueKind.True;
                    // Quilt meta 无 stable 字段：取最新一个标为推荐
                    if (key == "quilt" && index == 0) stable = true;

                    list.Add(new LoaderVersionOption { Version = ver, Stable = stable });
                    index++;
                    if (list.Count >= 40) break;
                }
            }
            else if (key == "forge")
            {
                list = await GetForgeVersionsAsync(gameVersion, ct);
            }
            else if (key == "neoforge")
            {
                list = await GetNeoForgeVersionsAsync(gameVersion, ct);
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"获取 {loaderKey} 版本列表失败：{ex.Message}");
        }

        return list;
    }

    private static async Task<List<LoaderVersionOption>> GetForgeVersionsAsync(string gameVersion, CancellationToken ct)
    {
        var list = new List<LoaderVersionOption>();

        // 1) 官方 promotions（推荐 / 最新）
        try
        {
            var json = await Http.GetStringAsync(
                "https://files.minecraftforge.net/net/minecraftforge/forge/promotions_slim.json", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("promos", out var promos))
            {
                foreach (var suffix in new[] { "-recommended", "-latest" })
                {
                    if (!promos.TryGetProperty(gameVersion + suffix, out var p)) continue;
                    var ver = p.GetString();
                    if (string.IsNullOrEmpty(ver)) continue;
                    if (list.Any(x => x.Version == ver)) continue;
                    list.Add(new LoaderVersionOption { Version = ver, Stable = suffix == "-recommended" });
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("Forge promotions 获取失败：" + ex.Message);
        }

        // 2) 回退：maven-metadata.xml 前缀匹配
        if (list.Count == 0)
        {
            try
            {
                var xml = await Http.GetStringAsync(
                    "https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml", ct);
                foreach (Match m in Regex.Matches(xml, "<version>([^<]+)</version>"))
                {
                    var full = m.Groups[1].Value;
                    if (!full.StartsWith(gameVersion + "-", StringComparison.OrdinalIgnoreCase)) continue;
                    var ver = full[(gameVersion.Length + 1)..];
                    if (list.Any(x => x.Version == ver)) continue;
                    list.Add(new LoaderVersionOption { Version = ver, Stable = false });
                }

                list = list.Reverse<LoaderVersionOption>().Take(40).ToList();
            }
            catch (Exception ex)
            {
                Log.Warn("Forge maven 版本列表获取失败：" + ex.Message);
            }
        }

        return list;
    }

    private static async Task<List<LoaderVersionOption>> GetNeoForgeVersionsAsync(string gameVersion, CancellationToken ct)
    {
        var prefix = NeoForgePrefix(gameVersion);
        if (string.IsNullOrEmpty(prefix)) return new List<LoaderVersionOption>();

        var all = new List<string>();

        // 1) 官方 API
        try
        {
            var json = await Http.GetStringAsync(
                "https://maven.neoforged.net/api/maven/versions/releases/net/neoforged/neoforge", ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("versions", out var versions) &&
                versions.ValueKind == JsonValueKind.Array)
            {
                foreach (var v in versions.EnumerateArray())
                {
                    var s = v.GetString();
                    if (!string.IsNullOrEmpty(s)) all.Add(s);
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warn("NeoForge 版本 API 获取失败：" + ex.Message);
        }

        // 2) 回退 maven-metadata（BMCLAPI 镜像）
        if (all.Count == 0)
        {
            try
            {
                var xml = await Http.GetStringAsync(
                    "https://maven.neoforged.net/releases/net/neoforged/neoforge/maven-metadata.xml", ct);
                foreach (Match m in Regex.Matches(xml, "<version>([^<]+)</version>"))
                    all.Add(m.Groups[1].Value);
            }
            catch (Exception ex)
            {
                Log.Warn("NeoForge maven-metadata 获取失败：" + ex.Message);
            }
        }

        return ParseVersionSort(all.Where(v => v == prefix || v.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .Take(60)
            .Select((v, i) => new LoaderVersionOption { Version = v, Stable = i == 0 })
            .ToList();
    }

    /// <summary>1.21.1 -> 21.1 ；1.20.4 -> 20.4 ；1.21 -> 21.0</summary>
    public static string NeoForgePrefix(string mcVersion)
    {
        var parts = (mcVersion ?? "").Split('.');
        if (parts.Length < 2) return "";
        var minor = parts[1];
        var patch = parts.Length > 2 ? parts[2] : "0";
        return $"{minor}.{patch}";
    }

    private static IEnumerable<string> ParseVersionSort(IEnumerable<string> versions)
        => versions
            .Select(v => (v, key: v.Split('.').Select(x => int.TryParse(x, out var n) ? n : 0).ToArray()))
            .OrderByDescending(x => x.key.ElementAtOrDefault(0))
            .ThenByDescending(x => x.key.ElementAtOrDefault(1))
            .ThenByDescending(x => x.key.ElementAtOrDefault(2))
            .Select(x => x.v);

    // ---------------------------------------------------------------- 安装

    /// <summary>安装「游戏版本 + 加载器 + 加载器版本」，返回最终可用（可启动）的版本 id</summary>
    public async Task<string?> InstallAsync(string gameVersion, string? loaderKey, string? loaderVersion,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        var key = (loaderKey ?? "").ToLowerInvariant();

        try
        {
            progress?.Report(new InstallProgress { Stage = "准备原版", Percent = 2, Detail = gameVersion });
            if (!await EnsureVanillaAsync(gameVersion, progress, ct))
            {
                Log.Error($"原版 {gameVersion} 未就绪，无法继续安装加载器。");
                return null;
            }

            switch (key)
            {
                case "":
                case "vanilla":
                    progress?.Report(new InstallProgress { Stage = "安装完成", Percent = 100, Detail = gameVersion });
                    return gameVersion;

                case "fabric":
                case "quilt":
                    return await InstallMetaLoaderAsync(gameVersion, key, loaderVersion, progress, ct);

                case "forge":
                case "neoforge":
                    return await InstallViaInstallerAsync(gameVersion, key, loaderVersion, progress, ct);

                default:
                    Log.Error("未知的加载器：" + loaderKey);
                    return null;
            }
        }
        catch (OperationCanceledException)
        {
            Log.Warn("加载器安装已取消。");
            return null;
        }
        catch (Exception ex)
        {
            Log.Error($"安装加载器失败：{ex.Message}");
            return null;
        }
    }

    private async Task<bool> EnsureVanillaAsync(string gameVersion, IProgress<InstallProgress>? progress,
        CancellationToken ct)
    {
        if (MinecraftVersionService.IsInstalled(gameVersion)) return true;

        await _versions.GetManifestAsync(ct: ct);
        var summary = _versions.FindSummary(gameVersion);

        Log.Info($"原版 {gameVersion} 未安装，先安装原版…");
        return await _installer.InstallAsync(gameVersion, summary?.Url, progress, ct);
    }

    /// <summary>Fabric / Quilt：通过 meta 拉取 profile json（inheritsFrom 原版），并下载其依赖库</summary>
    private async Task<string?> InstallMetaLoaderAsync(string gameVersion, string key, string? loaderVersion,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var baseUrl = key == "quilt" ? "https://meta.quiltmc.org/v3" : "https://meta.fabricmc.net/v2";

        var loaderVer = loaderVersion;
        if (string.IsNullOrWhiteSpace(loaderVer))
        {
            var options = await GetLoaderVersionsAsync(key, gameVersion, ct);
            loaderVer = options.FirstOrDefault()?.Version;
        }

        if (string.IsNullOrWhiteSpace(loaderVer))
        {
            Log.Error($"{key} 没有适用于 {gameVersion} 的加载器版本。");
            return null;
        }

        progress?.Report(new InstallProgress { Stage = $"获取 {key} 安装配置", Percent = 92, Detail = loaderVer });

        var profileUrl = $"{baseUrl}/versions/loader/{gameVersion}/{loaderVer}/profile/json";
        var profileJson = await Http.GetStringAsync(profileUrl, ct);

        using var doc = JsonDocument.Parse(profileJson);
        var root = doc.RootElement;

        var id = root.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrWhiteSpace(id))
            id = key == "quilt"
                ? $"quilt-loader-{loaderVer}-{gameVersion}"
                : $"fabric-loader-{loaderVer}-{gameVersion}";

        // 1) 依赖库
        var items = new List<DownloadItem>();
        if (root.TryGetProperty("libraries", out var libs) && libs.ValueKind == JsonValueKind.Array)
        {
            foreach (var lib in libs.EnumerateArray())
            {
                var name = lib.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrEmpty(name)) continue;

                var libBase = lib.TryGetProperty("url", out var u) ? u.GetString() : null;
                if (string.IsNullOrEmpty(libBase)) libBase = "https://libraries.minecraft.net/";
                if (!libBase!.EndsWith('/')) libBase += "/";

                var path = VersionInstaller.MavenToPath(name);
                if (path == name) continue;

                items.Add(new DownloadItem(libBase + path,
                    Path.Combine(AppPaths.LibrariesDir, path.Replace('/', Path.DirectorySeparatorChar))));
            }
        }

        if (items.Count > 0)
        {
            progress?.Report(new InstallProgress { Stage = $"下载 {key} 依赖库", Percent = 94, Detail = $"{items.Count} 个文件" });
            var result = await Downloader.DownloadBatchAsync(items, Math.Clamp(ConfigService.Current.DownloadThreads, 1, 32),
                null, ct);
            Log.Info($"{key} 依赖库下载完成：成功 {result.ok}，失败 {result.failed}");
        }

        // 2) 写版本 json（文件名必须与 id 一致，才能被版本扫描识别）
        var versionDir = Path.Combine(AppPaths.VersionsDir, id);
        Directory.CreateDirectory(versionDir);
        await File.WriteAllTextAsync(Path.Combine(versionDir, id + ".json"), profileJson, ct);

        if (!MinecraftVersionService.IsInstalled(id))
        {
            Log.Error($"{key} 版本 json 写入失败：{id}");
            return null;
        }

        progress?.Report(new InstallProgress { Stage = "安装完成", Percent = 100, Detail = id });
        Log.Info($"{key} {loaderVer} 已安装到版本 {id}（继承 {gameVersion}）");
        return id;
    }

    /// <summary>Forge / NeoForge：下载 installer 并执行 --installClient</summary>
    private async Task<string?> InstallViaInstallerAsync(string gameVersion, string key, string? loaderVersion,
        IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var loaderVer = loaderVersion;
        if (string.IsNullOrWhiteSpace(loaderVer))
        {
            var options = await GetLoaderVersionsAsync(key, gameVersion, ct);
            loaderVer = options.FirstOrDefault()?.Version;
        }

        if (string.IsNullOrWhiteSpace(loaderVer))
        {
            Log.Error($"{key} 没有适用于 {gameVersion} 的安装器版本。");
            return null;
        }

        string installerUrl;
        if (key == "forge")
        {
            var full = $"{gameVersion}-{loaderVer}";
            installerUrl = $"https://maven.minecraftforge.net/net/minecraftforge/forge/{full}/forge-{full}-installer.jar";
        }
        else
        {
            installerUrl = $"https://maven.neoforged.net/releases/net/neoforged/neoforge/{loaderVer}/neoforge-{loaderVer}-installer.jar";
        }

        var dir = Path.Combine(AppPaths.BaseDir, "installers");
        Directory.CreateDirectory(dir);
        var jar = Path.Combine(dir, $"{key}-{loaderVer}-installer.jar");

        progress?.Report(new InstallProgress { Stage = $"下载 {key} 安装器", Percent = 90, Detail = loaderVer });
        var ok = await Downloader.DownloadFileAsync(installerUrl, jar, 0, null, ct);
        if (!ok)
        {
            Log.Error($"{key} 安装器下载失败（官方源与 BMCLAPI 镜像均不可用）。");
            return null;
        }

        var java = FindInstallerJava();
        if (string.IsNullOrEmpty(java))
        {
            Log.Error($"{key} 安装器需要 Java 运行时，请先在「设置」中安装 JRE。");
            return null;
        }

        var before = SnapshotVersions();
        progress?.Report(new InstallProgress { Stage = $"运行 {key} 安装器", Percent = 92, Detail = "可能需要数分钟" });

        var okRun = await RunInstallerAsync(java, jar, ct);
        var added = SnapshotVersions().Where(v => !before.Contains(v)).ToList();

        if (added.Count == 0)
        {
            // 兜底：按命名约定探测
            var guess = key == "forge" ? $"{gameVersion}-forge-{loaderVer}" : $"neoforge-{loaderVer}";
            if (MinecraftVersionService.IsInstalled(guess)) added.Add(guess);
        }

        if (added.Count == 0)
        {
            Log.Error($"{key} 安装器执行{(okRun ? "完成但未生成版本目录" : "失败")}，详见日志。");
            return null;
        }

        var newId = added.OrderByDescending(File.GetLastWriteTime).First();
        progress?.Report(new InstallProgress { Stage = "安装完成", Percent = 100, Detail = newId });
        Log.Info($"{key} {loaderVer} 安装完成，版本 id：{newId}");
        return newId;
    }

    private static string? FindInstallerJava()
    {
        if (JavaFinder.Installed.Count == 0) JavaFinder.Scan();

        var list = JavaFinder.Installed.Where(j => j.Major >= 8).ToList();
        return list.OrderByDescending(j => j.Major).FirstOrDefault()?.Path
               ?? JavaFinder.Resolve(0);
    }

    private static async Task<bool> RunInstallerAsync(string java, string jar, CancellationToken ct)
    {
        var args = new List<List<string>>
        {
            new() { "-jar", jar, "--installClient", AppPaths.GameDir },
            new() { "-jar", jar, "--installClient" },
            new() { "-jar", jar, "-installClient" }
        };

        foreach (var argList in args)
        {
            try
            {
                var psi = new ProcessStartInfo(java)
                {
                    WorkingDirectory = AppPaths.GameDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };
                foreach (var a in argList) psi.ArgumentList.Add(a);

                Log.Info("运行安装器：" + java + " " + string.Join(' ', argList));

                using var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                p.OutputDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Info("[installer] " + e.Data); };
                p.ErrorDataReceived += (_, e) => { if (!string.IsNullOrWhiteSpace(e.Data)) Log.Warn("[installer] " + e.Data); };

                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromMinutes(10));
                await p.WaitForExitAsync(timeout.Token);

                if (p.ExitCode == 0) return true;
                Log.Warn($"安装器退出码 {p.ExitCode}，尝试其他参数形式。");
            }
            catch (OperationCanceledException)
            {
                Log.Warn("安装器执行超时或被取消。");
            }
            catch (Exception ex)
            {
                Log.Warn("安装器执行异常：" + ex.Message);
            }
        }

        return false;
    }

    private static HashSet<string> SnapshotVersions()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!Directory.Exists(AppPaths.VersionsDir)) return set;
            foreach (var dir in Directory.GetDirectories(AppPaths.VersionsDir))
            {
                var id = Path.GetFileName(dir);
                if (File.Exists(Path.Combine(dir, id + ".json"))) set.Add(id);
            }
        }
        catch { }
        return set;
    }
}
