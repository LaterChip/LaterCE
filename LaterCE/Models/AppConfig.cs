namespace LaterCE.Models;

/// <summary>全局配置，持久化在 %APPDATA%\LaterCE\config.json</summary>
public class AppConfig
{
    public string GameDirectory { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");

    public string JavaPath { get; set; } = "";
    public int MinMemoryMb { get; set; } = 1024;
    public int MaxMemoryMb { get; set; } = 4096;
    public string ExtraJvmArgs { get; set; } = "";
    public string ExtraGameArgs { get; set; } = "";

    /// <summary>使用 BMCLAPI 镜像加速下载</summary>
    public bool UseBmclApi { get; set; } = true;
    public int DownloadThreads { get; set; } = 16;

    public string CurseForgeApiKey { get; set; } = "";

    /// <summary>微软正版登录所用的 Azure 应用 Client ID（旧版公共 ID 已失效，必须自行注册）</summary>
    public string MicrosoftClientId { get; set; } = "";
    public bool VersionIsolation { get; set; } = true;

    public string SelectedVersion { get; set; } = "";
    public string ActiveAccountId { get; set; } = "";

    /// <summary>模组市场首选来源：Modrinth / CurseForge</summary>
    public string PreferredSource { get; set; } = "Modrinth";
    public string PreferredLoader { get; set; } = "fabric";

    public List<Account> Accounts { get; set; } = new();

    public Account? GetActiveAccount()
    {
        if (Accounts.Count == 0) return null;
        return Accounts.FirstOrDefault(a => a.Id == ActiveAccountId) ?? Accounts[0];
    }
}
