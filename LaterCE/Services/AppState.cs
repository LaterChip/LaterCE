using LaterCE.Models;

namespace LaterCE.Services;

/// <summary>全局服务容器与状态通知</summary>
public static class AppState
{
    public static MinecraftVersionService Versions { get; } = new();
    public static VersionInstaller Installer { get; } = new(Versions);
    public static LoaderInstaller Loaders { get; } = new(Versions, Installer);
    public static GameLauncher Launcher { get; } = new(Versions);
    public static ModrinthService Modrinth { get; } = new();
    public static CurseForgeService CurseForge { get; } = new();
    public static ModManager Mods { get; } = new();

    public static event Action? VersionsChanged;
    public static event Action? AccountsChanged;

    public static void RaiseVersionsChanged()
    {
        try { VersionsChanged?.Invoke(); } catch { }
    }

    public static void RaiseAccountsChanged()
    {
        try { AccountsChanged?.Invoke(); } catch { }
    }

    public static Account? ActiveAccount => ConfigService.Current.GetActiveAccount();

    public static void SetActiveAccount(Account account)
    {
        var cfg = ConfigService.Current;
        var existing = cfg.Accounts.FirstOrDefault(a => a.Id == account.Id);
        if (existing == null)
        {
            cfg.Accounts.Add(account);
        }
        else if (!ReferenceEquals(existing, account))
        {
            // 刷新令牌后把最新凭据回写到已存在的账号，避免重复添加且保证下次启动可用
            existing.Type = account.Type;
            existing.Name = account.Name;
            existing.Uuid = account.Uuid;
            existing.AccessToken = account.AccessToken;
            existing.RefreshToken = account.RefreshToken;
            existing.ClientToken = account.ClientToken;
            existing.ExpiresAt = account.ExpiresAt;
        }
        cfg.ActiveAccountId = account.Id;
        ConfigService.Save();
        RaiseAccountsChanged();
    }

    public static void RemoveAccount(Account account)
    {
        var cfg = ConfigService.Current;
        cfg.Accounts.RemoveAll(a => a.Id == account.Id);
        if (cfg.ActiveAccountId == account.Id)
            cfg.ActiveAccountId = cfg.Accounts.FirstOrDefault()?.Id ?? "";
        ConfigService.Save();
        RaiseAccountsChanged();
    }

    public static bool IsGameRunning => Launcher.RunningProcess is { HasExited: false };
}
