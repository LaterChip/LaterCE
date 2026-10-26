using LaterCE.Models;
using LaterCE.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using Windows.ApplicationModel.DataTransfer;

namespace LaterCE.Pages;

public sealed partial class AccountsPage : Page
{
    private CancellationTokenSource? _msCts;
    private DeviceCodeInfo? _msInfo;

    public AccountsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        AppState.AccountsChanged -= Refresh;
        AppState.AccountsChanged += Refresh;
        Refresh();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        AppState.AccountsChanged -= Refresh;
        _msCts?.Cancel();
        base.OnNavigatedFrom(e);
    }

    private void Refresh()
    {
        var accounts = ConfigService.Current.Accounts.ToList();
        AccountList.ItemsSource = accounts;

        var active = AppState.ActiveAccount;
        if (active == null)
        {
            CurrentName.Text = "未登录";
            CurrentType.Text = "登录后才能加入正版服务器，离线账户仅能进入离线服务器";
            CurrentInitial.Text = "?";
            return;
        }

        CurrentName.Text = active.Name;
        CurrentType.Text = $"{active.Subtitle}{(active.IsExpired ? " · 令牌已过期，启动时自动刷新" : "")}";
        CurrentInitial.Text = string.IsNullOrEmpty(active.Name)
            ? "?"
            : active.Name[..1].ToUpperInvariant();
    }

    #region 微软登录

    private async void MsLogin_Click(object sender, RoutedEventArgs e)
    {
        if (_msCts != null)
        {
            MsStatus.Text = "已有登录流程进行中，请先取消。";
            return;
        }

        MsPanel.Visibility = Visibility.Visible;
        MsRing.IsActive = false;
        MsCode.Text = "------";

        if (string.IsNullOrWhiteSpace(ConfigService.Current.MicrosoftClientId))
        {
            MsStatus.Text = "尚未配置 Microsoft 应用 Client ID：微软已停用旧的公共 ID。请到「设置 → 微软正版登录」按说明填写 Azure 应用客户端 ID 后，再回到本页点击正版登录。";
            MsUri.Text = "https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade";
            return;
        }

        MsRing.IsActive = true;
        MsStatus.Text = "正在向微软申请设备代码…";

        _msCts = new CancellationTokenSource();

        try
        {
            _msInfo = await AuthService.StartDeviceCodeAsync(_msCts.Token);
            MsCode.Text = _msInfo.UserCode;
            MsUri.Text = _msInfo.VerificationUri;
            MsStatus.Text = "等待你在浏览器中完成授权…";

            var status = new Progress<string>(s => MsStatus.Text = s);
            var (access, refresh, expires) = await AuthService.PollDeviceCodeAsync(_msInfo, status, _msCts.Token);

            MsStatus.Text = "授权成功，正在获取 Minecraft 档案…";
            var account = await AuthService.CompleteMicrosoftLoginAsync(access, refresh, expires);

            AppState.SetActiveAccount(account);
            Log.Info($"微软账号 {account.Name} 登录成功");
            MsStatus.Text = $"已登录：{account.Name}";
            MsPanel.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            MsStatus.Text = "登录已取消。";
        }
        catch (Exception ex)
        {
            Log.Error("微软登录失败：" + ex.Message);
            MsStatus.Text = "登录失败：" + ex.Message;
        }
        finally
        {
            MsRing.IsActive = false;
            _msCts?.Dispose();
            _msCts = null;
        }
    }

    private void OpenMsPage_Click(object sender, RoutedEventArgs e)
    {
        var uri = _msInfo?.VerificationUri ?? "https://microsoft.com/link";
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Log.Warn("打开登录页失败：" + ex.Message);
        }
    }

    private void CopyCode_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(MsCode.Text);
            Clipboard.SetContent(package);
            MsStatus.Text = "设备代码已复制到剪贴板。";
        }
        catch (Exception ex)
        {
            MsStatus.Text = "复制失败：" + ex.Message;
        }
    }

    private void CancelMs_Click(object sender, RoutedEventArgs e)
    {
        _msCts?.Cancel();
        MsStatus.Text = "已请求取消登录…";
    }

    #endregion

    #region LittleSkin

    private void ToggleLittleSkin_Click(object sender, RoutedEventArgs e)
    {
        OfflinePanel.Visibility = Visibility.Collapsed;
        LsPanel.Visibility = LsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void LsLogin_Click(object sender, RoutedEventArgs e)
    {
        var user = LsUser.Text?.Trim() ?? "";
        var pass = LsPass.Password ?? "";

        if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(pass))
        {
            LsStatus.Text = "请填写邮箱和密码。";
            return;
        }

        LsStatus.Text = "正在登录 LittleSkin…";

        try
        {
            var account = await AuthService.LoginLittleSkinAsync(user, pass);
            AppState.SetActiveAccount(account);
            LsStatus.Text = $"已登录：{account.Name}";
            LsPass.Password = "";
            Log.Info($"LittleSkin 账号 {account.Name} 登录成功");
        }
        catch (Exception ex)
        {
            Log.Error("LittleSkin 登录失败：" + ex.Message);
            LsStatus.Text = ex.Message;
        }
    }

    #endregion

    #region 离线账户

    private void ToggleOffline_Click(object sender, RoutedEventArgs e)
    {
        LsPanel.Visibility = Visibility.Collapsed;
        OfflinePanel.Visibility = OfflinePanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private async void CreateOffline_Click(object sender, RoutedEventArgs e)
    {
        var name = OfflineName.Text?.Trim() ?? "";

        if (name.Length is < 3 or > 16 || !name.All(c => char.IsLetterOrDigit(c) || c == '_'))
        {
            await ShowDialog("玩家名不合法", "离线玩家名需为 3-16 位字母、数字或下划线。");
            return;
        }

        var account = AuthService.CreateOfflineAccount(name);
        AppState.SetActiveAccount(account);
        OfflineName.Text = "";
        Log.Info($"已创建离线账户 {name}");
    }

    #endregion

    private void SetActive_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not Account account) return;
        AppState.SetActiveAccount(account);
        Log.Info($"当前账户切换为 {account.Name}");
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not Account account) return;

        var dialog = new ContentDialog
        {
            Title = $"移除账户「{account.Name}」？",
            Content = "仅移除本启动器中保存的登录信息，不影响该账号本身。",
            PrimaryButtonText = "移除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        AppState.RemoveAccount(account);
        Log.Info($"已移除账户 {account.Name}");
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
