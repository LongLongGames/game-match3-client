using System.IO;
using System.Threading.Tasks;
using Cathei.BakingSheet;
using Cathei.BakingSheet.Unity;
using HotUpdate.Gameplay;
using UnityEngine;

namespace HotUpdate.Config
{
    /// <summary>
    /// 运行时加载导表产物（当前为 JSON；模板正式格式将迁 MessagePack .bytes，接口保持一次加载进内存）。
    /// </summary>
    public static class ConfigLoader
    {
        public static async Task<GameSheetContainer> LoadFromDirectoryAsync(string absoluteDir)
        {
            var container = new GameSheetContainer(UnityLogger.Default);
            var converter = new JsonSheetConverter(absoluteDir);
            await container.Bake(converter);
            return container;
        }

        /// <summary>
        /// 编辑器：Assets/Bundles/Config；真机：StreamingAssets/Config（需把导表产物打进包或 AB）。
        /// 加载成功后写入 LevelConfigTable。
        /// </summary>
        public static async Task<GameSheetContainer> LoadDefaultAsync()
        {
#if UNITY_EDITOR
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "Bundles", "Config"));
#else
            string dir = Path.Combine(Application.streamingAssetsPath, "Config");
#endif
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning($"[ConfigLoader] 配置目录不存在: {dir}");
            }
            else
            {
                Debug.Log($"[ConfigLoader] loading from {dir}");
            }

            var container = await LoadFromDirectoryAsync(dir);
            LevelConfigTable.Initialize(container);
            return container;
        }
    }
}
