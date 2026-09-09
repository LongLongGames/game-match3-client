using System;
using System.IO;
using System.Threading.Tasks;
using Cathei.BakingSheet;
using Cathei.BakingSheet.Unity;
using HotUpdate.Config;
using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// 唯一导表入口：从工程根目录 Excel/ 读取 xlsx，
    /// 输出 JSON 到 Assets/Bundles/Config（客户端）以及同级 game-match3-server/config（服务端）。
    /// BakingSheet 以 C# 类为 schema，故导表必须在客户端 Editor 完成。
    /// </summary>
    public static class ConfigExportEditor
    {
        private const string MenuPath = "Tools/导表/Export Config (Excel → JSON)";

        [MenuItem(MenuPath)]
        public static void ExportConfig()
        {
            ExportConfigAsync().Forget();
        }

        private static async Task ExportConfigAsync()
        {
            try
            {
                // Application.dataPath = .../game-match3-client/Assets
                string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                string excelDir = Path.Combine(projectRoot, "Excel");
                string clientOutDir = Path.Combine(Application.dataPath, "Bundles", "Config");
                // 与 client 同级的 server
                string serverOutDir = Path.GetFullPath(Path.Combine(projectRoot, "..", "game-match3-server", "config"));

                if (!Directory.Exists(excelDir))
                {
                    Directory.CreateDirectory(excelDir);
                    EditorUtility.DisplayDialog(
                        "导表",
                        $"Excel 目录不存在，已创建：\n{excelDir}\n请放入 .xlsx 后重试。",
                        "确定");
                    return;
                }

                var xlsxFiles = Directory.GetFiles(excelDir, "*.xlsx");
                if (xlsxFiles.Length == 0)
                {
                    EditorUtility.DisplayDialog(
                        "导表",
                        $"Excel 目录下没有 .xlsx：\n{excelDir}",
                        "确定");
                    return;
                }

                Debug.Log($"[ConfigExport] ExcelDir={excelDir}");
                Debug.Log($"[ConfigExport] ClientOut={clientOutDir}");
                Debug.Log($"[ConfigExport] ServerOut={serverOutDir}");

                var logger = UnityLogger.Default;
                var container = new GameSheetContainer(logger);
                var excelConverter = new ExcelSheetConverter(excelDir);

                // Bake = 从 Excel 导入并校验
                await container.Bake(excelConverter);

                Directory.CreateDirectory(clientOutDir);
                Directory.CreateDirectory(serverOutDir);

                await container.Store(new JsonSheetConverter(clientOutDir));
                await container.Store(new JsonSheetConverter(serverOutDir));

                AssetDatabase.Refresh();

                string msg =
                    $"导表完成。\n" +
                    $"Excel: {excelDir}\n" +
                    $"Client: {clientOutDir}\n" +
                    $"Server: {serverOutDir}\n" +
                    $"已写入 JSON（工作表名 = Sheet 属性名，如 Level）。";
                Debug.Log($"[ConfigExport] {msg.Replace("\n", " | ")}");
                EditorUtility.DisplayDialog("导表成功", msg, "确定");
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorUtility.DisplayDialog("导表失败", e.Message, "确定");
            }
        }

        /// <summary>
        /// 简单 fire-and-forget，避免未观察 Task 警告。
        /// </summary>
        private static async void Forget(this Task task)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
