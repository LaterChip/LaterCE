using System.Net;
using System.Net.Http;
using System.Text.Json;

namespace LaterCE.Services;

public static class Http
{
    public const string UserAgent = "LaterCE/1.0.0 (Windows NT 10.0; Win64; x64) LaterChip-Launcher";

    public static readonly HttpClient Client = Create();

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(20),
            MaxConnectionsPerServer = 64
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(30)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        return client;
    }

    /// <summary>带镜像回退的文本获取：镜像失败自动回退官方地址</summary>
    public static async Task<string> GetStringAsync(string url, CancellationToken ct = default)
    {
        Exception? last = null;

        foreach (var candidate in Candidates(url))
        {
            try
            {
                using var resp = await Client.GetAsync(candidate, ct);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"请求失败 {candidate} -> {ex.Message}");
            }
        }

        throw last ?? new HttpRequestException($"请求失败：{url}");
    }

    public static async Task<T?> GetJsonAsync<T>(string url, CancellationToken ct = default)
        => await GetJsonAsync<T>(url, null, ct);

    /// <summary>带镜像回退的 JSON 获取</summary>
    public static async Task<T?> GetJsonAsync<T>(string url, IDictionary<string, string>? headers,
        CancellationToken ct = default)
    {
        Exception? last = null;

        foreach (var candidate in Candidates(url))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, candidate);
                if (headers != null)
                {
                    foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                }

                using var resp = await Client.SendAsync(req, HttpCompletionOption.ResponseContentRead, ct);
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync(ct);
                return JsonSerializer.Deserialize<T>(json, JsonOptions);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                last = ex;
                Log.Warn($"请求失败 {candidate} -> {ex.Message}");
            }
        }

        if (last != null) throw last;
        return default;
    }

    public static async Task<T?> PostJsonAsync<T>(string url, object body, IDictionary<string, string>? headers = null,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        req.Content = new StringContent(JsonSerializer.Serialize(body), System.Text.Encoding.UTF8, "application/json");
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        if (headers != null)
        {
            foreach (var kv in headers) req.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        using var resp = await Client.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"HTTP {(int)resp.StatusCode}: {json}");

        return JsonSerializer.Deserialize<T>(json, JsonOptions);
    }

    /// <summary>将 Mojang / Forge 官方地址改写为 BMCLAPI 镜像地址</summary>
    public static string Mirror(string url)
    {
        if (!ConfigService.Current.UseBmclApi || string.IsNullOrEmpty(url)) return url;

        return url
            .Replace("https://launchermeta.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://piston-meta.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://piston-data.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://launcher.mojang.com", "https://bmclapi2.bangbang93.com")
            .Replace("https://libraries.minecraft.net", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://files.minecraftforge.net/maven", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://maven.minecraftforge.net", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://maven.neoforged.net/releases", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://maven.neoforged.net", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://maven.fabricmc.net", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://maven.quiltmc.org/repository/release", "https://bmclapi2.bangbang93.com/maven")
            .Replace("https://resources.download.minecraft.net", "https://bmclapi2.bangbang93.com/assets")
            .Replace("https://meta.fabricmc.net", "https://bmclapi2.bangbang93.com/fabric-meta")
            .Replace("https://meta.quiltmc.org", "https://bmclapi2.bangbang93.com/quilt-meta");
    }

    /// <summary>第三方备用镜像（阿里云公共仓库，可覆盖 Maven Central 上的部分库）</summary>
    private static string AltMirror(string url)
    {
        const string libs = "https://libraries.minecraft.net/";
        if (!url.StartsWith(libs, StringComparison.OrdinalIgnoreCase)) return "";

        var path = url[libs.Length..];
        // 仅对 Maven Central 常见 group 使用阿里云兜底
        if (!(path.StartsWith("org/", StringComparison.OrdinalIgnoreCase) ||
              path.StartsWith("com/", StringComparison.OrdinalIgnoreCase) ||
              path.StartsWith("net/", StringComparison.OrdinalIgnoreCase)))
            return "";

        return "https://maven.aliyun.com/repository/public/" + path;
    }

    /// <summary>按优先级生成下载候选链：镜像 → 官方 → 备用镜像</summary>
    public static List<string> Candidates(string url)
    {
        var list = new List<string>();
        if (string.IsNullOrEmpty(url)) return list;

        var mirrored = Mirror(url);
        var alt = AltMirror(url);

        if (ConfigService.Current.UseBmclApi)
        {
            if (mirrored != url) list.Add(mirrored);
            list.Add(url);
        }
        else
        {
            list.Add(url);
            if (mirrored != url) list.Add(mirrored);
        }

        if (!string.IsNullOrEmpty(alt)) list.Add(alt);

        return list.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }
}
