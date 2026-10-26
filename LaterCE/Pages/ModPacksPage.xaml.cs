using LaterCE.Models;
using LaterCE.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LaterCE.Pages;

/// <summary>整合包市场：打开即列出 Modrinth 全部（热门）整合包，安装时按 modrinth.index.json 清单落地到实例</summary>
public sealed partial class ModPacksPage : Page
{
    private const string ProjectType = "modpack";

    private bool _busy;
    private bool _suppressFilter;
    private string _versionId = "";
    private string _gameVersion = "";
    private string _lastQuery = "";

    public ModPacksPage()
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

        // 进入页面即加载热门整合包，无需先输入关键词
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
            ? (string.IsNullOrEmpty(gv) ? "正在加载全部热门整合包…" : $"正在加载适用于 {gv} 的热门整合包…")
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
                Meta = $"{h.MetaText} · 整合包",
                SourceName = "Modrinth",
                ProjectType = ProjectType,
                Payload = h
            }).ToList();

            ResultList.ItemsSource = items;

            StatusText.Text = items.Count == 0
                ? "没有找到匹配的整合包，试试其他关键词或切换游戏版本"
                : $"共 {result.TotalHits} 个结果，当前显示 {items.Count} 个" +
                  (relaxed ? "（当前版本无匹配，已放宽为全部版本）" : "");

            MarketSupport.LoadIcons(items);
        }
        catch (Exception ex)
        {
            Log.Error("加载整合包失败：" + ex.Message);
            StatusText.Text = "加载失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            StatusRing.IsActive = false;
            StatusBar.IsIndeterminate = false;
        }
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not FrameworkElement element || element.DataContext is not MarketItem item) return;

        if (string.IsNullOrEmpty(_versionId))
        {
            await ShowDialog("未选择游戏版本",
                "请先在「版本管理」中安装并选择一个版本，再安装整合包。整合包会按清单安装到该实例目录。");
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
            var downloadDir = Path.Combine(AppPaths.BaseDir, "downloads");
            var mrpack = await AppState.Modrinth.DownloadAsync(target, downloadDir);

            if (mrpack == null)
            {
                StatusText.Text = "整合包下载失败，详见日志";
                return;
            }

            StatusText.Text = $"正在按清单安装到实例 {_versionId}…";
            StatusBar.Value = 0;

            var progress = new Progress<double>(p =>
            {
                StatusBar.Value = Math.Clamp(p * 100, 0, 100);
                StatusText.Text = $"正在按清单安装到实例 {_versionId}… {p:P0}";
            });

            var ok = await AppState.Mods.InstallMrpackAsync(mrpack, _versionId, null, progress);

            StatusBar.Value = ok ? 100 : 0;
            StatusText.Text = ok
                ? $"整合包安装完成 → {ModManager.InstanceRoot(_versionId)}"
                : "整合包安装失败，详见日志";
        }
        catch (Exception ex)
        {
            Log.Error("安装整合包失败：" + ex.Message);
            StatusText.Text = "安装失败：" + ex.Message;
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
            Header = "整合包版本",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 340,
            ItemsSource = versions
                .Select(v => $"{v.VersionNumber} · {v.Name}")
                .ToList(),
            SelectedIndex = 0
        };

        var hint = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.75,
            TextWrapping = TextWrapping.Wrap,
            Text = $"将安装到实例：{_versionId}"
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
        panel.Children.Add(hint);

        var dialog = new ContentDialog
        {
            Title = "选择整合包版本",
            Content = panel,
            PrimaryButtonText = "开始安装",
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
            var dir = ModManager.InstanceRoot(_versionId);
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开实例目录失败：" + ex.Message);
        }
    }
}
