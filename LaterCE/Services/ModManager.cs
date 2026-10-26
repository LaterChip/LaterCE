using System.IO.Compression;
using System.Text.Json;
using LaterCE.Models;

namespace LaterCE.Services;

public class ModManager
{
    public static string InstanceRoot(string versionId) => AppPaths.InstanceDir(versionId);

    public static string ModsDir(string versionId) => Path.Combine(InstanceRoot(versionId), "mods");
    public static string ResourcePacksDir(string versionId) => Path.Combine(InstanceRoot(versionId), "resourcepacks");
    public static string ShaderPacksDir(string versionId) => Path.Combine(InstanceRoot(versionId), "shaderpacks");
    public static string SavesDir(string versionId) => Path.Combine(InstanceRoot(versionId), "saves");
    public static string ConfigDir(string versionId) => Path.Combine(InstanceRoot(versionId), "config");

    public static string FolderFor(string projectType) => projectType switch
    {
        "resourcepack" => "resourcepacks",
        "shader" => "shaderpacks",
        "datapack" => "datapacks",
        _ => "mods"
    };

    /// <summary>从 Modrinth 安装到对应目录</summary>
    public async Task<string?> InstallFromModrinthAsync(string versionId, ModrinthVersion version,
        string projectType = "mod", CancellationToken ct = default)
    {
        var dir = Path.Combine(InstanceRoot(versionId), FolderFor(projectType));
        var path = await AppState.Modrinth.DownloadAsync(version, dir, null, ct);

        if (path != null)
            Log.Info($"已安装：{Path.GetFileName(path)} → {dir}");

        return path;
    }

    /// <summary>解析 Modrinth 整合包（.mrpack）</summary>
    public async Task<bool> InstallMrpackAsync(string mrpackPath, string versionId, string? targetDir = null,
        IProgress<double>? progress = null, CancellationToken ct = default)
    {
        try
        {
            var root = targetDir ?? InstanceRoot(versionId);
            Directory.CreateDirectory(root);

            var extractDir = Path.Combine(root, "__mrpack_tmp");
            if (Directory.Exists(extractDir)) Directory.Delete(extractDir, true);
            ZipFile.ExtractToDirectory(mrpackPath, extractDir);

            var indexPath = Path.Combine(extractDir, "modrinth.index.json");
            if (!File.Exists(indexPath))
            {
                Log.Error("该文件不是有效的 Modrinth 整合包（缺少 modrinth.index.json）。");
                return false;
            }

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath, ct));
            var indexRoot = doc.RootElement;

            var files = new List<(string path, string url, long size)>();
            if (indexRoot.TryGetProperty("files", out var filesElement) &&
                filesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in filesElement.EnumerateArray())
                {
                    if (!f.TryGetProperty("path", out var p)) continue;
                    var relative = p.GetString() ?? "";
                    if (string.IsNullOrEmpty(relative)) continue;

                    var url = "";
                    if (f.TryGetProperty("downloads", out var downloads) &&
                        downloads.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var d in downloads.EnumerateArray())
                        {
                            url = d.GetString() ?? "";
                            if (!string.IsNullOrEmpty(url)) break;
                        }
                    }

                    var size = f.TryGetProperty("fileSize", out var fs) ? fs.GetInt64() : 0;
                    if (!string.IsNullOrEmpty(url)) files.Add((relative, url, size));
                }
            }

            Log.Info($"整合包包含 {files.Count} 个待下载文件，开始下载…");

            var done = 0;
            foreach (var item in files)
            {
                ct.ThrowIfCancellationRequested();
                var dest = Path.Combine(root, item.path.Replace('/', Path.DirectorySeparatorChar));
                await Downloader.DownloadFileAsync(item.url, dest, item.size, null, ct);
                done++;
                progress?.Report((double)done / Math.Max(1, files.Count));
            }

            // 覆盖 overrides
            var overrides = Path.Combine(extractDir, "overrides");
            if (Directory.Exists(overrides))
                CopyDirectory(overrides, root);

            var clientOverrides = Path.Combine(extractDir, "client-overrides");
            if (Directory.Exists(clientOverrides))
                CopyDirectory(clientOverrides, root);

            try { Directory.Delete(extractDir, true); } catch { }

            Log.Info("整合包安装完成。");
            return true;
        }
        catch (OperationCanceledException)
        {
            Log.Warn("整合包安装已取消。");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error("安装整合包失败：" + ex.Message);
            return false;
        }
    }

    public static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, dir);
            Directory.CreateDirectory(Path.Combine(target, rel));
        }

        foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(source, file);
            var dest = Path.Combine(target, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }

    /// <summary>统计实例已安装的内容数量</summary>
    public static (int mods, int resourcepacks, int saves) CountInstalled(string versionId)
    {
        int Count(string dir) => Directory.Exists(dir)
            ? Directory.GetFiles(dir).Count(f => f.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) ||
                                                 f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            : 0;

        var saves = Directory.Exists(SavesDir(versionId))
            ? Directory.GetDirectories(SavesDir(versionId)).Length
            : 0;

        return (Count(ModsDir(versionId)), Count(ResourcePacksDir(versionId)), saves);
    }
}
