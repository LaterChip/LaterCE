using System.Text.Json;

namespace LaterCE.Models;

public class VersionManifest
{
    public LatestVersion Latest { get; set; } = new();
    public List<VersionSummary> Versions { get; set; } = new();
}

public class LatestVersion
{
    public string Release { get; set; } = "";
    public string Snapshot { get; set; } = "";
}

public class VersionSummary
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public string Time { get; set; } = "";
    public string ReleaseTime { get; set; } = "";
    public string Sha1 { get; set; } = "";
}

public class VersionDetail
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string MainClass { get; set; } = "";
    public string Assets { get; set; } = "";
    public string InheritsFrom { get; set; } = "";
    public string MinecraftArguments { get; set; } = "";
    public string ReleaseTime { get; set; } = "";
    public string Time { get; set; } = "";
    public int? ComplianceLevel { get; set; }

    public Arguments Arguments { get; set; } = new();
    public AssetIndexInfo AssetIndex { get; set; } = new();
    public VersionDownloads Downloads { get; set; } = new();
    public List<Library> Libraries { get; set; } = new();
    public JavaVersionInfo JavaVersion { get; set; } = new();
    public LoggingInfo? Logging { get; set; }
}

public class LoggingInfo
{
    public LoggingClient Client { get; set; } = new();
}

public class LoggingClient
{
    public string Argument { get; set; } = "";
    public string Type { get; set; } = "";
    public DownloadInfo File { get; set; } = new();
}

public class Arguments
{
    public List<JsonElement> Game { get; set; } = new();
    public List<JsonElement> Jvm { get; set; } = new();
}

public class AssetIndexInfo
{
    public string Id { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public long Size { get; set; }
    public long TotalSize { get; set; }
    public string Url { get; set; } = "";
}

public class VersionDownloads
{
    public DownloadInfo? Client { get; set; }
    public DownloadInfo? Server { get; set; }
}

public class DownloadInfo
{
    public string Id { get; set; } = "";
    public string Path { get; set; } = "";
    public string Sha1 { get; set; } = "";
    public long Size { get; set; }
    public string Url { get; set; } = "";
}

public class Library
{
    public string Name { get; set; } = "";
    public LibraryDownloads Downloads { get; set; } = new();
    public List<Rule> Rules { get; set; } = new();
    public Dictionary<string, string>? Natives { get; set; }
}

public class LibraryDownloads
{
    public DownloadInfo? Artifact { get; set; }
    public Dictionary<string, DownloadInfo>? Classifiers { get; set; }
}

public class Rule
{
    public string Action { get; set; } = "";
    public OsRule? Os { get; set; }
    public Dictionary<string, bool>? Features { get; set; }
}

public class OsRule
{
    public string? Name { get; set; }
    public string? Version { get; set; }
    public string? Arch { get; set; }
}

public class JavaVersionInfo
{
    public string Component { get; set; } = "";
    public int MajorVersion { get; set; }
}

/// <summary>资源索引（assets/indexes/xx.json）</summary>
public class AssetIndexFile
{
    public Dictionary<string, AssetObject> Objects { get; set; } = new();
}

public class AssetObject
{
    public string Hash { get; set; } = "";
    public long Size { get; set; }
}

#region UI 展示模型

public class VersionListItem
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Url { get; set; } = "";
    public bool IsInstalled { get; set; }

    public string TypeDisplay => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => Type
    };

    public string Badge => IsInstalled ? "已安装" : TypeDisplay;
    public string ActionText => IsInstalled ? "重新下载" : "下载";
    public string SubText { get; set; } = "";
}

public class InstalledVersion
{
    public string Id { get; set; } = "";
    public string Type { get; set; } = "";
    public string Loader { get; set; } = "原版";
    public string VersionFile { get; set; } = "";
    public DateTime ReleaseTime { get; set; }
    public long SizeBytes { get; set; }

    public string TypeDisplay => Type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "远古 Beta",
        "old_alpha" => "远古 Alpha",
        _ => Type
    };

    public string SubText => $"{TypeDisplay} · {Loader} · {SizeText}";
    public string SizeText => SizeBytes <= 0 ? "-" : $"{SizeBytes / 1024d / 1024d:F0} MB";
    public string ReleaseText => ReleaseTime == default ? "" : ReleaseTime.ToLocalTime().ToString("yyyy-MM-dd");
}

#endregion
