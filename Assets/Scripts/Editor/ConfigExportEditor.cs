using UnityEditor;
using UnityEngine;

namespace Editor
{
    /// <summary>
    /// 导表入口已统一到 Excel Config Compiler。
    /// 保留菜单项，避免旧文档/习惯失效。
    /// </summary>
    public static class ConfigExportEditor
    {
        const string MenuPath = "Tools/导表/Export Config (ExcelConfigCompiler)";

        [MenuItem(MenuPath)]
        public static void ExportConfig()
        {
            // 打开官方导表窗口；实际编译在窗口内一键完成
            EditorApplication.ExecuteMenuItem("Tools/Excel Config Compiler");
            Debug.Log(
                "[ConfigExport] 请使用 Excel Config Compiler 窗口完成导表。\n" +
                "建议：Excel 源=工程根/Excel，代码输出=Assets/Scripts/HotUpdate/Config/Generated，" +
                "二进制输出=Assets/Bundles/Config，命名空间=HotUpdate.Config");
        }
    }
}
