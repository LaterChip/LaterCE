using System.Text.Json;
using LaterCE.Models;

namespace LaterCE.Services;

public class InstallProgress
{
    public string Stage { get; set; } = "";
    public double Percent { get; set; }
    public string Detail { get; set; } = "";
}

public class VersionInstaller
{
    private readonly MinecraftVersionService _versions;

    public VersionInstaller(MinecraftVersionService versions) => _versions = versions;

    public static bool IsRulesAllowed(List<Rule>? rules)
    {
        if (rules == null || rules.Count == 0) return true;

        var allowed = false;
        foreach (var rule in rules)
        {
            if (!RuleMatches(rule)) continue;
            allowed = rule.Action == "allow";
        }

        return allowed;
    }

    private static bool RuleMatches(Rule rule)
    {
        if (rule.Os != null)
        {
            if (!string.IsNullOrEmpty(rule.Os.Name) && rule.Os.Name != "windows") return false;

            if (!string.IsNullOrEmpty(rule.Os.Arch))
            {
                var arch = Environment.Is64BitOperatingSystem ? "x86_64" : "x86";
                if (!string.Equals(rule.Os.Arch, arch, StringComparison.OrdinalIgnoreCase)) return false;
            }
        }

        // 启动器不启用额外 feature（demo / 自定义分辨率等）
        if (rule.Features is { Count: > 0 })
        {
            foreach (var feature in rule.Features)
                if (feature.Value) return false;
        }

        return true;
    }

    public static string MavenToPath(string name)
    {
        var parts = name.Split(':');
        if (parts.Length < 3) return name;

        var group = parts[0].Replace('.', '/');
        var artifact = parts[1];
        var version = parts[2];
        var classifier = parts.Length > 3 ? "-" + parts[3] : "";
        return $"{group}/{artifact}/{version}/{artifact}-{version}{classifier}.jar";
    }

    public static string ResolveLibraryPath(Library lib)
    {
        var path = lib.Downloads.Artifact?.Path;
        return string.IsNullOrEmpty(path) ? MavenToPath(lib.Name) : path;
    }

