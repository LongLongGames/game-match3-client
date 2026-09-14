using UnityEngine;

namespace HotUpdate.Services
{
    /// <summary>
    /// 记录最近一次资源加载走的是编辑器 AssetDatabase 还是真机 AB。
    /// DebugConsole 直接读这里展示。
    /// </summary>
    public static class ResourceLoadMode
    {
        public enum Mode
        {
            Unknown,
            EditorAssetDatabase,
            AssetBundle
        }

        public static Mode Current { get; private set; } = Mode.Unknown;
        public static string LastAsset { get; private set; } = "";

        public static void SetEditor(string asset)
        {
            Current = Mode.EditorAssetDatabase;
            LastAsset = asset ?? "";
            Debug.Log($"[ResMode] Editor AssetDatabase: {asset}");
        }

        public static void SetAssetBundle(string asset)
        {
            Current = Mode.AssetBundle;
            LastAsset = asset ?? "";
            Debug.Log($"[ResMode] AssetBundle: {asset}");
        }

        public static string StatusText
        {
            get
            {
                switch (Current)
                {
                    case Mode.EditorAssetDatabase:
                        return "资源加载: 编辑器 AssetDatabase" + (string.IsNullOrEmpty(LastAsset) ? "" : $" ({LastAsset})");
                    case Mode.AssetBundle:
                        return "资源加载: 真资源 AB" + (string.IsNullOrEmpty(LastAsset) ? "" : $" ({LastAsset})");
                    default:
                        return "资源加载: 尚未加载";
                }
            }
        }
    }
}
