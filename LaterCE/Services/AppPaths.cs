using LaterCE.Models;

namespace LaterCE.Services;

public static class AppPaths
{
    public static string BaseDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LaterCE");

    public static string ConfigFile => Path.Combine(BaseDir, "config.json");
    public static string LogsDir => Path.Combine(BaseDir, "logs");
    public static string JavaDir => Path.Combine(BaseDir, "java");

    /// <summary>游戏根目录（即 .minecraft）</summary>
    public static string GameDir => ConfigService.Current.GameDirectory;
    public static string VersionsDir => Path.Combine(GameDir, "versions");
    public static string LibrariesDir => Path.Combine(GameDir, "libraries");
    public static string AssetsDir => Path.Combine(GameDir, "assets");

    /// <summary>某个版本对应的游戏工作目录（版本隔离时独立）</summary>
    public static string InstanceDir(string versionId)
        => ConfigService.Current.VersionIsolation
            ? Path.Combine(VersionsDir, versionId, "game")
            : GameDir;

    public static void Ensure()
    {
        Directory.CreateDirectory(BaseDir);
        Directory.CreateDirectory(LogsDir);
    }
}
