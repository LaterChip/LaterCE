using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using LaterCE.Models;

namespace LaterCE.Services;

public class DeviceCodeInfo
{
    public string UserCode { get; set; } = "";
    public string VerificationUri { get; set; } = "";
    public string DeviceCode { get; set; } = "";
    public string ClientId { get; set; } = "";
    public int Interval { get; set; } = 5;
    public int ExpiresIn { get; set; } = 900;
    public string Message { get; set; } = "";
}

public static class AuthService
{
    // 微软 v2.0 端点已不再接受旧版公共 client id（00000000402b5328，会返回 AADSTS700016），
    // 因此改为读取用户在「设置 → 微软正版登录」中自行注册的 Azure 应用 Client ID。
    private const string Scope = "XboxLive.signin offline_access";

    private static string RequireClientId()
    {
        var id = ConfigService.Current.MicrosoftClientId?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(id))
            throw new Exception("尚未配置 Microsoft 应用 Client ID。请到「设置 → 微软正版登录」填入你自己注册的 Azure 应用客户端 ID，再回来重试。");
        return id;
    }

    public const string LittleSkinServer = "https://littleskin.cn/api/yggdrasil";

    #region 微软登录

    public static async Task<DeviceCodeInfo> StartDeviceCodeAsync(CancellationToken ct = default)
    {
        var clientId = RequireClientId();

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["scope"] = Scope
        });

        using var resp = await Http.Client.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/devicecode", content, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception($"获取微软登录设备码失败：{json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        return new DeviceCodeInfo
        {
            DeviceCode = root.GetProperty("device_code").GetString() ?? "",
            ClientId = clientId,
            UserCode = root.GetProperty("user_code").GetString() ?? "",
            VerificationUri = root.TryGetProperty("verification_uri", out var uri)
                ? uri.GetString() ?? "https://microsoft.com/link"
                : "https://microsoft.com/link",
            Interval = root.TryGetProperty("interval", out var interval) ? interval.GetInt32() : 5,
            ExpiresIn = root.TryGetProperty("expires_in", out var exp) ? exp.GetInt32() : 900,
            Message = root.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : ""
        };
    }

    /// <summary>轮询设备码授权结果，直到用户完成登录</summary>
    public static async Task<(string accessToken, string refreshToken, int expiresIn)> PollDeviceCodeAsync(
        DeviceCodeInfo info, IProgress<string>? status = null, CancellationToken ct = default)
    {
        var interval = Math.Max(2, info.Interval);
        var deadline = DateTime.UtcNow.AddSeconds(Math.Max(60, info.ExpiresIn));

        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = string.IsNullOrEmpty(info.ClientId) ? RequireClientId() : info.ClientId,
                ["device_code"] = info.DeviceCode
            });

            using var resp = await Http.Client.PostAsync(
                "https://login.microsoftonline.com/consumers/oauth2/v2.0/token", content, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);

            if (resp.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return (
                    root.GetProperty("access_token").GetString() ?? "",
                    root.TryGetProperty("refresh_token", out var r) ? r.GetString() ?? "" : "",
                    root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600
                );
            }

            using var errDoc = JsonDocument.Parse(json);
            var error = errDoc.RootElement.TryGetProperty("error", out var err)
                ? err.GetString() ?? ""
                : "";

            switch (error)
            {
                case "authorization_pending":
                    status?.Report("等待你在浏览器中完成微软账号登录…");
                    continue;
                case "slow_down":
                    interval += 5;
                    status?.Report("微软要求降低轮询频率，继续等待…");
                    continue;
                case "expired_token":
                    throw new Exception("设备码已过期，请重新发起登录。");
                case "authorization_declined":
                    throw new Exception("登录已被拒绝。");
                default:
                    throw new Exception($"微软登录失败：{json}");
            }
        }

        throw new TimeoutException("微软登录超时，请重试。");
    }

    /// <summary>用微软账号令牌换取 Minecraft 账号</summary>
    public static async Task<Account> CompleteMicrosoftLoginAsync(string accessToken, string refreshToken,
        int expiresIn)
    {
        // 1. Xbox Live
        var xblDoc = await PostJsonAsync("https://user.auth.xboxlive.com/user/authenticate", new
        {
            Properties = new
            {
                AuthMethod = "RPS",
                SiteName = "user.auth.xboxlive.com",
                RpsTicket = "d=" + accessToken
            },
            RelyingParty = "http://auth.xboxlive.com",
            TokenType = "JWT"
        });

        var xblToken = xblDoc.RootElement.GetProperty("Token").GetString() ?? "";
        var uhs = xblDoc.RootElement.GetProperty("DisplayClaims").GetProperty("xui")[0]
            .GetProperty("uhs").GetString() ?? "";

        // 2. XSTS
        JsonDocument xstsDoc;
        try
        {
            xstsDoc = await PostJsonAsync("https://xsts.auth.xboxlive.com/xsts/authorize", new
            {
                Properties = new
                {
                    SandboxId = "RETAIL",
                    UserTokens = new[] { xblToken }
                },
                RelyingParty = "rp://api.minecraftservices.com/",
                TokenType = "JWT"
            });
        }
        catch (Exception ex)
        {
            throw new Exception(TranslateXstsError(ex.Message));
        }

        var xstsToken = xstsDoc.RootElement.GetProperty("Token").GetString() ?? "";

        // 3. Minecraft 服务
        var mcDoc = await PostJsonAsync("https://api.minecraftservices.com/authentication/login_with_xbox", new
        {
            identityToken = $"XBL3.0 x={uhs};{xstsToken}"
        });

        var mcAccess = mcDoc.RootElement.GetProperty("access_token").GetString() ?? "";
        var mcExpires = mcDoc.RootElement.TryGetProperty("expires_in", out var e) ? e.GetInt32() : expiresIn;

        // 4. 玩家档案
        using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.minecraftservices.com/minecraft/profile");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", mcAccess);

        using var profileResp = await Http.Client.SendAsync(req);
        if (profileResp.StatusCode == HttpStatusCode.NotFound)
            throw new Exception("该微软账号未拥有 Minecraft Java 版（或尚未创建角色名）。");

        var profileJson = await profileResp.Content.ReadAsStringAsync();
        if (!profileResp.IsSuccessStatusCode)
            throw new Exception($"获取 Minecraft 档案失败：{profileJson}");

        using var profileDoc = JsonDocument.Parse(profileJson);
        var id = profileDoc.RootElement.GetProperty("id").GetString() ?? "";
        var name = profileDoc.RootElement.GetProperty("name").GetString() ?? "";

        return new Account
        {
            Type = "microsoft",
            Name = name,
            Uuid = id,
            AccessToken = mcAccess,
            RefreshToken = refreshToken,
            ExpiresAt = DateTime.UtcNow.AddSeconds(mcExpires)
        };
    }

    /// <summary>使用刷新令牌重新获取 Minecraft 令牌</summary>
    public static async Task<Account> RefreshMicrosoftAsync(Account account, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(account.RefreshToken))
            throw new Exception("该账号缺少刷新令牌，请重新登录。");

        using var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = RequireClientId(),
            ["refresh_token"] = account.RefreshToken,
            ["scope"] = Scope
        });

        using var resp = await Http.Client.PostAsync(
            "https://login.microsoftonline.com/consumers/oauth2/v2.0/token", content, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new Exception("微软令牌刷新失败，请重新登录。");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var access = root.GetProperty("access_token").GetString() ?? "";
        var refresh = root.TryGetProperty("refresh_token", out var r)
            ? r.GetString() ?? account.RefreshToken
            : account.RefreshToken;
        var expires = root.TryGetProperty("expires_in", out var e) ? e.GetInt32() : 3600;

        var fresh = await CompleteMicrosoftLoginAsync(access, refresh, expires);
        fresh.Id = account.Id;
        return fresh;
    }

    private static string TranslateXstsError(string raw)
    {
        if (raw.Contains("2148916233"))
            return "该微软账号尚未注册 Xbox 账号，请先到 xbox.com 创建。";
        if (raw.Contains("2148916235"))
            return "该账号所在地区不支持 Xbox Live。";
        if (raw.Contains("2148916238"))
            return "未成年账号需要先加入家庭组（由成年人账号批准）。";
        if (raw.Contains("2148916236") || raw.Contains("2148916237"))
            return "该账号需要完成 Xbox Live 年龄验证。";
        return "Xbox Live 认证失败：" + raw;
    }

    #endregion

    #region LittleSkin / 外置登录

    public static async Task<Account> LoginLittleSkinAsync(string username, string password,
        CancellationToken ct = default)
    {
        var clientToken = Guid.NewGuid().ToString("N");

        JsonDocument doc;
        try
        {
            doc = await PostJsonAsync(LittleSkinServer + "/authserver/authenticate", new
            {
                agent = new { name = "Minecraft", version = 1 },
                username,
                password,
                clientToken,
                requestUser = false
            }, ct);
        }
        catch (Exception ex)
        {
            throw new Exception("LittleSkin 登录失败：" + ex.Message);
        }

        var root = doc.RootElement;
        if (!root.TryGetProperty("accessToken", out var accessNode))
            throw new Exception("LittleSkin 返回数据异常（缺少 accessToken）。");

        var accessToken = accessNode.GetString() ?? "";

        if (!root.TryGetProperty("selectedProfile", out var profile) ||
            profile.ValueKind != JsonValueKind.Object)
            throw new Exception("该 LittleSkin 账号还没有创建角色，请先到 littleskin.cn 创建角色。");

        return new Account
        {
            Type = "littleskin",
            Name = profile.GetProperty("name").GetString() ?? username,
            Uuid = profile.GetProperty("id").GetString() ?? "",
            AccessToken = accessToken,
            ClientToken = clientToken,
            ExpiresAt = DateTime.UtcNow.AddHours(24)
        };
    }

    public static async Task<bool> RefreshLittleSkinAsync(Account account, CancellationToken ct = default)
    {
        try
        {
            var doc = await PostJsonAsync(LittleSkinServer + "/authserver/refresh", new
            {
                accessToken = account.AccessToken,
                clientToken = account.ClientToken,
                requestUser = false
            }, ct);

            var root = doc.RootElement;
            if (!root.TryGetProperty("accessToken", out var at)) return false;

            account.AccessToken = at.GetString() ?? account.AccessToken;
            if (root.TryGetProperty("selectedProfile", out var p) && p.ValueKind == JsonValueKind.Object)
            {
                account.Name = p.GetProperty("name").GetString() ?? account.Name;
                account.Uuid = p.GetProperty("id").GetString() ?? account.Uuid;
            }

            return true;
        }
        catch (Exception ex)
        {
            Log.Warn("LittleSkin 令牌刷新失败：" + ex.Message);
            return false;
        }
    }

    #endregion

    #region 离线

    public static Account CreateOfflineAccount(string name)
    {
        return new Account
        {
            Type = "offline",
            Name = name,
            Uuid = GameLauncher.OfflineUuid(name),
            AccessToken = "0"
        };
    }

    #endregion

    private static async Task<JsonDocument> PostJsonAsync(string url, object body, CancellationToken ct = default)
    {
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var resp = await Http.Client.PostAsync(url, content, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {json}");

        return JsonDocument.Parse(json);
    }
}
