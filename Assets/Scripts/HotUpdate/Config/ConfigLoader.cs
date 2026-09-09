using System.IO;
using System.Threading.Tasks;
using Cathei.BakingSheet;
using Cathei.BakingSheet.Unity;
using UnityEngine;

namespace HotUpdate.Config
{
    /// <summary>
    /// 运行时从 Bundles/Config（或 StreamingAssets 等）加载 JSON 配置。
    /// 导表产物由 Editor 菜单写入 Assets/Bundles/Config。
    /// </summary>
    public static class ConfigLoader
    {
        /// <summary>
        /// 从指定目录加载全部表。目录下应有与 Sheet 属性同名的 .json 文件。
        /// </summary>
        public static async Task<GameSheetContainer> LoadFromDirectoryAsync(string absoluteDir)
        {
            var container = new GameSheetContainer(UnityLogger.Default);
            var converter = new JsonSheetConverter(absoluteDir);
            await container.Bake(converter);
            return container;
        }

        /// <summary>
        /// 默认路径：Application.streamingAssetsPath 或自定义 Bundles 解包路径。
        /// 当前工程导表输出在 Assets/Bundles/Config，打包进 AB 后需按 ResService 解析真实路径。
        /// </summary>
        public static async Task<GameSheetContainer> LoadDefaultAsync()
        {
            // 编辑器下可直接读 Assets/Bundles/Config；真机走 AB / StreamingAssets
#if UNITY_EDITOR
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "Bundles", "Config"));
#else
            string dir = Path.Combine(Application.streamingAssetsPath, "Config");
#endif
            if (!Directory.Exists(dir))
            {
                Debug.LogWarning($"[ConfigLoader] 配置目录不存在: {dir}");
            }

            return await LoadFromDirectoryAsync(dir);
        }
    }
}
