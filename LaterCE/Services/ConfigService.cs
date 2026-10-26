using System.Text.Json;
using LaterCE.Models;

namespace LaterCE.Services;

public static class ConfigService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static AppConfig Current { get; private set; } = new();

    public static event Action? Changed;

    public static void Load()
    {
        try
        {
            if (File.Exists(AppPaths.ConfigFile))
            {
                var json = File.ReadAllText(AppPaths.ConfigFile);
                var cfg = JsonSerializer.Deserialize<AppConfig>(json, Options);
                if (cfg != null) Current = cfg;
            }
        }
        catch (Exception ex)
        {
            Log.Error("读取配置失败，使用默认配置：" + ex.Message);
            Current = new AppConfig();
        }

        if (string.IsNullOrWhiteSpace(Current.GameDirectory))
            Current.GameDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

        if (Current.DownloadThreads < 1) Current.DownloadThreads = 8;
        if (Current.MaxMemoryMb < 512) Current.MaxMemoryMb = 4096;
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.BaseDir);
            File.WriteAllText(AppPaths.ConfigFile, JsonSerializer.Serialize(Current, Options));
            Changed?.Invoke();
        }
        catch (Exception ex)
        {
            Log.Error("保存配置失败：" + ex.Message);
        }
    }
}
