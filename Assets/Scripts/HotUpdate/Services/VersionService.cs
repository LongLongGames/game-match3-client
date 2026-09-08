using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.Services
{
    /// <summary>
    /// 极简版本 / 本地存储。真实项目对接 AssetBundleFramework 的 ABConfig + Updater。
    /// </summary>
    public class VersionService : IVersionService
    {
        const string TokenKey = "access_token";

        public async UniTask<bool> CheckAndUpdateAsync(CancellationToken ct = default)
        {
            // TODO: 接入 AssetBundleFramework
            // await ABConfig.LoadAsync(ct);
            // await updater.CheckAndUpdateAsync(progress, ct);
            // await ABManager.Instance.InitializeAsync(ct);
            // await ResManager.InitializeAsync(ct);

            await UniTask.Delay(300, cancellationToken: ct); // 模拟检查
            Debug.Log("[Version] CheckAndUpdate OK (mock)");
            return true;
        }

        public string GetAccessToken() => PlayerPrefs.GetString(TokenKey, "");
        public void SetAccessToken(string token) => PlayerPrefs.SetString(TokenKey, token ?? "");
        public void ClearToken() => PlayerPrefs.DeleteKey(TokenKey);
    }
}
