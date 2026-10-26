using System.Text.Json;
using LaterCE.Services;

namespace LaterCE.Pages;

/// <summary>资源包 / 整合包等市场页面共用的辅助方法</summary>
internal static class MarketSupport
{
    /// <summary>从实例 ID（如 fabric-loader-0.20.1-1.21.2）解析出基础游戏版本</summary>
    public static string ResolveBaseGameVersion(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return "";

        try
        {
            var json = Path.Combine(AppPaths.VersionsDir, instanceId, instanceId + ".json");
            if (File.Exists(json))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(json));
                foreach (var key in new[] { "inheritsFrom", "jar" })
                {
                    if (doc.RootElement.TryGetProperty(key, out var v) &&
                        v.ValueKind == JsonValueKind.String)
                    {
                        var sv = v.GetString();
                        if (!string.IsNullOrWhiteSpace(sv)) return sv!;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Info("解析基础游戏版本失败：" + ex.Message);
        }

        var parts = instanceId.Split('-');
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var seg = parts[i];
            if (seg.Length > 0 && char.IsDigit(seg[0]) && seg.Contains('.')) return seg;
        }

        return instanceId;
    }

    /// <summary>批量触发图标惰性加载（空 URL / 加载失败时保持占位字形）</summary>
    public static void LoadIcons(IEnumerable<MarketItem> items)
    {
        foreach (var item in items)
            _ = item.LoadIconAsync();
    }

    /// <summary>常用的近期正式版本，供游戏版本筛选下拉补全</summary>
    public static readonly string[] CommonVersions =
    {
        "1.21.4", "1.21.3", "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.2", "1.20.1",
        "1.19.4", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.15.2", "1.12.2"
    };

    public static string FormatDownloads(long downloads) => downloads switch
    {
        >= 100_000_000 => $"{downloads / 100_000_000d:F1} 亿次下载",
        >= 10_000 => $"{downloads / 10_000d:F1} 万次下载",
        _ => $"{downloads} 次下载"
    };
}
