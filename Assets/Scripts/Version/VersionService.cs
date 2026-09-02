using System;
using System.Collections;
using UnityEngine;

public enum VersionResult
{
    Ok,
    ForceAppUpdate,
    Failed
}

/// <summary>
/// 版本检查 + 启动时完整资源下载。
/// </summary>
public class VersionService
{
    readonly ResourceUpdater _updater = new ResourceUpdater();

    public string AppStoreUrl { get; private set; }

    public IEnumerator CheckAndUpdate(
        Action<float, string> onProgress,
        Action<VersionResult, string> onDone)
    {
        onProgress?.Invoke(0f, "checking version...");

        var localRes = LocalVersionStore.GetResourceVersion();
        var url =
            $"{ApiConfig.GameBaseUrl}/api/v1/game/version-check" +
            $"?game_id={ApiConfig.GameId}" +
            $"&channel={ApiConfig.Channel}" +
            $"&platform={ApiConfig.Platform}" +
            $"&region={ApiConfig.Region}" +
            $"&client_version_code={ApiConfig.ClientVersionCode}" +
            $"&resource_version={Uri.EscapeDataString(localRes)}";

        VersionCheckResponse resp = null;
        string err = null;

        yield return HttpClient.Get(url,
            json =>
            {
                try { resp = JsonUtility.FromJson<VersionCheckResponse>(json); }
                catch (Exception ex) { err = "parse: " + ex.Message; }
            },
            e => err = e);

        if (resp == null)
        {
            onDone?.Invoke(VersionResult.Failed, err ?? "version-check failed");
            yield break;
        }

        if (resp.force_update_app)
        {
            AppStoreUrl = resp.app_store_url;
            onDone?.Invoke(VersionResult.ForceAppUpdate, "force app update");
            yield break;
        }

        // 后端已算 need_resource_update；再兜底比一次本地
        bool need = resp.need_resource_update ||
                    !string.Equals(localRes, resp.resource?.resource_version, StringComparison.Ordinal);

        if (!need || resp.resource == null)
        {
            onProgress?.Invoke(1f, "resource up to date");
            onDone?.Invoke(VersionResult.Ok, null);
            yield break;
        }

        bool ok = false;
        string updateErr = null;
        yield return _updater.FullUpdate(
            resp.resource,
            onProgress,
            () => ok = true,
            e => updateErr = e);

        if (!ok)
        {
            onDone?.Invoke(VersionResult.Failed, updateErr ?? "resource update failed");
            yield break;
        }

        LocalVersionStore.SetResourceVersion(resp.resource.resource_version);
        onDone?.Invoke(VersionResult.Ok, null);
    }

    public ResourceUpdater Updater => _updater;
}
