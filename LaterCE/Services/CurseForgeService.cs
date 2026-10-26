using LaterCE.Models;

namespace LaterCE.Services;

public class CurseForgeService
{
    public const string BaseUrl = "https://api.curseforge.com/v1";

    public const int ClassMods = 6;
    public const int ClassModpacks = 4471;
    public const int ClassResourcePacks = 12;
    public const int ClassTexturePacks = 12;
    public const int ClassWorlds = 17;

    public static bool HasKey => !string.IsNullOrWhiteSpace(ConfigService.Current.CurseForgeApiKey);

    private static IDictionary<string, string> Headers => new Dictionary<string, string>
    {
        ["x-api-key"] = ConfigService.Current.CurseForgeApiKey,
        ["Accept"] = "application/json"
    };

    private static void EnsureKey()
    {
        if (!HasKey)
            throw new Exception("未配置 CurseForge API Key。请在「设置」中填写后重试，或改用 Modrinth 源。");
    }

    public async Task<List<CfMod>> SearchAsync(string query, int classId = ClassMods, string? gameVersion = null,
        int pageSize = 20, int index = 0, CancellationToken ct = default)
    {
        EnsureKey();

        var url = $"{BaseUrl}/mods/search?gameId=432&classId={classId}" +
                  $"&searchFilter={Uri.EscapeDataString(query ?? "")}" +
                  $"&sortField=2&sortOrder=desc&pageSize={pageSize}&index={index}";

        if (!string.IsNullOrEmpty(gameVersion))
            url += $"&gameVersion={Uri.EscapeDataString(gameVersion)}";

        var result = await Http.GetJsonAsync<CfSearchResponse>(url, Headers, ct);
        return result?.Data ?? new List<CfMod>();
    }

    public async Task<List<CfFile>> GetFilesAsync(int modId, string? gameVersion = null,
        CancellationToken ct = default)
    {
        EnsureKey();

        var url = $"{BaseUrl}/mods/{modId}/files?pageSize=50";
        if (!string.IsNullOrEmpty(gameVersion))
            url += $"&gameVersion={Uri.EscapeDataString(gameVersion)}";

        var result = await Http.GetJsonAsync<CfFilesResponse>(url, Headers, ct);
        return result?.Data ?? new List<CfFile>();
    }

    public async Task<CfMod?> GetModAsync(int modId, CancellationToken ct = default)
    {
        EnsureKey();
        var result = await Http.GetJsonAsync<CfModResponse>($"{BaseUrl}/mods/{modId}", Headers, ct);
        return result?.Data;
    }

    public static string? PickDownloadUrl(CfFile file)
    {
        if (!string.IsNullOrEmpty(file.DownloadUrl)) return file.DownloadUrl;

        // 作者未授权第三方 API 分发时，用项目页链接兜底
        return null;
    }

    public async Task<string?> DownloadFileAsync(CfFile file, string destDir, CancellationToken ct = default)
    {
        var url = PickDownloadUrl(file);
        if (string.IsNullOrEmpty(url))
        {
            Log.Warn($"文件 {file.FileName} 未提供直链（作者禁止第三方下载），请在浏览器中手动下载。");
            return null;
        }

        Directory.CreateDirectory(destDir);
        var dest = Path.Combine(destDir, file.FileName);
        var ok = await Downloader.DownloadFileAsync(url, dest, file.FileLength, null, ct);
        return ok ? dest : null;
    }
}

public class CfModResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("data")]
    public CfMod? Data { get; set; }
}