    /// <summary>下载并安装一个 Minecraft 版本（含依赖库、资源文件）</summary>
    public async Task<bool> InstallAsync(string versionId, string? url = null,
        IProgress<InstallProgress>? progress = null, CancellationToken ct = default)
    {
        var threads = Math.Clamp(ConfigService.Current.DownloadThreads, 1, 64);

        try
        {
            progress?.Report(new InstallProgress { Stage = "获取版本元数据", Percent = 2 });
            await _versions.GetManifestAsync(ct: ct);

            var summary = _versions.FindSummary(versionId);
            var metaUrl = url ?? summary?.Url;

            var versionDir = Path.Combine(AppPaths.VersionsDir, versionId);
            Directory.CreateDirectory(versionDir);
            var jsonPath = Path.Combine(versionDir, versionId + ".json");

            VersionDetail? detail;
            if (!string.IsNullOrEmpty(metaUrl) && (!File.Exists(jsonPath) || !string.IsNullOrEmpty(url)))
            {
                var raw = await Http.GetStringAsync(metaUrl, ct);
                await File.WriteAllTextAsync(jsonPath, raw, ct);
                detail = JsonSerializer.Deserialize<VersionDetail>(raw, Http.JsonOptions);
            }
            else if (File.Exists(jsonPath))
            {
                detail = JsonSerializer.Deserialize<VersionDetail>(await File.ReadAllTextAsync(jsonPath, ct), Http.JsonOptions);
            }
            else
            {
                Log.Error($"无法获取版本 {versionId} 的元数据（请检查网络或镜像设置）");
                return false;
            }

            if (detail == null)
            {
                Log.Error($"版本元数据解析失败：{versionId}");
                return false;
            }

            Log.Info($"开始安装版本 {versionId}（类型：{detail.Type}）");
            progress?.Report(new InstallProgress { Stage = "版本元数据就绪", Percent = 6, Detail = versionId });

            // 1. 客户端 jar
            var client = detail.Downloads.Client;
            if (client != null && !string.IsNullOrEmpty(client.Url))
            {
                var jarPath = Path.Combine(versionDir, versionId + ".jar");
                var ok = await Downloader.DownloadFileAsync(client.Url, jarPath, client.Size, (d, t) =>
                {
                    progress?.Report(new InstallProgress
                    {
                        Stage = "下载客户端 jar",
                        Percent = 6 + 8 * (t > 0 ? (double)d / t : 0)
                    });
                }, ct);

                if (!ok) Log.Warn("客户端 jar 下载失败，稍后可在版本页重试。");
            }

            // 2. 依赖库
            progress?.Report(new InstallProgress { Stage = "下载依赖库", Percent = 15 });
            var libItems = new List<DownloadItem>();
            foreach (var lib in detail.Libraries)
            {
                if (!IsRulesAllowed(lib.Rules)) continue;

                var path = ResolveLibraryPath(lib);
                var libUrl = lib.Downloads.Artifact?.Url;
                if (string.IsNullOrEmpty(libUrl) && !string.IsNullOrEmpty(path))
                    libUrl = "https://libraries.minecraft.net/" + path;

                if (!string.IsNullOrEmpty(libUrl) && !string.IsNullOrEmpty(path))
                {
                    libItems.Add(new DownloadItem(libUrl,
                        Path.Combine(AppPaths.LibrariesDir, path.Replace('/', Path.DirectorySeparatorChar)),
                        lib.Downloads.Artifact?.Size ?? 0));
                }

                // 旧版 natives（classifiers）
                if (lib.Natives != null && lib.Natives.TryGetValue("windows", out var classifier) &&
                    lib.Downloads.Classifiers != null &&
                    lib.Downloads.Classifiers.TryGetValue(classifier, out var native) &&
                    !string.IsNullOrEmpty(native.Url))
                {
                    libItems.Add(new DownloadItem(native.Url,
                        Path.Combine(AppPaths.LibrariesDir, native.Path.Replace('/', Path.DirectorySeparatorChar)),
                        native.Size));
                }
            }

            var libResult = await Downloader.DownloadBatchAsync(libItems, threads, (done, total, bytes, totalBytes) =>
            {
                progress?.Report(new InstallProgress
                {
                    Stage = "下载依赖库",
                    Percent = 15 + 25.0 * done / Math.Max(1, total),
                    Detail = $"{done}/{total}"
                });
            }, ct);

            Log.Info($"依赖库下载完成：成功 {libResult.ok}，失败 {libResult.failed}");

            // 3. 资源文件
            var index = detail.AssetIndex;
            if (!string.IsNullOrEmpty(index.Url))
            {
                progress?.Report(new InstallProgress { Stage = "下载资源索引", Percent = 42 });
                var indexPath = Path.Combine(AppPaths.AssetsDir, "indexes", index.Id + ".json");
                await Downloader.DownloadFileAsync(index.Url, indexPath, index.Size, null, ct);

                if (File.Exists(indexPath))
                {
                    var assetIndex = JsonSerializer.Deserialize<AssetIndexFile>(
                        await File.ReadAllTextAsync(indexPath, ct), Http.JsonOptions);

                    if (assetIndex != null && assetIndex.Objects.Count > 0)
                    {
                        var assetItems = assetIndex.Objects.Values
                            .Where(o => !string.IsNullOrEmpty(o.Hash) && o.Hash.Length >= 2)
                            .Select(o => new DownloadItem(
                                $"https://resources.download.minecraft.net/{o.Hash[..2]}/{o.Hash}",
                                Path.Combine(AppPaths.AssetsDir, "objects", o.Hash[..2], o.Hash),
                                o.Size))
                            .ToList();

                        Log.Info($"需要下载 {assetItems.Count} 个资源文件…");
                        var assetResult = await Downloader.DownloadBatchAsync(assetItems, threads,
                            (done, total, bytes, totalBytes) =>
                            {
                                progress?.Report(new InstallProgress
                                {
                                    Stage = "下载游戏资源",
                                    Percent = 44 + 48.0 * done / Math.Max(1, total),
                                    Detail = $"{done}/{total}"
                                });
                            }, ct);

                        Log.Info($"资源文件下载完成：成功 {assetResult.ok}，失败 {assetResult.failed}");
                    }
                }
            }

            // 4. 日志配置
            var logClient = detail.Logging?.Client;
            if (logClient != null && !string.IsNullOrEmpty(logClient.File.Url))
            {
                var logPath = Path.Combine(AppPaths.AssetsDir, "log_configs", logClient.File.Id);
                await Downloader.DownloadFileAsync(logClient.File.Url, logPath, logClient.File.Size, null, ct);
            }

            progress?.Report(new InstallProgress { Stage = "安装完成", Percent = 100, Detail = versionId });
            Log.Info($"版本 {versionId} 安装完成。");
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Warn("安装已取消。");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"安装版本 {versionId} 失败：{ex.Message}");
            return false;
        }
    }
}
