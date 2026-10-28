using LaterCE.Pages;
using LaterCE.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace LaterCE;

public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        Title = "Later CE";

        SetupTitleBar();
        SetupWindowSize();

        AppState.AccountsChanged += () => DispatcherQueue.TryEnqueue(RefreshAccountFooter);
        AppState.Launcher.Exited += _ => DispatcherQueue.TryEnqueue(OnGameExited);
        AppState.Launcher.OutputReceived += line => Log.Info("[游戏] " + line);

        RefreshAccountFooter();
        ContentFrame.Navigate(typeof(HomePage));
        Nav.SelectedItem = Nav.MenuItems[0];

        Closed += (_, _) => ConfigService.Save();
    }

    private void SetupTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var titleBar = AppWindow.TitleBar;
            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
            titleBar.ButtonForegroundColor = ColorHelper.FromArgb(255, 31, 41, 55);
            titleBar.ButtonInactiveForegroundColor = ColorHelper.FromArgb(255, 138, 148, 163);
            titleBar.ButtonHoverBackgroundColor = ColorHelper.FromArgb(24, 0, 0, 0);
            titleBar.ButtonHoverForegroundColor = ColorHelper.FromArgb(255, 15, 23, 34);
            titleBar.ButtonPressedBackgroundColor = ColorHelper.FromArgb(45, 0, 0, 0);
        }
        catch (Exception ex)
        {
            Log.Warn("自定义标题栏初始化失败：" + ex.Message);
        }
    }

    private void SetupWindowSize()
    {
        try
        {
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary);
            const int width = 1200;
            const int height = 800;

            var x = area.WorkArea.X + (area.WorkArea.Width - width) / 2;
            var y = area.WorkArea.Y + (area.WorkArea.Height - height) / 2;

            AppWindow.MoveAndResize(new RectInt32(Math.Max(0, x), Math.Max(0, y), width, height));

            SetWindowIcon();
        }
        catch (Exception ex)
        {
            Log.Warn("窗口初始化失败：" + ex.Message);
        }
    }

    /// <summary>标题栏 / 任务栏图标：优先 ico，失败回退 png</summary>
    private void SetWindowIcon()
    {
        var candidates = new[] { "AppIcon.ico", "AppIcon.png" };

        foreach (var name in candidates)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", name);
            if (!File.Exists(path)) continue;

            try
            {
                AppWindow.SetIcon(path);
                return;
            }
            catch (Exception ex)
            {
                Log.Warn($"设置窗口图标失败（{name}）：{ex.Message}");
            }
        }

        Log.Warn("未找到可用的应用图标（Assets/AppIcon.ico）");
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem item) return;

        var type = item.Tag?.ToString() switch
        {
            "versions" => typeof(VersionsPage),
            "mods" => typeof(ModsPage),
            "modpacks" => typeof(ModPacksPage),
            "resourcepacks" => typeof(ResourcePacksPage),
            "shaderpacks" => typeof(ShaderPacksPage),
            "accounts" => typeof(AccountsPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        if (ContentFrame.CurrentSourcePageType != type)
            ContentFrame.Navigate(type);
    }

    public void NavigateTo(string tag)
    {
        foreach (var obj in Nav.MenuItems)
        {
            if (obj is NavigationViewItem item && item.Tag?.ToString() == tag)
            {
                Nav.SelectedItem = item;
                break;
            }
        }
    }

    public void RefreshAccountFooter()
    {
        var account = AppState.ActiveAccount;
        FooterAccountName.Text = account?.Name ?? "未登录";
        FooterAccountType.Text = account?.TypeDisplay ?? "点击前往账户页";
    }

    private async void LaunchButton_Click(object sender, RoutedEventArgs e)
    {
        await LaunchAsync();
    }

    public async Task LaunchAsync()
    {
        if (AppState.IsGameRunning)
        {
            await ShowDialog("游戏已经在运行", "请先关闭当前游戏窗口，再启动新的实例。");
            return;
        }

        var cfg = ConfigService.Current;
        var versionId = cfg.SelectedVersion;

        if (string.IsNullOrWhiteSpace(versionId) || !MinecraftVersionService.IsInstalled(versionId))
        {
            await ShowDialog("还没有可启动的版本", "请先到「版本管理」页面下载并选择一个游戏版本。");
            NavigateTo("versions");
            return;
        }

        var account = AppState.ActiveAccount;
        if (account == null)
        {
            account = AuthService.CreateOfflineAccount("Player");
            AppState.SetActiveAccount(account);
            Log.Info("未登录账号，已自动创建离线账户 Player。");
        }

        // 正版令牌只有 24 小时且刷新令牌会轮换，启动前刷新一次，否则服务器会判为未验证会话
        if (account.Type == "microsoft")
        {
            if (string.IsNullOrEmpty(account.RefreshToken))
            {
                await ShowDialog("正版账号需要重新登录",
                    "该微软账号缺少刷新令牌，无法续期。请到「账户」页面重新点击正版登录。");
                NavigateTo("accounts");
                return;
            }

            try
            {
                LaunchButton.Content = "刷新正版令牌…";
                account = await AuthService.RefreshMicrosoftAsync(account);
                AppState.SetActiveAccount(account);
                Log.Info("已刷新微软正版令牌：" + account.Name);
            }
            catch (Exception ex)
            {
                Log.Warn("微软正版令牌刷新失败：" + ex.Message);
                LaunchButton.Content = "启动游戏";
                await ShowDialog("正版账号需要重新登录",
                    "微软账号令牌已失效，请到「账户」页面重新点击正版登录。\n\n详情：" + ex.Message);
                NavigateTo("accounts");
                return;
            }
        }
        else if (account.Type == "littleskin" && account.IsExpired)
        {
            await AuthService.RefreshLittleSkinAsync(account);
        }

        LaunchButton.IsEnabled = false;
        LaunchButton.Content = "启动中…";

        var ok = await AppState.Launcher.LaunchAsync(versionId, account);

        if (!ok)
        {
            LaunchButton.Content = "启动游戏";
            LaunchButton.IsEnabled = true;

            var reason = string.IsNullOrWhiteSpace(AppState.Launcher.LastError)
                ? "请到「设置」页查看运行日志，或打开日志文件：\n" + Log.LogFile
                : AppState.Launcher.LastError + "\n\n日志文件：" + Log.LogFile;

            await ShowDialog("启动失败", reason);
        }
        else
        {
            LaunchButton.Content = "游戏运行中";
        }
    }

    private void OnGameExited()
    {
        LaunchButton.Content = "启动游戏";
        LaunchButton.IsEnabled = true;
    }

    public async Task ShowDialog(string title, string content)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
                CloseButtonText = "知道了",
                XamlRoot = RootGrid.XamlRoot
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Warn("弹窗失败：" + ex.Message);
        }
    }
}
