using System.IO;
using System.Threading.Tasks;
using HotUpdate.Gameplay;
using UnityEngine;

namespace HotUpdate.Config
{
    /// <summary>
    /// 运行时加载 ExcelConfigCompiler 导出的二进制表（.bytes）。
    /// 导表：Tools → Excel Config Compiler（Excel 源目录建议填工程根相对路径 Excel）。
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>
        /// 加载 Level / Item / CheckInReward，写入对应内存表。
        /// </summary>
        public static Task LoadDefaultAsync()
        {
            LoadTable("Level", data =>
            {
                var rows = LevelTable.LoadAndCache(data);
                LevelConfigTable.Initialize(rows);
                Debug.Log($"[ConfigLoader] Level.bytes rows={rows.Length}");
            });

            LoadTable("Item", data =>
            {
                var rows = ItemTable.LoadAndCache(data);
                ItemConfigTable.Initialize(rows);
                Debug.Log($"[ConfigLoader] Item.bytes rows={rows.Length}");
            });

            LoadTable("CheckInReward", data =>
            {
                var rows = CheckInRewardTable.LoadAndCache(data);
                CheckInRewardConfigTable.Initialize(rows);
                Debug.Log($"[ConfigLoader] CheckInReward.bytes rows={rows.Length}");
            });

            return Task.CompletedTask;
        }

        static void LoadTable(string tableName, System.Action<byte[]> onData)
        {
            byte[] data = LoadBytes(tableName);
            if (data == null || data.Length == 0)
            {
                Debug.LogError($"[ConfigLoader] 未找到 {tableName}.bytes。请先 Tools → Excel Config Compiler 导表。");
                return;
            }

            try
            {
                onData(data);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                Debug.LogError($"[ConfigLoader] 加载 {tableName}.bytes 失败");
            }
        }

        static byte[] LoadBytes(string tableName)
        {
#if UNITY_EDITOR
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "Bundles", "Config", tableName + ".bytes"));
            if (File.Exists(path))
                return File.ReadAllBytes(path);
#endif
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
