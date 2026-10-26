using System.ComponentModel;
using LaterCE.Models;
using LaterCE.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;

namespace LaterCE.Pages;

/// <summary>市场列表统一展示模型</summary>
public class MarketItem : INotifyPropertyChanged
{
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string IconUrl { get; set; } = "";
    public string Meta { get; set; } = "";
    public string SourceName { get; set; } = "";
    public string ProjectType { get; set; } = "mod";
    public object? Payload { get; set; }

    private ImageSource? _icon;
    private bool _iconRequested;

    /// <summary>惰性加载的图标；为空或加载失败时保持 null，界面显示占位字形</summary>
    public ImageSource? Icon
    {
        get => _icon;
        private set
        {
            _icon = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public async Task LoadIconAsync()
    {
        if (_iconRequested) return;
        _iconRequested = true;

        var url = (IconUrl ?? "").Trim();
        if (url.Length == 0) return;

        // 单次最多 6 个并发图片请求，避免瞬间大量连接
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
            Log.Info($"图标加载失败，使用占位图标（{url}）：{ex.Message}");
            Icon = null;
        }
        finally
        {
            IconGate.Release();
        }
    }

    private static readonly SemaphoreSlim IconGate = new(6, 6);
}

/// <summary>安装面板中的单个可下载文件</summary>
public class ModFileOption
{
    public string Label { get; set; } = "";
    public List<string> GameVersions { get; set; } = new();
    public List<string> Loaders { get; set; } = new();
    public string InstallGameVersion { get; set; } = "";
    public ModrinthVersion? Modrinth { get; set; }
    public CfFile? Cf { get; set; }

    public override string ToString() => Label;
}

public sealed partial class ModsPage : Page
{
    private string _source = "Modrinth";
    private bool _busy;
    private string _versionId = "";
    private string _gameVersion = "";

    public ModsPage()
    {
        InitializeComponent();

        // 控件就绪后再设默认项，防止初始化期 SelectionChanged 触发空引用
        SourceCombo.SelectedIndex = 0;
        LoaderCombo.SelectedIndex = 0;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _versionId = ConfigService.Current.SelectedVersion ?? "";
        _gameVersion = ResolveBaseGameVersion(_versionId);

        TargetVersionText.Text = string.IsNullOrEmpty(_versionId)
            ? "未选择版本"
            : $"目标版本：{_gameVersion}（{_versionId}）";

        // 默认加载器跟随配置
        var preferred = (ConfigService.Current.PreferredLoader ?? "").ToLowerInvariant();
        foreach (var obj in LoaderCombo.Items)
        {
            if (obj is ComboBoxItem item && string.Equals(item.Tag?.ToString(), preferred, StringComparison.OrdinalIgnoreCase))
            {
                LoaderCombo.SelectedItem = item;
                break;
            }
        }

        // 进入页面即自动加载热门模组，无需先输入关键词
        _ = LoadPopularAsync();
    }

    private string SelectedLoader()
    {
        return LoaderCombo.SelectedItem is ComboBoxItem item ? item.Tag?.ToString() ?? "" : "";
    }

    private void Source_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SourceCombo == null) return;
        if (SourceCombo.SelectedItem is not ComboBoxItem item) return;

        var changed = (item.Tag?.ToString() ?? "Modrinth") != _source;
        _source = item.Tag?.ToString() ?? "Modrinth";
        if (StatusText == null) return;

        if (_source == "CurseForge" && string.IsNullOrWhiteSpace(ConfigService.Current.CurseForgeApiKey))
        {
            StatusText.Text = "CurseForge 需要在「设置」中填写 API Key；可切回 Modrinth 浏览";
            return;
        }

        StatusText.Text = _source == "CurseForge" ? "正在加载 CurseForge 热门模组…" : "正在加载 Modrinth 热门模组…";

