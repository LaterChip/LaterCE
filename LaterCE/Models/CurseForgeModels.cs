using System.Text.Json.Serialization;

namespace LaterCE.Models;

public class CfSearchResponse
{
    [JsonPropertyName("data")] public List<CfMod> Data { get; set; } = new();
    [JsonPropertyName("pagination")] public CfPagination? Pagination { get; set; }
}

public class CfPagination
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("pageSize")] public int PageSize { get; set; }
    [JsonPropertyName("resultCount")] public int ResultCount { get; set; }
    [JsonPropertyName("totalCount")] public long TotalCount { get; set; }
}

public class CfMod
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("summary")] public string Summary { get; set; } = "";
    [JsonPropertyName("downloadCount")] public double DownloadCount { get; set; }
    [JsonPropertyName("dateModified")] public string DateModified { get; set; } = "";
    [JsonPropertyName("links")] public CfLinks? Links { get; set; }
    [JsonPropertyName("logo")] public CfLogo? Logo { get; set; }
    [JsonPropertyName("authors")] public List<CfAuthor> Authors { get; set; } = new();
    [JsonPropertyName("latestFiles")] public List<CfFile> LatestFiles { get; set; } = new();
    [JsonPropertyName("categories")] public List<CfCategory> Categories { get; set; } = new();

    public string IconUrl => Logo?.ThumbnailUrl ?? Logo?.Url ?? "";
    public string AuthorName => Authors.Count > 0 ? Authors[0].Name : "";

    public string DownloadsText => DownloadCount switch
    {
        >= 100_000_000 => $"{DownloadCount / 100_000_000d:F1} 亿次下载",
        >= 10_000 => $"{DownloadCount / 10_000d:F1} 万次下载",
        _ => $"{DownloadCount:F0} 次下载"
    };

    public string MetaText => string.IsNullOrWhiteSpace(AuthorName)
        ? DownloadsText
        : $"{DownloadsText} · by {AuthorName}";
}

public class CfLinks
{
    [JsonPropertyName("websiteUrl")] public string WebsiteUrl { get; set; } = "";
}

public class CfLogo
{
    [JsonPropertyName("thumbnailUrl")] public string ThumbnailUrl { get; set; } = "";
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

public class CfAuthor
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class CfCategory
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
}

public class CfFile
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("modId")] public int ModId { get; set; }
    [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
    [JsonPropertyName("downloadUrl")] public string? DownloadUrl { get; set; }
    [JsonPropertyName("fileLength")] public long FileLength { get; set; }
    [JsonPropertyName("releaseType")] public int ReleaseType { get; set; }
    [JsonPropertyName("gameVersions")] public List<string> GameVersions { get; set; } = new();

    public string ReleaseText => ReleaseType switch
    {
        1 => "正式版",
        2 => "Beta",
        3 => "Alpha",
        _ => ""
    };
}

public class CfFilesResponse
{
    [JsonPropertyName("data")] public List<CfFile> Data { get; set; } = new();
}

/// <summary>CurseForge 整合包 manifest.json</summary>
public class CfPackManifest
{
    [JsonPropertyName("minecraft")] public CfPackMinecraft Minecraft { get; set; } = new();
    [JsonPropertyName("manifestType")] public string ManifestType { get; set; } = "";
    [JsonPropertyName("manifestVersion")] public int ManifestVersion { get; set; }
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("files")] public List<CfPackFileRef> Files { get; set; } = new();
    [JsonPropertyName("overrides")] public string Overrides { get; set; } = "overrides";
}

public class CfPackMinecraft
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("modLoaders")] public List<CfPackLoader> ModLoaders { get; set; } = new();
}

public class CfPackLoader
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("primary")] public bool Primary { get; set; }
}

public class CfPackFileRef
{
    [JsonPropertyName("projectID")] public int ProjectId { get; set; }
    [JsonPropertyName("fileID")] public int FileId { get; set; }
    [JsonPropertyName("required")] public bool Required { get; set; }
}
