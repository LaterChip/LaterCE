using System.Text.Json;
using LaterCE.Models;

namespace LaterCE.Services;

public class MinecraftVersionService
{
    public const string ManifestUrl = "https://piston-meta.mojang.com/mc/game/version_manifest_v2.json";

    public VersionManifest? Manifest { get; private set; }

    private DateTime _fetchedAt;

    /// <summary>已知愚人节玩笑版本（Mojang 每年 4 月 1 日发布）</summary>
    public static readonly HashSet<string> AprilFoolsIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "2.0",
        "15w14a",
        "1.RV-Pre1",
        "3D Shareware v1.34",
        "20w14infinite",
        "22w13oneblockatatime",
        "23w13a_or_b",
        "24w14potato",
        "25w14craftmine",
        "26w14a"
    };

    public static bool IsAprilFools(string id) => AprilFoolsIds.Contains(id);

    public async Task<VersionManifest> GetManifestAsync(bool force = false, CancellationToken ct = default)
    {
        if (!force && Manifest != null && (DateTime.UtcNow - _fetchedAt).TotalMinutes < 10)
            return Manifest;

        Log.Info("正在获取 Minecraft 版本清单…");
        var manifest = await Http.GetJsonAsync<VersionManifest>(ManifestUrl, ct)
                       ?? throw new Exception("版本清单解析失败");
        Manifest = manifest;
        _fetchedAt = DateTime.UtcNow;
        Log.Info($"版本清单加载完成，共 {manifest.Versions.Count} 个版本");
        return manifest;
    }

    public VersionSummary? FindSummary(string id) =>
        Manifest?.Versions.FirstOrDefault(v => string.Equals(v.Id, id, StringComparison.OrdinalIgnoreCase));

    public static string VersionJsonPath(string id) => Path.Combine(AppPaths.VersionsDir, id, id + ".json");

    public static VersionDetail? TryReadLocalDetail(string id)
    {
        try
        {
            var file = VersionJsonPath(id);
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(file), Http.JsonOptions);
        }
        catch (Exception ex)
        {
            Log.Warn($"读取本地版本文件失败 {id}: {ex.Message}");
            return null;
        }
    }

    /// <summary>获取版本详情：优先本地，其次网络</summary>
    public async Task<VersionDetail?> GetVersionDetailAsync(string id, string? explicitUrl = null,
        CancellationToken ct = default)
    {
        var local = TryReadLocalDetail(id);
        if (local != null) return local;

        var url = explicitUrl ?? FindSummary(id)?.Url;
        if (string.IsNullOrEmpty(url))
        {
            if (Manifest == null) await GetManifestAsync(ct: ct);
            url = FindSummary(id)?.Url;
        }

        if (string.IsNullOrEmpty(url)) return null;
        return await Http.GetJsonAsync<VersionDetail>(url, ct);
    }

    /// <summary>扫描已安装版本</summary>
    public List<InstalledVersion> GetInstalled()
    {
        var list = new List<InstalledVersion>();
        if (!Directory.Exists(AppPaths.VersionsDir)) return list;

        foreach (var dir in Directory.GetDirectories(AppPaths.VersionsDir))
        {
            var id = Path.GetFileName(dir);
            var json = Path.Combine(dir, id + ".json");
            if (!File.Exists(json)) continue;

            try
            {
                var detail = JsonSerializer.Deserialize<VersionDetail>(File.ReadAllText(json), Http.JsonOptions);
                var size = 0L;
                var jar = Path.Combine(dir, id + ".jar");
                if (File.Exists(jar)) size += new FileInfo(jar).Length;

                DateTime.TryParse(detail?.ReleaseTime, out var release);

                list.Add(new InstalledVersion
                {
                    Id = id,
                    Type = detail?.Type ?? "release",
                    Loader = DetectLoader(id),
                    VersionFile = json,
                    SizeBytes = size,
                    ReleaseTime = release
                });
            }
            catch (Exception ex)
            {
                Log.Warn($"读取已安装版本失败 {id}: {ex.Message}");
            }
        }

        return list.OrderByDescending(v => v.ReleaseTime).ToList();
    }

    public static bool IsInstalled(string id) => File.Exists(VersionJsonPath(id));

    public static string DetectLoader(string id)
    {
        var l = id.ToLowerInvariant();
        if (l.Contains("neoforge")) return "NeoForge";
        if (l.Contains("fabric")) return "Fabric";
        if (l.Contains("quilt")) return "Quilt";
        if (l.Contains("forge")) return "Forge";
        return "原版";
    }

    /// <summary>按分类筛选版本列表</summary>
    public List<VersionSummary> Filter(VersionManifest manifest, string category)
    {
        var all = manifest.Versions;
        return category switch
        {
            "latest" => all.Where(v => v.Id == manifest.Latest.Release || v.Id == manifest.Latest.Snapshot).Take(2).ToList(),
            "release" => all.Where(v => v.Type == "release").ToList(),
            "snapshot" => all.Where(v => v.Type == "snapshot" && !IsAprilFools(v.Id)).ToList(),
            "old" => all.Where(v => v.Type is "old_beta" or "old_alpha").ToList(),
            "fools" => all.Where(v => IsAprilFools(v.Id)).ToList(),
            _ => all.ToList()
        };
    }
}
