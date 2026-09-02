using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 原生 AssetBundle 完整下载器（启动时一次性补齐，不做场景按需）。
/// 不做代码热更。
/// </summary>
public class ResourceUpdater
{
    public string LocalAbRoot => Path.Combine(Application.persistentDataPath, "ab");

    /// <summary>
    /// 根据 version-check 返回的资源信息，完整下载缺失/变更的 AB。
    /// </summary>
    public IEnumerator FullUpdate(
        ResourceInfo info,
        Action<float, string> onProgress,
        Action onOk,
        Action<string> onErr)
    {
        if (info == null)
        {
            onErr?.Invoke("resource info null");
            yield break;
        }

        Directory.CreateDirectory(LocalAbRoot);

        // 1. 拉 Manifest
        string manifestUrl = info.manifest_url;
        if (string.IsNullOrEmpty(manifestUrl))
        {
            // 约定：cdn_main_url + "manifest.json"
            var baseUrl = info.cdn_main_url?.TrimEnd('/') + "/";
            manifestUrl = baseUrl + "manifest.json";
        }

        byte[] manifestBytes = null;
        string err = null;
        yield return HttpClient.DownloadBytesOnce(manifestUrl, b => manifestBytes = b, e => err = e,
            p => onProgress?.Invoke(p * 0.1f, "manifest"));

        if (manifestBytes == null && !string.IsNullOrEmpty(info.cdn_fallback_url))
        {
            err = null;
            var fb = info.cdn_fallback_url.TrimEnd('/') + "/manifest.json";
            yield return HttpClient.DownloadBytesOnce(fb, b => manifestBytes = b, e => err = e,
                p => onProgress?.Invoke(p * 0.1f, "manifest(fallback)"));
        }

        if (manifestBytes == null)
        {
            onErr?.Invoke("download manifest failed: " + err);
            yield break;
        }

        AbManifest manifest;
        try
        {
            var json = Encoding.UTF8.GetString(manifestBytes);
            manifest = JsonUtility.FromJson<AbManifest>(json);
        }
        catch (Exception ex)
        {
            onErr?.Invoke("parse manifest: " + ex.Message);
            yield break;
        }

        if (manifest?.files == null || manifest.files.Count == 0)
        {
            // 无文件也视为成功（仅版本号）
            onProgress?.Invoke(1f, "done");
            onOk?.Invoke();
            yield break;
        }

        // 2. 计算需要下载的列表
        var toDownload = new System.Collections.Generic.List<AbFileEntry>();
        foreach (var f in manifest.files)
        {
            var path = Path.Combine(LocalAbRoot, f.name);
            if (!File.Exists(path) || !HashMatch(path, f.hash))
                toDownload.Add(f);
        }

        if (toDownload.Count == 0)
        {
            onProgress?.Invoke(1f, "up to date");
            onOk?.Invoke();
            yield break;
        }

        long total = 0;
        foreach (var f in toDownload) total += Math.Max(f.size, 1);
        long done = 0;

        string baseMain = (info.cdn_main_url ?? "").TrimEnd('/') + "/";
        string baseFb = string.IsNullOrEmpty(info.cdn_fallback_url)
            ? null
            : info.cdn_fallback_url.TrimEnd('/') + "/";

        foreach (var f in toDownload)
        {
            byte[] data = null;
            string fileErr = null;
            var url = baseMain + f.name;

            yield return HttpClient.DownloadBytesOnce(url, b => data = b, e => fileErr = e,
                p =>
                {
                    float overall = total <= 0 ? 1f : (done + f.size * p) / (float)total;
                    onProgress?.Invoke(0.1f + overall * 0.9f, f.name);
                });

            if (data == null && baseFb != null)
            {
                fileErr = null;
                yield return HttpClient.DownloadBytesOnce(baseFb + f.name, b => data = b, e => fileErr = e);
            }

            if (data == null)
            {
                onErr?.Invoke($"download {f.name} failed: {fileErr}");
                yield break;
            }

            if (!string.IsNullOrEmpty(f.hash) && !ComputeMd5(data).Equals(f.hash, StringComparison.OrdinalIgnoreCase))
            {
                onErr?.Invoke($"hash mismatch: {f.name}");
                yield break;
            }

            var dest = Path.Combine(LocalAbRoot, f.name);
            var dir = Path.GetDirectoryName(dest);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllBytes(dest, data);
            done += Math.Max(f.size, 1);
        }

        onProgress?.Invoke(1f, "done");
        onOk?.Invoke();
    }

    static bool HashMatch(string path, string expected)
    {
        if (string.IsNullOrEmpty(expected)) return true;
        try
        {
            var bytes = File.ReadAllBytes(path);
            return ComputeMd5(bytes).Equals(expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    static string ComputeMd5(byte[] data)
    {
        using var md5 = MD5.Create();
        var hash = md5.ComputeHash(data);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// 从本地加载已下载的 AB（给后续玩法用）。
    /// </summary>
    public AssetBundle LoadLocalAb(string name)
    {
        var path = Path.Combine(LocalAbRoot, name);
        if (!File.Exists(path)) return null;
        return AssetBundle.LoadFromFile(path);
    }
}
