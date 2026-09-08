using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.Services
{
    /// <summary>
    /// 资源门面。业务只调这里。
    /// 正式环境包装 AssetBundleFramework 的 ABManager / ResManager。
    /// 当前为 Editor / 极简占位实现。
    /// </summary>
    public class ResService : IResService
    {
        bool _inited;

        public async UniTask InitializeAsync(CancellationToken ct = default)
        {
            if (_inited) return;
            // await ResManager.InitializeAsync(ct); // 对接 AB 框架
            await UniTask.Yield(ct);
            _inited = true;
            Debug.Log("[Res] Initialized (mock)");
        }

        public async UniTask<T> LoadAsync<T>(string bundle, string asset, CancellationToken ct = default) where T : Object
        {
            await InitializeAsync(ct);
#if UNITY_EDITOR
            // Editor 直接 Resources / AssetDatabase 也可，这里返回 null 占位
            await UniTask.Yield(ct);
            return null;
#else
            // return await ABManager.Instance.LoadAssetAsync<T>(bundle, asset, ct);
            await UniTask.Yield(ct);
            return null;
#endif
        }

        public async UniTask<GameObject> LoadUIAsync(string name, CancellationToken ct = default)
        {
            return await LoadAsync<GameObject>("ui/" + name.ToLower(), name, ct);
        }
    }
}
