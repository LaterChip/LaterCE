using System.Text.Json.Serialization;

namespace LaterCE.Models;

public class ModrinthSearchResult
{
    [JsonPropertyName("hits")] public List<ModrinthHit> Hits { get; set; } = new();
    [JsonPropertyName("offset")] public int Offset { get; set; }
    [JsonPropertyName("limit")] public int Limit { get; set; }
    [JsonPropertyName("total_hits")] public int TotalHits { get; set; }
}

public class ModrinthHit
{
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("slug")] public string Slug { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("project_type")] public string ProjectType { get; set; } = "";
    [JsonPropertyName("icon_url")] public string? IconUrl { get; set; }
    [JsonPropertyName("downloads")] public long Downloads { get; set; }
    [JsonPropertyName("follows")] public int Follows { get; set; }
    [JsonPropertyName("author")] public string Author { get; set; } = "";
    [JsonPropertyName("categories")] public List<string> Categories { get; set; } = new();
    [JsonPropertyName("display_categories")] public List<string> DisplayCategories { get; set; } = new();
    [JsonPropertyName("versions")] public List<string> Versions { get; set; } = new();
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = new();
    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = new();
    [JsonPropertyName("date_modified")] public string DateModified { get; set; } = "";

    public string DownloadsText => Downloads switch
    {
        >= 100_000_000 => $"{Downloads / 100_000_000d:F1} 亿次下载",
        >= 10_000 => $"{Downloads / 10_000d:F1} 万次下载",
        _ => $"{Downloads} 次下载"
    };

    public string MetaText
    {
        get
        {
            var parts = new List<string> { DownloadsText };
            if (!string.IsNullOrWhiteSpace(Author)) parts.Add("by " + Author);
            if (GameVersions.Count > 0) parts.Add(GameVersions[^1] + "+");
            return string.Join(" · ", parts);
        }
    }
}

public class ModrinthVersion
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("project_id")] public string ProjectId { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("version_number")] public string VersionNumber { get; set; } = "";
    [JsonPropertyName("version_type")] public string VersionType { get; set; } = "";
    [JsonPropertyName("game_versions")] public List<string> GameVersions { get; set; } = new();
    [JsonPropertyName("loaders")] public List<string> Loaders { get; set; } = new();
    [JsonPropertyName("date_published")] public string DatePublished { get; set; } = "";
    [JsonPropertyName("files")] public List<ModrinthFile> Files { get; set; } = new();
    [JsonPropertyName("dependencies")] public List<ModrinthDependency> Dependencies { get; set; } = new();
}

public class ModrinthFile
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("filename")] public string Filename { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("primary")] public bool Primary { get; set; }
    [JsonPropertyName("hashes")] public ModrinthHashes Hashes { get; set; } = new();
}

public class ModrinthHashes
{
    [JsonPropertyName("sha1")] public string Sha1 { get; set; } = "";
    [JsonPropertyName("sha512")] public string Sha512 { get; set; } = "";
}

public class ModrinthDependency
{
    [JsonPropertyName("version_id")] public string? VersionId { get; set; }
    [JsonPropertyName("project_id")] public string? ProjectId { get; set; }
    [JsonPropertyName("dependency_type")] public string DependencyType { get; set; } = "";
}
