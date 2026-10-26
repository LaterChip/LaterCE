using LaterCE.Models;
using LaterCE.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace LaterCE.Pages;

public sealed partial class VersionsPage : Page
{
    private List<VersionListItem> _all = new();
    private string _category = "release";
    private string _search = "";
    private bool _busy;

    public VersionsPage()
    {
        InitializeComponent();

        // 在控件初始化完成后再设默认项，避免 SelectionChanged 早于控件创建而空引用
        CategoryCombo.SelectedIndex = 0;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        LoadInstalled();
        _ = LoadManifestAsync();
    }

    private async Task LoadManifestAsync(bool force = false)
    {
        try
        {
            LoadingRing.IsActive = true;
            TotalText.Text = "正在获取版本清单…";

            var manifest = await AppState.Versions.GetManifestAsync(force);

            _all = manifest.Versions.Select(v => new VersionListItem
            {
                Id = v.Id,
                Type = v.Type,
                Url = v.Url,
                IsInstalled = MinecraftVersionService.IsInstalled(v.Id),
                SubText = BuildSubText(v)
            }).ToList();

            TotalText.Text = $"共 {_all.Count} 个版本";
            ApplyFilter();
        }
        catch (Exception ex)
        {
            Log.Error("加载版本清单失败：" + ex.Message);
            TotalText.Text = "版本清单获取失败，请检查网络";
        }
        finally
        {
            LoadingRing.IsActive = false;
        }
    }

    private static string BuildSubText(VersionSummary v)
    {
        var parts = new List<string>();

        if (DateTime.TryParse(v.ReleaseTime, out var dt))
            parts.Add(dt.ToLocalTime().ToString("yyyy-MM-dd"));

        if (MinecraftVersionService.IsAprilFools(v.Id))
            parts.Add("愚人节版本");

        if (string.IsNullOrEmpty(v.ReleaseTime) && parts.Count == 0)
            parts.Add(v.Type);

        return string.Join(" · ", parts);
    }

    private void ApplyFilter()
    {
        if (VersionList == null || FilterCountText == null) return;

        IEnumerable<VersionListItem> query = _category switch
        {
            "release" => _all.Where(v => v.Type == "release"),
            "snapshot" => _all.Where(v => v.Type == "snapshot" && !MinecraftVersionService.IsAprilFools(v.Id)),
            "fools" => _all.Where(v => MinecraftVersionService.IsAprilFools(v.Id)),
            "old" => _all.Where(v => v.Type is "old_beta" or "old_alpha"),
            _ => _all
        };

        if (!string.IsNullOrWhiteSpace(_search))
            query = query.Where(v => v.Id.Contains(_search, StringComparison.OrdinalIgnoreCase));

        var list = query.Take(500).ToList();
        VersionList.ItemsSource = list;
        FilterCountText.Text = $"显示 {list.Count} 项";
    }

