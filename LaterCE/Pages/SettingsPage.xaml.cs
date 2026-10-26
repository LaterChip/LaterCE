using LaterCE.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LaterCE.Pages;

public sealed partial class SettingsPage : Page
{
    private bool _javaInstalling;

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadFromConfig();
        RefreshJavaList();
        AboutText.Text = $"Later CE · {typeof(App).Assembly.GetName().Version} · 游戏目录 {AppPaths.GameDir}";
    }

    private void LoadFromConfig()
    {
        var cfg = ConfigService.Current;
        GameDirBox.Text = cfg.GameDirectory;
        MinMemoryBox.Value = cfg.MinMemoryMb;
        MaxMemoryBox.Value = cfg.MaxMemoryMb;
        JvmArgsBox.Text = cfg.ExtraJvmArgs;
        GameArgsBox.Text = cfg.ExtraGameArgs;
        IsolationCheck.IsChecked = cfg.VersionIsolation;
        BmclCheck.IsChecked = cfg.UseBmclApi;
        ThreadsBox.Value = cfg.DownloadThreads;
        CfKeyBox.Text = cfg.CurseForgeApiKey;
        MsClientIdBox.Text = cfg.MicrosoftClientId;
    }

    private void OpenAzureApps_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade");
    }

    private void OpenMsLink_Click(object sender, RoutedEventArgs e)
    {
        OpenUrl("https://microsoft.com/link");
    }

    private void OpenUrl(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开链接失败：" + ex.Message);
        }
    }

    private void RefreshJavaList()
    {
        if (JavaFinder.Installed.Count == 0) JavaFinder.Scan();

        JavaCombo.Items.Clear();
        JavaCombo.Items.Add(new ComboBoxItem { Content = "自动选择（按版本要求匹配）", Tag = "" });

        foreach (var java in JavaFinder.Installed)
            JavaCombo.Items.Add(new ComboBoxItem { Content = java.Display, Tag = java.Path });

        var current = ConfigService.Current.JavaPath ?? "";
        var selectedIndex = 0;
        for (var i = 1; i < JavaCombo.Items.Count; i++)
        {
            if (JavaCombo.Items[i] is ComboBoxItem item &&
                string.Equals(item.Tag?.ToString(), current, StringComparison.OrdinalIgnoreCase))
            {
                selectedIndex = i;
                break;
            }
        }

        JavaCombo.SelectedIndex = selectedIndex;

        JavaStatus.Text = JavaFinder.Installed.Count == 0
            ? "未检测到 Java，点击右侧按钮可自动下载安装 Adoptium JRE。"
            : "已检测到：" + string.Join("，", JavaFinder.Installed.Select(j => $"Java {j.Major}"));
    }

    private void ScanJava_Click(object sender, RoutedEventArgs e)
    {
        JavaFinder.Scan();
        RefreshJavaList();
    }

    private async void InstallJava_Click(object sender, RoutedEventArgs e)
    {
        if (_javaInstalling) return;
        _javaInstalling = true;
        JavaProgress.Value = 0;
        JavaStatus.Text = "正在下载 Java 21 运行时…";

        try
        {
            var progress = new Progress<double>(p => JavaProgress.Value = p);
            var exe = await JavaFinder.InstallJreAsync(21, progress);

            JavaStatus.Text = exe == null ? "安装失败，详情请查看日志。" : $"安装完成：{exe}";
            RefreshJavaList();
        }
        catch (Exception ex)
        {
            Log.Error("安装 Java 失败：" + ex.Message);
            JavaStatus.Text = "安装失败：" + ex.Message;
        }
        finally
        {
            _javaInstalling = false;
        }
    }

    private async void Browse_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");

            if (MainWindow.Instance != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(MainWindow.Instance);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var folder = await picker.PickSingleFolderAsync();
            if (folder != null) GameDirBox.Text = folder.Path;
        }
        catch (Exception ex)
        {
            Log.Warn("选择目录失败：" + ex.Message);
            await ShowDialog("无法打开目录选择器", ex.Message);
        }
    }

    private void OpenGameDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dir = GameDirBox.Text?.Trim();
            if (string.IsNullOrEmpty(dir)) return;

            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开游戏目录失败：" + ex.Message);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var cfg = ConfigService.Current;

        var gameDir = GameDirBox.Text?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            await ShowDialog("游戏目录不能为空", "请填写或选择一个有效的游戏目录。");
            return;
        }

        if (double.IsNaN(MinMemoryBox.Value) || MinMemoryBox.Value < 512)
        {
            await ShowDialog("最小内存不合法", "最小内存至少为 512 MB。");
            return;
        }

        cfg.GameDirectory = gameDir;
        cfg.MinMemoryMb = (int)MinMemoryBox.Value;
        cfg.MaxMemoryMb = (int)(double.IsNaN(MaxMemoryBox.Value) ? cfg.MinMemoryMb : MaxMemoryBox.Value);
        cfg.ExtraJvmArgs = JvmArgsBox.Text?.Trim() ?? "";
        cfg.ExtraGameArgs = GameArgsBox.Text?.Trim() ?? "";
        cfg.VersionIsolation = IsolationCheck.IsChecked == true;
        cfg.UseBmclApi = BmclCheck.IsChecked == true;
        cfg.DownloadThreads = double.IsNaN(ThreadsBox.Value) ? 16 : (int)ThreadsBox.Value;
        cfg.CurseForgeApiKey = CfKeyBox.Text?.Trim() ?? "";
        cfg.MicrosoftClientId = MsClientIdBox.Text?.Trim() ?? "";
        cfg.JavaPath = JavaCombo.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "" : "";

        ConfigService.Save();

        try { Directory.CreateDirectory(cfg.GameDirectory); } catch { }

        JavaStatus.Text = "设置已保存。";
        Log.Info("设置已保存");
        AboutText.Text = $"Later CE · {typeof(App).Assembly.GetName().Version} · 游戏目录 {AppPaths.GameDir}";
    }

    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            Title = "恢复默认设置？",
            Content = "将把游戏目录、内存、下载与 API 配置恢复为初始值，账户列表不受影响。",
            PrimaryButtonText = "恢复默认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        var cfg = ConfigService.Current;
        var defaults = new LaterCE.Models.AppConfig
        {
            Accounts = cfg.Accounts,
            ActiveAccountId = cfg.ActiveAccountId,
            SelectedVersion = cfg.SelectedVersion
        };

        cfg.GameDirectory = defaults.GameDirectory;
        cfg.JavaPath = defaults.JavaPath;
        cfg.MinMemoryMb = defaults.MinMemoryMb;
        cfg.MaxMemoryMb = defaults.MaxMemoryMb;
        cfg.ExtraJvmArgs = defaults.ExtraJvmArgs;
        cfg.ExtraGameArgs = defaults.ExtraGameArgs;
        cfg.UseBmclApi = defaults.UseBmclApi;
        cfg.DownloadThreads = defaults.DownloadThreads;
        cfg.CurseForgeApiKey = defaults.CurseForgeApiKey;
        cfg.MicrosoftClientId = defaults.MicrosoftClientId;
        cfg.VersionIsolation = defaults.VersionIsolation;

        ConfigService.Save();
        LoadFromConfig();
        RefreshJavaList();
        JavaStatus.Text = "已恢复默认设置。";
    }

    private async Task ShowDialog(string title, string content)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "知道了",
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }
}
