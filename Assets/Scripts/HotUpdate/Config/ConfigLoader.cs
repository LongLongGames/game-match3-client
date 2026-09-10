using System.IO;
using System.Threading.Tasks;
using HotUpdate.Gameplay;
using UnityEngine;

namespace HotUpdate.Config
{
    /// <summary>
    /// 运行时加载 ExcelConfigCompiler 导出的二进制表（.bytes）。
    /// 导表：Tools → Excel Config Compiler。
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>
        /// 编辑器优先读 Assets/Bundles/Config；真机读 StreamingAssets/Config。
        /// 也可从 Resources/Config 加载。
        /// 成功后写入 LevelConfigTable。
        /// </summary>
        public static Task LoadDefaultAsync()
        {
            byte[] data = LoadBytes("Level");
            if (data == null || data.Length == 0)
            {
                Debug.LogError("[ConfigLoader] 未找到 Level.bytes。请先执行 Tools → Excel Config Compiler 导表。");
                return Task.CompletedTask;
            }

            var rows = LevelTable.LoadAndCache(data);
            LevelConfigTable.Initialize(rows);
            Debug.Log($"[ConfigLoader] Level.bytes loaded, rows={rows.Length}");
            return Task.CompletedTask;
        }

        static byte[] LoadBytes(string tableName)
        {
#if UNITY_EDITOR
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Bundles", "Config", tableName + ".bytes"));
            if (File.Exists(path))
                return File.ReadAllBytes(path);
#endif
            // Resources: Assets/Resources/Config/Level.bytes → "Config/Level"
            var ta = Resources.Load<TextAsset>("Config/" + tableName);
            if (ta != null)
                return ta.bytes;

            string streaming = Path.Combine(Application.streamingAssetsPath, "Config", tableName + ".bytes");
            if (File.Exists(streaming))
                return File.ReadAllBytes(streaming);

            return null;
        }
    }
}
