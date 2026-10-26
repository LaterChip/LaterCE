using LaterCE.Models;

namespace LaterCE.Services;

public class ModrinthService
{
    public const string BaseUrl = "https://api.modrinth.com/v2";

    public async Task<ModrinthSearchResult> SearchAsync(string query, string projectType = "mod",
        string? loader = null, string? gameVersion = null, int limit = 20, int offset = 0,
        string sortIndex = "relevance", CancellationToken ct = default)
    {
        var facets = new List<string>();
        if (!string.IsNullOrEmpty(projectType)) facets.Add($"[\"project_type:{projectType}\"]");
        if (!string.IsNullOrEmpty(loader)) facets.Add($"[\"categories:{loader}\"]");
        if (!string.IsNullOrEmpty(gameVersion)) facets.Add($"[\"versions:{gameVersion}\"]");

        var url = $"{BaseUrl}/search?query={Uri.EscapeDataString(query ?? "")}&limit={limit}&offset={offset}&index={sortIndex}";
        if (facets.Count > 0)
            url += "&facets=" + Uri.EscapeDataString("[" + string.Join(",", facets) + "]");

        return await Http.GetJsonAsync<ModrinthSearchResult>(url, ct) ?? new ModrinthSearchResult();
    }

    public async Task<List<ModrinthVersion>> GetVersionsAsync(string projectId, string? loader = null,
        string? gameVersion = null, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/project/{projectId}/version";
        var query = new List<string>();

        if (!string.IsNullOrEmpty(loader))
            query.Add("loaders=" + Uri.EscapeDataString($"[\"{loader}\"]"));
        if (!string.IsNullOrEmpty(gameVersion))
            query.Add("game_versions=" + Uri.EscapeDataString($"[\"{gameVersion}\"]"));

        if (query.Count > 0) url += "?" + string.Join("&", query);

        return await Http.GetJsonAsync<List<ModrinthVersion>>(url, ct) ?? new List<ModrinthVersion>();
    }

    public static ModrinthFile? PickPrimaryFile(ModrinthVersion version)
        => version.Files.FirstOrDefault(f => f.Primary) ?? version.Files.FirstOrDefault();

    /// <summary>下载版本文件到指定目录，返回保存路径</summary>
    public async Task<string?> DownloadAsync(ModrinthVersion version, string destDir,
        string? renameTo = null, CancellationToken ct = default)
    {
        var file = PickPrimaryFile(version);
        if (file == null || string.IsNullOrEmpty(file.Url)) return null;

        Directory.CreateDirectory(destDir);
        var fileName = string.IsNullOrEmpty(renameTo) ? file.Filename : renameTo;
        var dest = Path.Combine(destDir, fileName);

        var ok = await Downloader.DownloadFileAsync(file.Url, dest, file.Size, null, ct);
        return ok ? dest : null;
    }
}
