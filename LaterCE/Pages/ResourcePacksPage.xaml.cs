using LaterCE.Models;
using LaterCE.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LaterCE.Pages;

/// <summary>资源包市场：打开即列出 Modrinth 全部（热门）资源包，支持搜索与游戏版本筛选</summary>
public sealed partial class ResourcePacksPage : Page
{
    private const string ProjectType = "resourcepack";

    private bool _busy;
    private bool _suppressFilter;
    private string _versionId = "";
    private string _gameVersion = "";
    private string _lastQuery = "";

    public ResourcePacksPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _versionId = ConfigService.Current.SelectedVersion ?? "";
        _gameVersion = MarketSupport.ResolveBaseGameVersion(_versionId);

        TargetVersionText.Text = string.IsNullOrEmpty(_versionId)
            ? "未选择实例"
            : $"目标实例：{_versionId}（{_gameVersion}）";

        BuildVersionFilter();

        // 进入页面即加载热门资源包，无需先输入关键词
        _ = FetchAsync("", "downloads");
    }

    private void BuildVersionFilter()
    {
        _suppressFilter = true;

        var items = new List<ComboBoxItem>
        {
            new() { Content = "全部游戏版本", Tag = "" }
        };

        if (!string.IsNullOrEmpty(_gameVersion))
            items.Add(new ComboBoxItem { Content = $"{_gameVersion}（当前实例）", Tag = _gameVersion });

        foreach (var v in MarketSupport.CommonVersions
                     .Where(v => !string.Equals(v, _gameVersion, StringComparison.OrdinalIgnoreCase)))
            items.Add(new ComboBoxItem { Content = v, Tag = v });

        VersionCombo.ItemsSource = items;
        VersionCombo.SelectedIndex = 0;

        _suppressFilter = false;
    }

    private string SelectedGameVersion()
        => (VersionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

    private async void Version_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilter || !IsLoaded) return;
        await FetchAsync(_lastQuery, string.IsNullOrWhiteSpace(_lastQuery) ? "downloads" : "relevance");
    }

    private async void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = (args.QueryText ?? "").Trim();
        if (query.Length == 0)
        {
            await FetchAsync("", "downloads");
            return;
        }

        await FetchAsync(query, "relevance");
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await FetchAsync("", "downloads");

    private async Task FetchAsync(string query, string sortIndex)
    {
        if (_busy) return;

        _busy = true;
        _lastQuery = query;

        var gv = SelectedGameVersion();
        StatusRing.IsActive = true;
        StatusBar.IsIndeterminate = true;
        StatusText.Text = string.IsNullOrWhiteSpace(query)
            ? (string.IsNullOrEmpty(gv) ? "正在加载全部热门资源包…" : $"正在加载适用于 {gv} 的热门资源包…")
            : $"正在搜索「{query}」…";

        try
        {
            var result = await AppState.Modrinth.SearchAsync(query, ProjectType, null,
                string.IsNullOrEmpty(gv) ? null : gv, 40, 0, sortIndex);

            var relaxed = false;
            if (result.Hits.Count == 0 && !string.IsNullOrEmpty(gv))
            {
                // 指定版本无结果时放宽为全部版本，保证页面不空
                result = await AppState.Modrinth.SearchAsync(query, ProjectType, null, null, 40, 0, sortIndex);
                relaxed = true;
            }

            var items = result.Hits.Select(h => new MarketItem
            {
                Title = h.Title,
                Description = h.Description,
                IconUrl = h.IconUrl ?? "",
                Meta = $"{h.MetaText} · 资源包",
                SourceName = "Modrinth",
                ProjectType = ProjectType,
                Payload = h
            }).ToList();

            ResultList.ItemsSource = items;

            StatusText.Text = items.Count == 0
                ? "没有找到匹配的资源包，试试其他关键词或切换游戏版本"
                : $"共 {result.TotalHits} 个结果，当前显示 {items.Count} 个" +
                  (relaxed ? "（当前版本无匹配，已放宽为全部版本）" : "");

            MarketSupport.LoadIcons(items);
        }
        catch (Exception ex)
        {
            Log.Error("加载资源包失败：" + ex.Message);
            StatusText.Text = "加载失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            StatusRing.IsActive = false;
            StatusBar.IsIndeterminate = false;
        }
    }

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not FrameworkElement element || element.DataContext is not MarketItem item) return;

        if (string.IsNullOrEmpty(_versionId))
        {
            await ShowDialog("未选择游戏版本",
                "请先在「版本管理」中安装并选择一个版本，再下载资源包。资源包会安装到该实例的 resourcepacks 目录。");
            return;
        }

        if (item.Payload is not ModrinthHit hit) return;

        StatusRing.IsActive = true;
        StatusText.Text = $"正在获取 {item.Title} 的可用版本…";

        List<ModrinthVersion> versions;
        try
        {
            var gv = SelectedGameVersion();
            var all = await AppState.Modrinth.GetVersionsAsync(hit.ProjectId,
                null, string.IsNullOrEmpty(gv) ? null : gv);

            versions = all.Where(v => ModrinthService.PickPrimaryFile(v) != null).ToList();

            // 指定版本不匹配时回退为全部可用版本
            if (versions.Count == 0)
                versions = (await AppState.Modrinth.GetVersionsAsync(hit.ProjectId))
                    .Where(v => ModrinthService.PickPrimaryFile(v) != null)
                    .ToList();
        }
        catch (Exception ex)
        {
            StatusRing.IsActive = false;
            StatusText.Text = "获取可用版本失败：" + ex.Message;
            return;
        }

        StatusRing.IsActive = false;

        if (versions.Count == 0)
        {
            StatusText.Text = $"{item.Title} 没有可直接下载的文件";
            return;
        }

        var target = versions.Count == 1 ? versions[0] : await PickVersionAsync(item, versions);
        if (target == null) return;

        _busy = true;
        StatusRing.IsActive = true;
        StatusBar.IsIndeterminate = false;
        StatusBar.Value = 0;
        StatusText.Text = $"正在下载 {target.Name}（{target.VersionNumber}）…";

        try
        {
            var saved = await AppState.Mods.InstallFromModrinthAsync(_versionId, target, ProjectType);

            StatusText.Text = saved != null
                ? $"已安装 {Path.GetFileName(saved)} → {Path.GetDirectoryName(saved)}"
                : "下载失败，详见日志";
            StatusBar.Value = saved != null ? 100 : 0;
        }
        catch (Exception ex)
        {
            Log.Error("下载资源包失败：" + ex.Message);
            StatusText.Text = "下载失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            StatusRing.IsActive = false;
            StatusBar.IsIndeterminate = false;
        }
    }

    private async Task<ModrinthVersion?> PickVersionAsync(MarketItem item, List<ModrinthVersion> versions)
    {
        var combo = new ComboBox
        {
            Header = "资源包版本",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 340,
            ItemsSource = versions
                .Select(v => $"{v.VersionNumber} · {v.Name}")
                .ToList(),
            SelectedIndex = 0
        };

        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 15,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(combo);

        var dialog = new ContentDialog
        {
            Title = "选择资源包版本",
            Content = panel,
            PrimaryButtonText = "开始下载",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var index = combo.SelectedIndex;
        return index >= 0 && index < versions.Count ? versions[index] : null;
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

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_versionId))
        {
            await ShowDialog("未选择游戏版本", "请先在「版本管理」中安装并选择一个版本。");
            return;
        }

        try
        {
            var dir = ModManager.ResourcePacksDir(_versionId);
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开 resourcepacks 目录失败：" + ex.Message);
        }
    }
}
