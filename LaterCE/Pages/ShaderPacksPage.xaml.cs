using System.Collections.ObjectModel;
using System.ComponentModel;
using LaterCE.Models;
using LaterCE.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.VisualBasic.FileIO;

namespace LaterCE.Pages;

/// <summary>光影市场列表项（比通用 MarketItem 多一个下载状态标记）</summary>
public class ShaderMarketItem : INotifyPropertyChanged
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string Meta { get; set; } = "";
    public ModrinthHit? Payload { get; set; }

    private ImageSource? _icon;
    private string _badge = "";
    private bool _iconRequested;

    /// <summary>惰性加载的图标；为空或加载失败时保持 null，界面显示占位字形</summary>
    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            _icon = value;
            Raise(nameof(Icon));
        }
    }

    /// <summary>下载状态提示（空字符串表示不显示）</summary>
    public string Badge
    {
        get => _badge;
        set
        {
            _badge = value;
            Raise(nameof(Badge));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public async Task LoadIconAsync()
    {
        if (_iconRequested) return;
        _iconRequested = true;

        var url = (IconUrl ?? "").Trim();
        if (url.Length == 0) return;

        await IconGate.WaitAsync();
        try
        {
            // 交给 WinRT 图像管线后台下载：走系统网络栈，不会因 HttpClient 连接策略被误取消
            var bmp = new BitmapImage { DecodePixelWidth = 96 };
            bmp.UriSource = new Uri(url);
            Icon = bmp;
        }
        catch (Exception ex)
        {
            Log.Info($"光影图标加载失败，使用占位图标（{url}）：{ex.Message}");
            Icon = null;
        }
        finally
        {
            IconGate.Release();
        }
    }

    private static readonly SemaphoreSlim IconGate = new(6, 6);
}

/// <summary>已安装光影条目（支持启用 / 禁用 / 删除）</summary>
public class InstalledShaderItem : INotifyPropertyChanged
{
    public string FilePath { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public long Size { get; init; }
    public bool IsDirectory { get; init; }

    private bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Raise(nameof(Enabled));
            Raise(nameof(StateText));
            Raise(nameof(ToggleText));
            Raise(nameof(StatusBrush));
            Raise(nameof(SizeText));
        }
    }

    public string StateText => Enabled ? "已启用" : "已禁用";

    public string ToggleText => Enabled ? "禁用" : "启用";

    public string SizeText => IsDirectory ? FormatSize(Size) + " · 文件夹" : FormatSize(Size);

    public SolidColorBrush StatusBrush => Enabled
        ? new SolidColorBrush(Windows.UI.Color.FromArgb(255, 14, 164, 114))
        : new SolidColorBrush(Windows.UI.Color.FromArgb(255, 138, 148, 163));

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1024L * 1024 * 1024 => $"{bytes / 1024d / 1024d / 1024d:F2} GB",
        >= 1024L * 1024 => $"{bytes / 1024d / 1024d:F1} MB",
        >= 1024 => $"{bytes / 1024d:F0} KB",
        _ => $"{bytes} B"
    };
}

/// <summary>光影包页面：Modrinth 光影市场 + 本地已安装光影管理</summary>
public sealed partial class ShaderPacksPage : Page
{
    private const string ProjectType = "shader";
    private const string DisabledSuffix = ".disabled";

    private bool _busy;
    private bool _suppressFilter;
    private string _versionId = "";
    private string _gameVersion = "";
    private string _lastQuery = "";

    private List<InstalledShaderItem> _installedAll = new();
    private readonly ObservableCollection<InstalledShaderItem> _installedView = new();

    public ShaderPacksPage()
    {
        InitializeComponent();
        InstalledList.ItemsSource = _installedView;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        _versionId = ConfigService.Current.SelectedVersion ?? "";
        _gameVersion = MarketSupport.ResolveBaseGameVersion(_versionId);

        TargetVersionText.Text = string.IsNullOrEmpty(_versionId)
            ? "未选择实例"
            : $"目标实例：{_versionId}（{_gameVersion}）";

        InstancePathText.Text = string.IsNullOrEmpty(_versionId)
            ? ""
            : "shaderpacks：" + ModManager.ShaderPacksDir(_versionId);

        BuildVersionFilter();
        RefreshInstalled();

        // 进入页面即加载热门光影，无需先输入关键词
        _ = FetchAsync("", "downloads");
    }

    // ==================== 市场：筛选与搜索 ====================

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