        // 切换数据源后立即刷新热门列表
        if (changed && IsLoaded) _ = LoadPopularAsync();
    }

    private void Loader_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || LoaderCombo == null) return;
        if (LoaderCombo.SelectedItem is ComboBoxItem item)
        {
            ConfigService.Current.PreferredLoader = item.Tag?.ToString() ?? "";
            ConfigService.Save();

            if (!_busy && ResultList.ItemsSource != null)
                _ = FetchAsync("", "downloads", "正在刷新热门模组…");
        }
    }

    private async void Search_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        => await DoSearchAsync(args.QueryText);

    private async Task DoSearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            await LoadPopularAsync();
            return;
        }

        await FetchAsync(query.Trim(), "relevance", $"正在搜索「{query}」…");
    }

    /// <summary>加载热门模组：空关键词 + 按下载量排序</summary>
    private async Task LoadPopularAsync()
    {
        var scope = string.IsNullOrEmpty(_gameVersion) ? "全部版本" : _gameVersion;
        await FetchAsync("", "downloads", $"正在加载适用于 {scope} 的热门模组…");
    }

    private async Task FetchAsync(string query, string sortIndex, string statusPrefix)
    {
        if (_busy) return;

        _busy = true;
        StatusRing.IsActive = true;
        StatusBar.IsIndeterminate = true;
        StatusText.Text = statusPrefix;

        try
        {
            var loader = SelectedLoader();

            if (_source == "CurseForge")
            {
                if (string.IsNullOrWhiteSpace(ConfigService.Current.CurseForgeApiKey))
                {
                    StatusText.Text = "尚未配置 CurseForge API Key，请到「设置」填写后重试";
                    ResultList.ItemsSource = null;
                    return;
                }

                var mods = await AppState.CurseForge.SearchAsync(query, CurseForgeService.ClassMods,
                    string.IsNullOrEmpty(_gameVersion) ? null : _gameVersion, 40);

                // 指定版本无结果时放宽为全部版本，保证页面不空
                var relaxed = false;
                if (mods.Count == 0 && !string.IsNullOrEmpty(_gameVersion))
                {
                    mods = await AppState.CurseForge.SearchAsync(query, CurseForgeService.ClassMods, null, 40);
                    relaxed = true;
                }

                var items = mods.Select(m => new MarketItem
                {
                    Title = m.Name,
                    Description = m.Summary,
                    IconUrl = m.IconUrl,
                    Meta = m.MetaText,
                    SourceName = "CurseForge",
                    ProjectType = "mod",
                    Payload = m
                }).ToList();

                ResultList.ItemsSource = items;
                StatusText.Text = $"CurseForge 返回 {items.Count} 个模组（按下载量排序）" +
                                  (relaxed ? "，当前版本无匹配已放宽为全部版本" : "");
                LoadIcons(items);
            }
            else
            {
                var result = await AppState.Modrinth.SearchAsync(query, "mod",
                    string.IsNullOrEmpty(loader) ? null : loader,
                    string.IsNullOrEmpty(_gameVersion) ? null : _gameVersion, 40, 0, sortIndex);

                var relaxed = false;
                if (result.Hits.Count == 0 && !string.IsNullOrEmpty(_gameVersion))
                {
                    result = await AppState.Modrinth.SearchAsync(query, "mod",
                        string.IsNullOrEmpty(loader) ? null : loader, null, 40, 0, sortIndex);
                    relaxed = true;
                }

                var items = result.Hits.Select(h => new MarketItem
                {
                    Title = h.Title,
                    Description = h.Description,
                    IconUrl = h.IconUrl ?? "",
                    Meta = $"{h.MetaText} · {h.ProjectType}",
                    SourceName = "Modrinth",
                    ProjectType = string.IsNullOrEmpty(h.ProjectType) ? "mod" : h.ProjectType,
                    Payload = h
                }).ToList();

                ResultList.ItemsSource = items;
                StatusText.Text = items.Count == 0
                    ? "没有找到匹配的模组，试试其他关键词或切换加载器"
                    : $"共 {result.TotalHits} 个结果，当前显示 {items.Count} 个" +
                      (relaxed ? "（当前版本无匹配，已放宽为全部版本）" : "");
                LoadIcons(items);
            }
        }
        catch (Exception ex)
        {
            Log.Error("搜索失败：" + ex.Message);
            StatusText.Text = "加载失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            StatusRing.IsActive = false;
            StatusBar.IsIndeterminate = false;
        }
    }

    private static void LoadIcons(IEnumerable<MarketItem> items)
    {
        foreach (var item in items)
            _ = item.LoadIconAsync();
    }

    private async void Install_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (sender is not FrameworkElement element || element.DataContext is not MarketItem item) return;

        if (string.IsNullOrEmpty(_versionId))
        {
            await ShowDialog("未选择游戏版本",
                "请先在「版本管理」中安装并选择一个版本，再安装模组。");
            return;
        }

        StatusRing.IsActive = true;
        StatusText.Text = $"正在获取 {item.Title} 的可用版本…";

        List<ModFileOption> files;
        try
        {
            files = await LoadFileOptionsAsync(item);
        }
        catch (Exception ex)
        {
            StatusText.Text = "获取可用版本失败：" + ex.Message;
            return;
        }
        finally
        {
            StatusRing.IsActive = false;
        }

        if (files.Count == 0)
        {
            StatusText.Text = $"{item.Title} 没有可直接下载的文件";
            return;
        }

        var picked = await PickFileAsync(item, files);
        if (picked == null) return;

        // 统一装进当前实例目录，避免选了别的游戏版本后写进不存在的 mods 目录
        var targetVersion = _versionId;

        _busy = true;
        StatusRing.IsActive = true;
        StatusBar.IsIndeterminate = true;
        StatusBar.Value = 0;
        StatusText.Text = $"正在下载 {picked.Label} → {targetVersion} …";

        try
        {
            string? saved = null;

            if (picked.Modrinth != null)
            {
                saved = await AppState.Mods.InstallFromModrinthAsync(targetVersion, picked.Modrinth, item.ProjectType);
            }
            else if (picked.Cf != null)
            {
                var dir = Path.Combine(ModManager.InstanceRoot(targetVersion), ModManager.FolderFor(item.ProjectType));
                saved = await AppState.CurseForge.DownloadFileAsync(picked.Cf, dir);
            }

            StatusText.Text = saved != null
                ? $"已安装 {Path.GetFileName(saved)} → {Path.GetDirectoryName(saved)}"
                : "下载失败，详见日志";
        }
        catch (Exception ex)
        {
            Log.Error("安装失败：" + ex.Message);
            StatusText.Text = "安装失败：" + ex.Message;
        }
        finally
        {
            _busy = false;
            StatusRing.IsActive = false;
            StatusBar.IsIndeterminate = false;
            StatusBar.Value = 100;
        }
    }

    private async Task<List<ModFileOption>> LoadFileOptionsAsync(MarketItem item)
    {
        var list = new List<ModFileOption>();

        if (item.Payload is ModrinthHit hit)
        {
            var versions = await AppState.Modrinth.GetVersionsAsync(hit.ProjectId);
            foreach (var v in versions)
            {
                if (ModrinthService.PickPrimaryFile(v) == null) continue;
                list.Add(new ModFileOption
                {
                    Label = $"{v.VersionNumber} · {v.Name}",
                    GameVersions = v.GameVersions,
                    Loaders = v.Loaders.Select(x => x.ToLowerInvariant()).ToList(),
                    Modrinth = v
                });
            }
        }
        else if (item.Payload is CfMod mod)
        {
            var files = await AppState.CurseForge.GetFilesAsync(mod.Id);
            foreach (var f in files)
            {
                if (string.IsNullOrEmpty(CurseForgeService.PickDownloadUrl(f))) continue;

                var gv = f.GameVersions.Where(IsGameVersionTag).ToList();
                var ld = f.GameVersions.Where(x => !IsGameVersionTag(x))
                    .Select(x => x.ToLowerInvariant())
                    .Where(x => x is "fabric" or "forge" or "neoforge" or "quilt")
                    .ToList();

                list.Add(new ModFileOption
                {
                    Label = string.IsNullOrWhiteSpace(f.DisplayName) ? f.FileName : f.DisplayName,
                    GameVersions = gv,
                    Loaders = ld,
                    Cf = f
                });
            }
        }

        return list;
    }

    /// <summary>CurseForge 的 gameVersions 中区分「游戏版本」与「加载器/环境标签」</summary>
    private static bool IsGameVersionTag(string tag)
        => !string.IsNullOrWhiteSpace(tag) && char.IsDigit(tag[0]) && tag.Contains('.');

    private async Task<ModFileOption?> PickFileAsync(MarketItem item, List<ModFileOption> files)
    {
        // 游戏版本候选：当前目标版本置顶
        var allGameVersions = files.SelectMany(f => f.GameVersions)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var orderedGameVersions = new List<string>();
        if (!string.IsNullOrEmpty(_gameVersion) && allGameVersions.Contains(_gameVersion, StringComparer.OrdinalIgnoreCase))
            orderedGameVersions.Add(_gameVersion);
        orderedGameVersions.AddRange(allGameVersions
            .Where(v => !string.Equals(v, _gameVersion, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(v => v, StringComparer.OrdinalIgnoreCase));

        // 加载器候选
        var loaderTags = files.SelectMany(f => f.Loaders)
            .Where(l => l is "fabric" or "forge" or "neoforge" or "quilt")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(l => l, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var gameCombo = new ComboBox
        {
            Header = "游戏版本",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 340,
            ItemsSource = orderedGameVersions,
            IsEnabled = orderedGameVersions.Count > 0
        };
        if (orderedGameVersions.Count > 0) gameCombo.SelectedIndex = 0;

        var loaderItems = new List<ComboBoxItem> { new() { Content = "任意加载器", Tag = "" } };
        foreach (var tag in loaderTags)
            loaderItems.Add(new ComboBoxItem { Content = tag, Tag = tag });

        var loaderCombo = new ComboBox
        {
            Header = "模组加载器",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 340,
            ItemsSource = loaderItems,
            SelectedIndex = 0
        };

        var preferred = (ConfigService.Current.PreferredLoader ?? "").ToLowerInvariant();
        if (!string.IsNullOrEmpty(preferred))
        {
            var match = loaderItems.FirstOrDefault(i => string.Equals(i.Tag?.ToString(), preferred, StringComparison.OrdinalIgnoreCase));
            if (match != null) loaderCombo.SelectedItem = match;
        }

        var fileCombo = new ComboBox
        {
            Header = "模组版本",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 340,
            DisplayMemberPath = "Label"
        };

        var info = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };

        void Refilter()
        {
            var gv = gameCombo.SelectedItem as string ?? "";
            var ld = (loaderCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "";

            var matched = files.Where(f =>
                    (string.IsNullOrEmpty(gv) || f.GameVersions.Count == 0 ||
                     f.GameVersions.Contains(gv, StringComparer.OrdinalIgnoreCase)) &&
                    (string.IsNullOrEmpty(ld) || f.Loaders.Count == 0 ||
                     f.Loaders.Contains(ld, StringComparer.OrdinalIgnoreCase)))
                .ToList();

            fileCombo.ItemsSource = matched;
            fileCombo.SelectedIndex = matched.Count > 0 ? 0 : -1;

            info.Text = matched.Count == 0
                ? "没有同时匹配该游戏版本与加载器的文件，请调整上方筛选。"
                : $"共 {matched.Count} 个可选文件";

            foreach (var f in matched)
                f.InstallGameVersion = string.IsNullOrEmpty(gv) ? _gameVersion : gv;
        }

        gameCombo.SelectionChanged += (_, _) => Refilter();
        loaderCombo.SelectionChanged += (_, _) => Refilter();
        Refilter();

        var panel = new StackPanel { Spacing = 12, MinWidth = 360 };
        panel.Children.Add(new TextBlock
        {
            Text = item.Title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(gameCombo);
        panel.Children.Add(loaderCombo);
        panel.Children.Add(fileCombo);
        panel.Children.Add(info);

        var dialog = new ContentDialog
        {
            Title = "选择安装内容",
            Content = new ScrollViewer { Content = panel, MaxHeight = 460 },
            PrimaryButtonText = "开始安装",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (fileCombo.SelectedItem is not ModFileOption)
            {
                args.Cancel = true;
                info.Text = "请选择一个模组版本后再开始安装。";
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return fileCombo.SelectedItem as ModFileOption;
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

    private void OpenModsFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_versionId))
        {
            StatusText.Text = "请先选择一个游戏版本";
            return;
        }

        try
        {
            var dir = ModManager.ModsDir(_versionId);
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开 mods 目录失败：" + ex.Message);
        }
    }

    /// <summary>从实例 ID（如 fabric-loader-0.20.1-1.21.2）解析出基础游戏版本</summary>
    private static string ResolveBaseGameVersion(string instanceId)
    {
        if (string.IsNullOrWhiteSpace(instanceId)) return "";
        try
        {
            var json = Path.Combine(AppPaths.VersionsDir, instanceId, instanceId + ".json");
            if (File.Exists(json))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(json));
                foreach (var key in new[] { "inheritsFrom", "jar" })
                {
                    if (doc.RootElement.TryGetProperty(key, out var v) &&
                        v.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        var sv = v.GetString();
                        if (!string.IsNullOrWhiteSpace(sv)) return sv!;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log.Info("解析基础游戏版本失败：" + ex.Message);
        }

        var parts = instanceId.Split('-');
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            var seg = parts[i];
            if (seg.Length > 0 && char.IsDigit(seg[0]) && seg.Contains('.')) return seg;
        }
        return instanceId;
    }
}
