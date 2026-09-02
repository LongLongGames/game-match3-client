using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// 极简 HTTP 封装。所有请求走协程，自动带 JWT。
/// </summary>
public class HttpClient
{
    public static string AccessToken { get; set; }

    public static IEnumerator Get(string url, Action<string> onOk, Action<string> onErr, bool auth = false)
    {
        using var req = UnityWebRequest.Get(url);
        if (auth && !string.IsNullOrEmpty(AccessToken))
            req.SetRequestHeader("Authorization", "Bearer " + AccessToken);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onErr?.Invoke($"{req.responseCode} {req.error}");
            yield break;
        }
        onOk?.Invoke(req.downloadHandler.text);
    }

    public static IEnumerator PostJson(string url, string json, Action<string> onOk, Action<string> onErr, bool auth = false)
    {
        var body = Encoding.UTF8.GetBytes(json ?? "{}");
        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        if (auth && !string.IsNullOrEmpty(AccessToken))
            req.SetRequestHeader("Authorization", "Bearer " + AccessToken);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onErr?.Invoke($"{req.responseCode} {req.error}");
            yield break;
        }
        onOk?.Invoke(req.downloadHandler.text);
    }

    public static IEnumerator PutJson(string url, string json, Action<string> onOk, Action<string> onErr, bool auth = true)
    {
        var body = Encoding.UTF8.GetBytes(json ?? "{}");
        using var req = new UnityWebRequest(url, "PUT");
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        if (auth && !string.IsNullOrEmpty(AccessToken))
            req.SetRequestHeader("Authorization", "Bearer " + AccessToken);

        yield return req.SendWebRequest();

        if (req.result != UnityWebRequest.Result.Success)
        {
            onErr?.Invoke($"{req.responseCode} {req.error}");
            yield break;
        }
        onOk?.Invoke(req.downloadHandler.text);
    }

    public static IEnumerator DownloadBytesOnce(string url, Action<byte[]> onOk, Action<string> onErr, Action<float> onProgress = null)
    {
        if (string.IsNullOrEmpty(url))
        {
            onErr?.Invoke("url empty");
            yield break;
        }

        using var req = UnityWebRequest.Get(url);
        var op = req.SendWebRequest();
        while (!op.isDone)
        {
            onProgress?.Invoke(req.downloadProgress);
            yield return null;
        }

        if (req.result != UnityWebRequest.Result.Success)
        {
            onErr?.Invoke($"{req.responseCode} {req.error}");
            yield break;
        }
        onOk?.Invoke(req.downloadHandler.data);
    }
}