    private void LoadInstalled()
    {
        var installed = AppState.Versions.GetInstalled();
        InstalledList.ItemsSource = installed;
        InstalledCountText.Text = installed.Count == 0
            ? "尚未安装任何版本"
            : $"已安装 {installed.Count} 个版本 · 当前：{ConfigService.Current.SelectedVersion}";
    }

    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (CategoryCombo == null || FilterCountText == null || VersionList == null) return;
        if (CategoryCombo.SelectedItem is not ComboBoxItem item) return;
        _category = item.Tag?.ToString() ?? "all";
        ApplyFilter();
    }

    private void Search_Changed(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;
        _search = sender.Text?.Trim() ?? "";
        ApplyFilter();
    }

    private async void RefreshManifest_Click(object sender, RoutedEventArgs e)
    {
        await LoadManifestAsync(true);
        LoadInstalled();
    }

    private void Rescan_Click(object sender, RoutedEventArgs e) => LoadInstalled();

    private async void Download_Click(object sender, RoutedEventArgs e)
    {
        if (_busy)
        {
            await ShowDialog("请稍候", "当前已有下载任务在进行中。");
            return;
        }

        if (sender is not FrameworkElement element || element.DataContext is not VersionListItem item) return;

        if (MinecraftVersionService.IsInstalled(item.Id))
        {
            var confirm = new ContentDialog
            {
                Title = $"重新下载 {item.Id}？",
                Content = "将覆盖该版本已有的核心文件（不影响 mods、存档等游戏数据）。",
                PrimaryButtonText = "重新下载",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = XamlRoot
            };

            if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        }

        // 选择加载器与加载器版本（Vanilla / Fabric / Quilt / Forge / NeoForge）
        var pick = await PickLoaderAsync(item.Id);
        if (pick == null) return;

        var loaderKey = pick.Value.loader ?? "";
        var loaderVersion = pick.Value.version;
        var loaderName = string.IsNullOrEmpty(loaderKey)
            ? "原版"
            : LoaderInstaller.Loaders.FirstOrDefault(l => l.Key == loaderKey)?.Name ?? loaderKey;

        _busy = true;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressText.Text = $"准备安装 {item.Id}（{loaderName}）…";
        InstallProgressBar.Value = 0;

        var progress = new Progress<InstallProgress>(p =>
        {
            InstallProgressBar.Value = p.Percent;
            ProgressText.Text = string.IsNullOrEmpty(p.Detail)
                ? $"{item.Id} · {p.Stage}"
                : $"{item.Id} · {p.Stage}（{p.Detail}）";
        });

        var installedId = await AppState.Loaders.InstallAsync(item.Id, loaderKey, loaderVersion, progress);

        _busy = false;

        if (!string.IsNullOrEmpty(installedId))
        {
            ProgressText.Text = $"{installedId}（{loaderName}）安装完成";
            ConfigService.Current.SelectedVersion = installedId;
            if (!string.IsNullOrEmpty(loaderKey))
                ConfigService.Current.PreferredLoader = loaderKey;
            ConfigService.Save();

            LoadInstalled();
            await LoadManifestAsync();

            var launch = new ContentDialog
            {
                Title = "安装完成",
                Content = $"版本 {installedId}（{loaderName}）已安装并设为当前版本，是否立即启动？",
                PrimaryButtonText = "立即启动",
                CloseButtonText = "稍后",
                XamlRoot = XamlRoot
            };

            if (await launch.ShowAsync() == ContentDialogResult.Primary && MainWindow.Instance != null)
                await MainWindow.Instance.LaunchAsync();
        }
        else
        {
            ProgressText.Text = $"{item.Id} + {loaderName} 安装失败，详情请查看日志";
        }

        await Task.Delay(2500);
        if (!_busy) ProgressPanel.Visibility = Visibility.Collapsed;
    }

    /// <summary>选择模组加载器与加载器版本；返回 (加载器 key, 加载器版本)，取消时返回 null</summary>
    private async Task<(string? loader, string? version)?> PickLoaderAsync(string gameVersion)
    {
        var loaderCombo = new ComboBox
        {
            Header = "模组加载器",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 360,
            ItemsSource = LoaderInstaller.Loaders,
            DisplayMemberPath = "Name"
        };

        var versionCombo = new ComboBox
        {
            Header = "加载器版本",
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MinWidth = 360,
            IsEnabled = false,
            PlaceholderText = "选择加载器后自动加载"
        };

        var ring = new ProgressRing { Width = 18, Height = 18, IsActive = false };
        var status = new TextBlock { FontSize = 12, Opacity = 0.75, TextWrapping = TextWrapping.Wrap };

        var statusRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        statusRow.Children.Add(ring);
        statusRow.Children.Add(status);

        var panel = new StackPanel { Spacing = 12, MinWidth = 380 };
        panel.Children.Add(new TextBlock
        {
            Text = $"游戏版本：{gameVersion}",
            FontWeight = FontWeights.SemiBold
        });
        panel.Children.Add(loaderCombo);
        panel.Children.Add(versionCombo);
        panel.Children.Add(statusRow);
        panel.Children.Add(new TextBlock
        {
            Text = "Fabric / Quilt 通过官方 meta 生成可继承的版本；Forge / NeoForge 会运行安装器（需要可用的 Java 运行时）。",
            FontSize = 12,
            Opacity = 0.6,
            TextWrapping = TextWrapping.Wrap
        });

        async void LoaderChanged(object? s, SelectionChangedEventArgs e)
        {
            versionCombo.ItemsSource = null;
            versionCombo.SelectedItem = null;
            versionCombo.IsEnabled = false;

            if (loaderCombo.SelectedItem is not LoaderOption opt) return;

            if (!opt.NeedsLoaderVersion)
            {
                versionCombo.PlaceholderText = "原版无需加载器版本";
                status.Text = "将安装官方原版 " + gameVersion;
                return;
            }

            ring.IsActive = true;
            status.Text = $"正在获取 {opt.Name} 可用版本…";

            try
            {
                var list = await AppState.Loaders.GetLoaderVersionsAsync(opt.Key, gameVersion);
                versionCombo.ItemsSource = list;
                versionCombo.DisplayMemberPath = "Display";
                versionCombo.IsEnabled = list.Count > 0;
                versionCombo.SelectedIndex = list.Count > 0 ? 0 : -1;
                versionCombo.PlaceholderText = list.Count > 0 ? "" : "该版本暂无可用加载器版本";
                status.Text = list.Count > 0
                    ? $"共 {list.Count} 个 {opt.Name} 版本，默认推荐最新版本"
                    : $"未找到适用于 {gameVersion} 的 {opt.Name} 版本，可换其他加载器";
            }
            catch (Exception ex)
            {
                status.Text = "获取加载器版本失败：" + ex.Message;
            }
            finally
            {
                ring.IsActive = false;
            }
        }

        versionCombo.SelectionChanged += (_, _) =>
        {
            if (versionCombo.SelectedItem is LoaderVersionOption v && loaderCombo.SelectedItem is LoaderOption opt)
                status.Text = $"将安装：{gameVersion} + {opt.Name} {v.Version}";
        };

        loaderCombo.SelectionChanged += LoaderChanged;
        loaderCombo.SelectedIndex = 1; // 默认选中 Fabric

        var dialog = new ContentDialog
        {
            Title = "选择模组加载器",
            Content = new ScrollViewer { Content = panel, MaxHeight = 460 },
            PrimaryButtonText = "开始安装",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot
        };

        dialog.PrimaryButtonClick += (_, args) =>
        {
            if (loaderCombo.SelectedItem is not LoaderOption opt)
            {
                args.Cancel = true;
                return;
            }

            if (opt.NeedsLoaderVersion && versionCombo.SelectedItem is not LoaderVersionOption)
            {
                args.Cancel = true;
                status.Text = "请等待加载器版本列表加载完成并选择一个版本。";
            }
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        var chosen = loaderCombo.SelectedItem as LoaderOption;
        if (chosen == null) return null;

        var ver = (versionCombo.SelectedItem as LoaderVersionOption)?.Version;
        return (chosen.Key, ver);
    }

    private async void SetCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not InstalledVersion version) return;

        ConfigService.Current.SelectedVersion = version.Id;
        ConfigService.Save();
        Log.Info($"当前版本已切换为 {version.Id}");
        LoadInstalled();
        await Task.CompletedTask;
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not InstalledVersion version) return;

        try
        {
            var dir = Path.Combine(AppPaths.VersionsDir, version.Id);
            Directory.CreateDirectory(dir);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = dir,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开目录失败：" + ex.Message);
        }
    }

    private async void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not InstalledVersion version) return;

        var installedOnly = Path.Combine(AppPaths.VersionsDir, version.Id);
        var confirm = new ContentDialog
        {
            Title = $"删除版本 {version.Id}？",
            Content = $"将永久删除该版本目录：\n{installedOnly}\n\n" +
                      "若已开启版本隔离，该目录内的 mods、存档也会一并删除，请谨慎操作。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            if (Directory.Exists(installedOnly)) Directory.Delete(installedOnly, true);
            Log.Info($"已删除版本 {version.Id}");

            if (ConfigService.Current.SelectedVersion == version.Id)
            {
                ConfigService.Current.SelectedVersion = "";
                ConfigService.Save();
            }

            LoadInstalled();
            await LoadManifestAsync();
        }
        catch (Exception ex)
        {
            Log.Error("删除版本失败：" + ex.Message);
            await ShowDialog("删除失败", ex.Message);
        }
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
