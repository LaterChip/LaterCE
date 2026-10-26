using System.Collections.Concurrent;

namespace LaterCE.Services;

public class DownloadItem
{
    public string Url { get; set; } = "";
    public string DestPath { get; set; } = "";
    public long Size { get; set; }

    public DownloadItem() { }
    public DownloadItem(string url, string dest, long size = 0)
    {
        Url = url;
        DestPath = dest;
        Size = size;
    }
}

public static class Downloader
{
    /// <summary>
    /// 单文件下载：按「BMCLAPI 镜像 → 官方 → 备用镜像」候选链依次尝试，每个候选最多 2 轮。
    /// 目标文件已存在且大小匹配时直接跳过。
    /// </summary>
    public static async Task<bool> DownloadFileAsync(string url, string destPath, long expectedSize = 0,
        Action<long, long>? progress = null, CancellationToken ct = default)
    {
        if (expectedSize > 0 && File.Exists(destPath) && new FileInfo(destPath).Length == expectedSize)
        {
            progress?.Invoke(expectedSize, expectedSize);
            return true;
        }

        var dir = Path.GetDirectoryName(destPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        var candidates = Http.Candidates(url);
        if (candidates.Count == 0) return false;

        var tmp = destPath + ".tmp";
        Exception? last = null;

        for (var round = 1; round <= 2; round++)
        {
            foreach (var candidate in candidates)
            {
                try
                {
                    using var resp = await Http.Client.GetAsync(candidate, HttpCompletionOption.ResponseHeadersRead, ct);
                    resp.EnsureSuccessStatusCode();

                    var total = resp.Content.Headers.ContentLength ?? expectedSize;

                    await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                        var buffer = new byte[81920];
                        long read = 0;
                        int n;
                        while ((n = await stream.ReadAsync(buffer, ct)) > 0)
                        {
                            await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                            read += n;
                            progress?.Invoke(read, total > 0 ? total : read);
                        }
                    }

                    if (expectedSize > 0)
                    {
                        var actual = new FileInfo(tmp).Length;
                        if (actual != expectedSize)
                        {
                            Log.Warn($"文件大小不符({actual}/{expectedSize})，换源重试：{candidate}");
                            try { File.Delete(tmp); } catch { }
                            last = new IOException("文件大小校验失败");
                            continue;
                        }
                    }

                    File.Move(tmp, destPath, true);
                    if (round > 1 || !string.Equals(candidate, url, StringComparison.OrdinalIgnoreCase))
                        Log.Info($"已改用备用源完成下载：{Path.GetFileName(destPath)}");
                    return true;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                    throw;
                }
                catch (Exception ex)
                {
                    // 连接超时（ConnectTimeout）也会抛 TaskCanceledException，这里按普通失败换源重试
                    last = ex;
                    Log.Warn($"下载失败(第{round}轮) {candidate} -> {ex.Message}");
                    try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
                }
            }

            if (round < 2) await Task.Delay(800, ct);
        }

        if (last != null) Log.Warn($"下载彻底失败 {url} -> {last.Message}");
        return false;
    }

    /// <summary>多线程批量下载</summary>
    public static async Task<(int ok, int failed)> DownloadBatchAsync(IEnumerable<DownloadItem> items, int threads,
        Action<int, int, long, long>? onProgress = null, CancellationToken ct = default)
    {
        var list = items?.ToList() ?? new List<DownloadItem>();
        if (list.Count == 0) return (0, 0);

        var total = list.Count;
        var done = 0;
        var failed = 0;
        long bytes = 0;
        var totalBytes = list.Sum(i => Math.Max(0, i.Size));

        var errors = new ConcurrentBag<string>();
        using var sem = new SemaphoreSlim(Math.Max(1, threads));

        var tasks = list.Select(async item =>
        {
            await sem.WaitAsync(ct);
            try
            {
                var ok = await DownloadFileAsync(item.Url, item.DestPath, item.Size, null, ct);
                if (!ok)
                {
                    Interlocked.Increment(ref failed);
                    errors.Add(item.Url);
                }
                else
                {
                    Interlocked.Add(ref bytes, Math.Max(0, item.Size));
                }

                var d = Interlocked.Increment(ref done);
                onProgress?.Invoke(d, total, Interlocked.Read(ref bytes), totalBytes);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref failed);
                errors.Add(item.Url + " : " + ex.Message);
                var d = Interlocked.Increment(ref done);
                onProgress?.Invoke(d, total, Interlocked.Read(ref bytes), totalBytes);
            }
            finally
            {
                sem.Release();
            }
        });

        await Task.WhenAll(tasks);

        if (!errors.IsEmpty)
            Log.Warn($"有 {errors.Count} 个文件下载失败，可重试：{errors.FirstOrDefault()}");

        return (total - failed, failed);
    }
}
