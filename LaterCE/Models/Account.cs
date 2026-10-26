namespace LaterCE.Models;

public class Account
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>microsoft / littleskin / offline</summary>
    public string Type { get; set; } = "offline";

    public string Name { get; set; } = "";
    public string Uuid { get; set; } = "";

    /// <summary>Minecraft access token（微软）或 Yggdrasil accessToken（LittleSkin）</summary>
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string ClientToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }

    public string TypeDisplay => Type switch
    {
        "microsoft" => "微软正版",
        "littleskin" => "LittleSkin",
        _ => "离线账号"
    };

    public string Subtitle => string.IsNullOrWhiteSpace(Uuid)
        ? TypeDisplay
        : $"{TypeDisplay} · {Uuid}";

    public bool IsExpired => Type == "microsoft" && ExpiresAt != default &&
                             ExpiresAt.ToUniversalTime() < DateTime.UtcNow.AddMinutes(2);
}
