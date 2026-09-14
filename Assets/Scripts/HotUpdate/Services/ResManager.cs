using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace HotUpdate.Services
{
    /// <summary>
    /// 资源门面。业务只调这里，底层走 AssetBundleFramework 的 ABManager。
    /// 启动时由 AppFlow / AppEntry 调用 InitializeAsync。
    ///
    /// 约定：
    /// - bundle 路径小写，如 ui/ui_login、characters/hero、audio/sfx
    /// - asset 为资源文件名（无扩展名），与 AB 内名字一致
    /// </summary>
    public static class ResManager
    {
        static bool _inited;

        public static bool IsReady => _inited && ABManager.Instance != null;

        // ---------- 生命周期 ----------

        /// <summary>启动时调一次</summary>
        public static async UniTask InitializeAsync(CancellationToken ct = default)
        {
            if (_inited) return;
            if (ABManager.Instance == null)
            {
                Debug.LogError("[Res] ABManager.Instance is null。请确认 AssetBundleFramework 已正确安装并初始化。");
                return;
            }
            await ABManager.Instance.InitializeAsync(ct);
            _inited = true;
            Debug.Log($"[Res] ready version={ABManager.Instance.GetVersion()}");
        }

        /// <summary>登出 / 回登录时可调</summary>
        public static void ReleaseAll()
        {
            ABManager.Instance?.UnloadAll(true);
            _inited = false;
            Debug.Log("[Res] ReleaseAll");
        }

        // ---------- 通用 ----------

        public static UniTask<T> LoadAsync<T>(string bundle, string asset, CancellationToken ct = default)
            where T : Object
        {
            EnsureReady();
            return ABManager.Instance.LoadAssetAsync<T>(bundle, asset, ct);
        }

        public static void Unload(string bundle) => ABManager.Instance?.UnloadBundle(bundle);

        public static int GetRefCount(string bundle) => ABManager.Instance?.GetRefCount(bundle) ?? 0;

        public static bool IsLoaded(string bundle) =>
            ABManager.Instance != null && ABManager.Instance.IsLoaded(bundle);

        public static string GetVersion() =>
            ABManager.Instance != null ? ABManager.Instance.GetVersion() : null;

        // ---------- UI（UIToolkit UXML）----------

        /// <summary>
        /// 加载 UIToolkit 面板。
        /// 例：LoadUIAsync("UI_Login") → bundle=ui/ui_login, asset=UI_Login
        /// </summary>
        public static UniTask<VisualTreeAsset> LoadUIAsync(string name, CancellationToken ct = default)
            => LoadAsync<VisualTreeAsset>($"ui/{name.ToLowerInvariant()}", name, ct);

        /// <summary>uGUI / 普通 Prefab（若有混用）</summary>
        public static UniTask<GameObject> LoadUIPrefabAsync(string name, CancellationToken ct = default)
            => LoadAsync<GameObject>($"ui/{name.ToLowerInvariant()}", name, ct);

        // ---------- 角色 / 道具 Prefab ----------

        public static UniTask<GameObject> LoadModelAsync(string name, CancellationToken ct = default)
            => LoadAsync<GameObject>($"characters/{name.ToLowerInvariant()}", name, ct);

        public static UniTask<GameObject> LoadPropAsync(string name, CancellationToken ct = default)
            => LoadAsync<GameObject>($"props/{name.ToLowerInvariant()}", name, ct);

        /// <summary>任意路径 Prefab</summary>
        public static UniTask<GameObject> LoadPrefabAsync(string bundle, string asset, CancellationToken ct = default)
            => LoadAsync<GameObject>(bundle, asset, ct);

        // ---------- 图集 / 贴图 ----------

        public static UniTask<Sprite> LoadSpriteAsync(string bundle, string asset, CancellationToken ct = default)
            => LoadAsync<Sprite>(bundle, asset, ct);

        /// <summary>
        /// 便捷：sprites/{name} 包内同名 Sprite。
        /// 例：LoadSpriteAsync("gem_red") → sprites/gem_red + gem_red
        /// </summary>
        public static UniTask<Sprite> LoadSpriteAsync(string name, CancellationToken ct = default)
            => LoadAsync<Sprite>($"sprites/{name.ToLowerInvariant()}", name, ct);

        public static UniTask<Texture2D> LoadTextureAsync(string bundle, string asset, CancellationToken ct = default)
            => LoadAsync<Texture2D>(bundle, asset, ct);

        public static UniTask<Texture2D> LoadTextureAsync(string name, CancellationToken ct = default)
            => LoadAsync<Texture2D>($"textures/{name.ToLowerInvariant()}", name, ct);

        // ---------- 音效 / 音乐 ----------

        public static UniTask<AudioClip> LoadAudioAsync(string bundle, string asset, CancellationToken ct = default)
            => LoadAsync<AudioClip>(bundle, asset, ct);

        /// <summary>
        /// 便捷：audio/sfx/{name}
        /// 例：LoadSfxAsync("click") → audio/sfx + click
        /// </summary>
        public static UniTask<AudioClip> LoadSfxAsync(string name, CancellationToken ct = default)
            => LoadAsync<AudioClip>("audio/sfx", name, ct);

        /// <summary>
        /// 便捷：audio/bgm/{name}
        /// 例：LoadBgmAsync("home") → audio/bgm + home
        /// </summary>
        public static UniTask<AudioClip> LoadBgmAsync(string name, CancellationToken ct = default)
            => LoadAsync<AudioClip>("audio/bgm", name, ct);

        // ---------- 配置 / 其它 ----------

        /// <summary>
        /// 二进制配置（如 Excel 导出的 .bytes）。
        /// 例：LoadConfigBytesAsync("Level") → config/level + Level
        /// </summary>
        public static UniTask<TextAsset> LoadConfigBytesAsync(string name, CancellationToken ct = default)
            => LoadAsync<TextAsset>($"config/{name.ToLowerInvariant()}", name, ct);

        public static UniTask<Material> LoadMaterialAsync(string bundle, string asset, CancellationToken ct = default)
            => LoadAsync<Material>(bundle, asset, ct);

        public static UniTask<Font> LoadFontAsync(string name, CancellationToken ct = default)
            => LoadAsync<Font>($"fonts/{name.ToLowerInvariant()}", name, ct);

        // ---------- 内部 ----------

        static void EnsureReady()
        {
            if (!_inited || ABManager.Instance == null)
                throw new System.InvalidOperationException("[Res] 先调用 await ResManager.InitializeAsync()");
        }
    }
}
