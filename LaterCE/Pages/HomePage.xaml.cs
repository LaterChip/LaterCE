using LaterCE.Models;
using LaterCE.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LaterCE.Pages;

public sealed partial class HomePage : Page
{
    private readonly Queue<string> _lines = new();
    private List<InstalledVersion> _installed = new();
    private bool _suppressSelection;

    public HomePage()
    {
        InitializeComponent();
        Log.OnMessage += OnLogMessage;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        Log.OnMessage -= OnLogMessage;
        Log.OnMessage += OnLogMessage;
        _ = RefreshAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Log.OnMessage -= OnLogMessage;
        base.OnNavigatedFrom(e);
    }

    private void OnLogMessage(string line)
        => DispatcherQueue.TryEnqueue(() => AppendLog(line));

    private void AppendLog(string line)
    {
        _lines.Enqueue(line);
        while (_lines.Count > 200) _lines.Dequeue();
        LogText.Text = string.Join(Environment.NewLine, _lines);
        LogScroll.ChangeView(null, LogScroll.ScrollableHeight, null);
    }

    private async Task RefreshAsync()
    {
        try
        {
            _installed = AppState.Versions.GetInstalled();

            VersionCountText.Text = _installed.Count.ToString();
            VersionHintText.Text = _installed.Count == 0
                ? "尚未下载任何版本"
                : string.Join("、", _installed.Take(2).Select(v => v.Id)) + (_installed.Count > 2 ? " …" : "");

            var java = JavaFinder.Installed;
            if (java.Count == 0) JavaFinder.Scan();
            java = JavaFinder.Installed;

            JavaCountText.Text = java.Count.ToString();
            JavaHintText.Text = java.Count == 0
                ? "未检测到 Java，可在设置中一键安装"
                : $"最高 Java {java.Max(j => j.Major)}";

            // 版本下拉
            _suppressSelection = true;
            VersionCombo.ItemsSource = null;
            VersionCombo.ItemsSource = _installed;
            var selectedId = ConfigService.Current.SelectedVersion;

            if (string.IsNullOrEmpty(selectedId) && _installed.Count > 0)
            {
                selectedId = _installed[0].Id;
                ConfigService.Current.SelectedVersion = selectedId;
                ConfigService.Save();
            }

            var current = _installed.FirstOrDefault(v => v.Id == selectedId);
            if (current != null) VersionCombo.SelectedItem = current;
            _suppressSelection = false;

            var account = AppState.ActiveAccount;
            HeroAccount.Text = account == null ? "未登录 · 点击左侧「账户」登录" : $"{account.Name}（{account.TypeDisplay}）";

            UpdateHeroSubtitle(selectedId);

            // 模组统计
            if (!string.IsNullOrEmpty(selectedId))
            {
                var (mods, packs, saves) = ModManager.CountInstalled(selectedId);
                ModCountText.Text = mods.ToString();
                ModHintText.Text = $"资源包 {packs} · 存档 {saves}";
            }
            else
            {
                ModCountText.Text = "0";
                ModHintText.Text = "未选择版本";
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            Log.Error("主页刷新失败：" + ex.Message);
        }
    }

    private void UpdateHeroSubtitle(string? versionId)
    {
        if (string.IsNullOrEmpty(versionId))
        {
            HeroSubtitle.Text = "还没有可选版本，请到「版本管理」下载游戏本体";
            return;
        }

        var info = _installed.FirstOrDefault(v => v.Id == versionId);
        HeroSubtitle.Text = info == null
            ? "Minecraft Java 版"
            : $"{info.Id} · {info.Type} · {MinecraftVersionService.DetectLoader(info.Id)}";
    }

    private void VersionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection) return;
        if (VersionCombo.SelectedItem is not InstalledVersion version) return;

        ConfigService.Current.SelectedVersion = version.Id;
        ConfigService.Save();
        UpdateHeroSubtitle(version.Id);

        var (mods, packs, saves) = ModManager.CountInstalled(version.Id);
        ModCountText.Text = mods.ToString();
        ModHintText.Text = $"资源包 {packs} · 存档 {saves}";
        Log.Info($"已选择版本：{version.Id}");
    }

    private async void Launch_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Instance != null)
            await MainWindow.Instance.LaunchAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        Log.Info("正在刷新本地状态…");
        await RefreshAsync();
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = AppPaths.LogsDir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开日志目录失败：" + ex.Message);
        }
    }
}
