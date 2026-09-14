using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using HotUpdate.Services;

namespace HotUpdate.UI
{
    /// <summary>
    /// 从 AssetBundle（或 Editor AssetDatabase）加载 VisualTreeAsset 并实例化到 root。
    /// UIService 用它替代原先代码拼 VisualElement 的 Build* 方法。
    /// </summary>
    public static class UIPanelLoader
    {
        /// <summary>
        /// 加载 UXML 并挂到 parent 下。返回实例化的根节点。
        /// Editor 下优先 AssetDatabase，方便迭代；真机走 AB。
        /// </summary>
        public static async UniTask<VisualElement> LoadAsync(
            string assetName,
            VisualElement parent,
            CancellationToken ct = default)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent));

            VisualTreeAsset vta = null;

#if UNITY_EDITOR
            // 编辑器直读，无需每次打 AB
            vta = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"Assets/Bundles/UI/{assetName}.uxml");
            if (vta != null)
            {
                Debug.Log($"[UIPanelLoader] Editor AssetDatabase: {assetName}");
                ResourceLoadMode.SetEditor(assetName);
            }
#endif

            if (vta == null)
            {
                if (!ResManager.IsReady)
                {
                    Debug.LogWarning("[UIPanelLoader] ResManager 未初始化，尝试 InitializeAsync");
                    await ResManager.InitializeAsync(ct);
                }
                vta = await ResManager.LoadUIAsync(assetName, ct);
                if (vta != null)
                    ResourceLoadMode.SetAssetBundle(assetName);
            }

            if (vta == null)
            {
                Debug.LogError($"[UIPanelLoader] 加载失败: ui/{assetName.ToLowerInvariant()}/{assetName}");
                return null;
            }

            var page = vta.Instantiate();
            page.name = assetName;
            page.style.flexGrow = 1;
            page.style.width = Length.Percent(100);
            page.style.height = Length.Percent(100);
            parent.Add(page);
            return page;
        }

        /// <summary>卸载指定 UI bundle（引用计数 -1）</summary>
        public static void Unload(string assetName)
        {
            if (string.IsNullOrEmpty(assetName)) return;
            ResManager.Unload($"ui/{assetName.ToLowerInvariant()}");
        }
    }
}