        LoaderCombo.SelectedIndex = 0;

        _suppressFilter = false;
    }

    private string SelectedGameVersion()
        => (VersionCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

    private string SelectedLoader()
        => (LoaderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

    private async void Version_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilter || !IsLoaded) return;
        await FetchAsync(_lastQuery, string.IsNullOrWhiteSpace(_lastQuery) ? "downloads" : "relevance");
    }

    private async void Loader_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFilter || !IsLoaded) return;
        await FetchAsync(_lastQuery, string.IsNullOrWhiteSpace(_lastQuery) ? "downloads" : "relevance");
    }

    private async void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = (args.QueryText ?? "").Trim();
        await FetchAsync(query, query.Length == 0 ? "downloads" : "relevance");
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
        => await FetchAsync("", "downloads");

    private async Task FetchAsync(string query, string sortIndex)
    {
        if (_busy) return;

        _busy = true;
        _lastQuery = query;

        var gv = SelectedGameVersion();
        var loader = SelectedLoader();
        StatusRing.IsActive = true;
        StatusBar.IsIndeterminate = true;
        StatusText.Text = string.IsNullOrWhiteSpace(query)
            ? (string.IsNullOrEmpty(gv) ? "正在加载全部热门光影…" : $"正在加载适用于 {gv} 的热门光影…")
            : $"正在搜索「{query}」…";

        try
        {
            var result = await AppState.Modrinth.SearchAsync(query, ProjectType,
                string.IsNullOrEmpty(loader) ? null : loader,
                string.IsNullOrEmpty(gv) ? null : gv, 40, 0, sortIndex);

            var relaxed = false;
            if (result.Hits.Count == 0 && !string.IsNullOrEmpty(gv))
            {
                // 指定版本无结果时放宽为全部版本，保证页面不空
                result = await AppState.Modrinth.SearchAsync(query, ProjectType,
                    string.IsNullOrEmpty(loader) ? null : loader, null, 40, 0, sortIndex);
                relaxed = true;
            }

            var items = result.Hits.Select(h => new ShaderMarketItem
            {
                Title = h.Title,
                Description = h.Description,
                IconUrl = h.IconUrl ?? "",
                Meta = $"{h.MetaText} · 光影包",
                Payload = h
            }).ToList();

            ResultList.ItemsSource = items;

            StatusText.Text = items.Count == 0
                ? "没有找到匹配的光影包，试试其他关键词或切换游戏版本 / 加载器"
                : $"共 {result.TotalHits} 个结果，当前显示 {items.Count} 个" +
                  (relaxed ? "（当前版本无匹配，已放宽为全部版本）" : "");

            foreach (var i in items) _ = i.LoadIconAsync();
        }
        catch (Exception ex)
        {
            Log.Error("加载光影包失败：" + ex.Message);
            StatusText.Text = "加载失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            StatusRing.IsActive = false;
            StatusBar.IsIndeterminate = false;
        }
    }

    // ==================== 市场：下载安装 ====================

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not FrameworkElement element || element.DataContext is not ShaderMarketItem item) return;

        if (string.IsNullOrEmpty(_versionId))
        {
            await ShowDialog("未选择游戏版本",
                "请先在「版本管理」中安装并选择一个版本，再下载光影包。光影会安装到该实例的 shaderpacks 目录。");
            return;
        }

        if (item.Payload is not ModrinthHit hit) return;

        StatusRing.IsActive = true;
        item.Badge = "获取版本…";
        StatusText.Text = $"正在获取 {item.Title} 的可用版本…";

        List<ModrinthVersion> versions;
        try
        {
            var gv = SelectedGameVersion();
            var loader = SelectedLoader();
            var all = await AppState.Modrinth.GetVersionsAsync(hit.ProjectId,
                null, string.IsNullOrEmpty(gv) ? null : gv);

            versions = all.Where(v => ModrinthService.PickPrimaryFile(v) != null).ToList();

            // 指定加载器优先（iris / optifine），没有匹配时保留全部可用版本
            if (!string.IsNullOrEmpty(loader))
            {
                var matched = versions
                    .Where(v => v.Loaders.Any(l => string.Equals(l, loader, StringComparison.OrdinalIgnoreCase)))
                    .ToList();
                if (matched.Count > 0) versions = matched;
            }

            // 指定版本不匹配时回退为全部可用版本
            if (versions.Count == 0)
                versions = (await AppState.Modrinth.GetVersionsAsync(hit.ProjectId))
                    .Where(v => ModrinthService.PickPrimaryFile(v) != null)
                    .ToList();
        }
        catch (Exception ex)
        {
            StatusRing.IsActive = false;
            item.Badge = "";
            StatusText.Text = "获取可用版本失败：" + ex.Message;
            return;
        }

        StatusRing.IsActive = false;
        item.Badge = "";

        if (versions.Count == 0)
        {
            StatusText.Text = $"{item.Title} 没有可直接下载的文件";
            return;
        }

        var target = versions.Count == 1 ? versions[0] : await PickVersionAsync(item, versions);
        if (target == null) return;

        _busy = true;
        item.Badge = "下载中…";
        StatusRing.IsActive = true;
        StatusBar.IsIndeterminate = false;
        StatusBar.Value = 0;
        StatusText.Text = $"正在下载 {target.Name}（{target.VersionNumber}）…";

        try
        {
            var saved = await AppState.Mods.InstallFromModrinthAsync(_versionId, target, ProjectType);

            if (saved != null)
            {
                item.Badge = "已安装";
                StatusText.Text = $"已安装 {Path.GetFileName(saved)} → {Path.GetDirectoryName(saved)}";
                StatusBar.Value = 100;
                RefreshInstalled();
            }
            else
            {
                item.Badge = "失败";
                StatusText.Text = "下载失败，详见日志";
                StatusBar.Value = 0;
            }
        }
        catch (Exception ex)
        {
            item.Badge = "失败";
            Log.Error("下载光影包失败：" + ex.Message);
            StatusText.Text = "下载失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            StatusRing.IsActive = false;
            StatusBar.IsIndeterminate = false;
        }
    }

    private async Task<ModrinthVersion?> PickVersionAsync(ShaderMarketItem item, List<ModrinthVersion> versions)
    {
        var combo = new ComboBox
        {
            Header = "光影版本",
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
            Title = "选择光影版本",
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

    // ==================== 已安装：扫描与展示 ====================

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // 切到「已安装光影」页签时重新扫描目录（内部控件的 SelectionChanged 冒泡到此，按页签号过滤即可）
        if (Tabs.SelectedIndex == 1) RefreshInstalled();
    }

    private void RefreshInstalled_Click(object sender, RoutedEventArgs e)
    {
        RefreshInstalled();
        StatusText.Text = $"已刷新，共 {_installedAll.Count} 个光影包";
    }

    private void InstalledSearch_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        => ApplyInstalledFilter();

    private void RefreshInstalled()
    {
        _installedAll = ScanInstalled();
        ApplyInstalledFilter();
        UpdateInstalledSummary();
    }

    private void ApplyInstalledFilter()
    {
        var keyword = (InstalledSearch.Text ?? "").Trim();

        _installedView.Clear();
        foreach (var item in _installedAll)
        {
            if (keyword.Length > 0 &&
                item.DisplayName.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            _installedView.Add(item);
        }

        var empty = _installedView.Count == 0;
        EmptyInstalledText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyInstalledText.Text = _installedAll.Count == 0
            ? "当前实例的 shaderpacks 目录里还没有光影包。\n可以切到「光影市场」下载，或把 .zip 光影包手动放进该目录。"
            : $"没有匹配「{keyword}」的光影包。";
    }

    private void UpdateInstalledSummary()
    {
        var enabled = _installedAll.Count(i => i.Enabled);
        var total = _installedAll.Sum(i => i.Size);
        InstalledSummary.Text = $"共 {_installedAll.Count} 个光影包 · 已启用 {enabled} 个 · 已禁用 {_installedAll.Count - enabled} 个 · 占用 {InstalledShaderItem.FormatSize(total)}";
    }

    private List<InstalledShaderItem> ScanInstalled()
    {
        var list = new List<InstalledShaderItem>();
        if (string.IsNullOrEmpty(_versionId)) return list;

        var dir = ModManager.ShaderPacksDir(_versionId);
        if (!Directory.Exists(dir)) return list;

        try
        {
            foreach (var file in Directory.GetFiles(dir))
            {
                var name = Path.GetFileName(file);
                var enabled = !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                var core = enabled ? name : name[..^DisabledSuffix.Length];

                if (!core.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) &&
                    !core.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
                    continue;

                long size = 0;
                try { size = new FileInfo(file).Length; } catch { }

                list.Add(new InstalledShaderItem
                {
                    FilePath = file,
                    DisplayName = Path.GetFileNameWithoutExtension(core),
                    Size = size,
                    Enabled = enabled
                });
            }

            foreach (var folder in Directory.GetDirectories(dir))
            {
                var name = Path.GetFileName(folder);
                var enabled = !name.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase);
                var core = enabled ? name : name[..^DisabledSuffix.Length];

                list.Add(new InstalledShaderItem
                {
                    FilePath = folder,
                    DisplayName = core,
                    Size = GetDirectorySize(folder),
                    IsDirectory = true,
                    Enabled = enabled
                });
            }
        }
        catch (Exception ex)
        {
            Log.Warn("扫描 shaderpacks 目录失败：" + ex.Message);
        }

        return list
            .OrderByDescending(i => i.Enabled)
            .ThenBy(i => i.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static long GetDirectorySize(string dir)
    {
        try
        {
            return Directory.GetFiles(dir, "*", System.IO.SearchOption.AllDirectories)
                .Sum(f =>
                {
                    try { return new FileInfo(f).Length; } catch { return 0L; }
                });
        }
        catch
        {
            return 0;
        }
    }

    // ==================== 已安装：启用 / 禁用 / 删除 ====================

    private async void ToggleShader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InstalledShaderItem item }) return;

        try
        {
            var path = item.FilePath;
            var disable = item.Enabled;
            var target = disable ? path + DisabledSuffix : StripDisabled(path);

            if (File.Exists(path)) File.Move(path, target);
            else if (Directory.Exists(path)) Directory.Move(path, target);
            else
            {
                StatusText.Text = "文件已不存在，正在刷新列表…";
                RefreshInstalled();
                return;
            }

            Log.Info($"{(disable ? "已禁用" : "已启用")}光影：{item.DisplayName}");
            StatusText.Text = $"{(disable ? "已禁用" : "已启用")} {item.DisplayName}";
            RefreshInstalled();
        }
        catch (Exception ex)
        {
            Log.Error("切换光影启用状态失败：" + ex.Message);
            await ShowDialog("操作失败", "无法修改该光影的启用状态。\n\n详情：" + ex.Message);
        }
    }

    private static string StripDisabled(string path)
        => path.EndsWith(DisabledSuffix, StringComparison.OrdinalIgnoreCase)
            ? path[..^DisabledSuffix.Length]
            : path;

    private async void DeleteShader_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: InstalledShaderItem item }) return;

        var dialog = new ContentDialog
        {
            Title = "删除光影包",
            Content = new TextBlock
            {
                Text = $"将把「{item.DisplayName}」移入回收站，之后可从回收站还原。",
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "移入回收站",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            if (Directory.Exists(item.FilePath))
                FileSystem.DeleteDirectory(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                FileSystem.DeleteFile(item.FilePath, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);

            Log.Info("已删除光影：" + item.DisplayName);
            StatusText.Text = $"已删除 {item.DisplayName}（已移入回收站）";
            RefreshInstalled();
        }
        catch (Exception ex)
        {
            Log.Error("删除光影失败：" + ex.Message);
            await ShowDialog("删除失败", "无法删除该光影包。\n\n详情：" + ex.Message);
        }
    }

    private async void EnableAll_Click(object sender, RoutedEventArgs e)
    {
        var disabled = _installedAll.Where(i => !i.Enabled).ToList();
        if (disabled.Count == 0)
        {
            StatusText.Text = "当前没有处于禁用状态的光影包";
            return;
        }

        var count = 0;
        foreach (var item in disabled)
        {
            try
            {
                var target = StripDisabled(item.FilePath);
                if (File.Exists(item.FilePath)) File.Move(item.FilePath, target);
                else if (Directory.Exists(item.FilePath)) Directory.Move(item.FilePath, target);
                count++;
            }
            catch (Exception ex)
            {
                Log.Warn($"启用光影失败（{item.DisplayName}）：{ex.Message}");
            }
        }

        RefreshInstalled();
        StatusText.Text = $"已启用 {count} 个光影包" + (count < disabled.Count ? $"（{disabled.Count - count} 个失败，详见日志）" : "");
        await Task.CompletedTask;
    }

    // ==================== 打开目录 ====================

    private async void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_versionId))
        {
            await ShowDialog("未选择游戏版本", "请先在「版本管理」中安装并选择一个版本。");
            return;
        }

        try
        {
            var dir = ModManager.ShaderPacksDir(_versionId);
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开 shaderpacks 目录失败：" + ex.Message);
        }
    }
}
