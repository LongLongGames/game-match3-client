using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 启动场景唯一脚本（挂到空摄像机场景的空物体上）。
/// AOT 侧负责：HybridCLR 热更加载 → 反射调用热更入口 → 移交控制权。
/// </summary>
public class GameBootstrap : MonoBehaviour
{
    [Header("HybridCLR")]
    [Tooltip("热更程序集名（与 HybridCLR 配置一致）")]
    public string hotUpdateAssemblyName = "HotUpdate";

    [Tooltip("热更入口类型全名")]
    public string entryTypeName = "HotUpdate.AppEntry";

    [Tooltip("热更入口方法名（static async UniTask）")]
    public string entryMethodName = "StartAsync";

    [Header("调试")]
    public bool useEditorDirectLoad = true;

    void Start()
    {
        RunAsync().Forget();
    }

    async UniTaskVoid RunAsync()
    {
        DontDestroyOnLoad(gameObject);
        Debug.Log("[GameBootstrap] Boot start");

        try
        {
            // 1. 初始化 HybridCLR（AOT 侧）
            await InitHybridCLRAsync();

            // 2. 加载热更程序集
            var assembly = await LoadHotUpdateAssemblyAsync();
            if (assembly == null)
            {
                Debug.LogError("[GameBootstrap] HotUpdate assembly load failed");
                return;
            }

            // 3. 反射调用热更入口
            var type = assembly.GetType(entryTypeName);
            if (type == null)
            {
                Debug.LogError($"[GameBootstrap] Type not found: {entryTypeName}");
                return;
            }

            var method = type.GetMethod(entryMethodName, BindingFlags.Public | BindingFlags.Static);
            if (method == null)
            {
                Debug.LogError($"[GameBootstrap] Method not found: {entryMethodName}");
                return;
            }

            Debug.Log("[GameBootstrap] Hand over to HotUpdate");
            var task = (UniTask)method.Invoke(null, null);
            await task;
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    async UniTask InitHybridCLRAsync()
    {
        // HybridCLR 正式环境在此补充 AOT 元数据、加载 dll 等。
        // 当前极简版仅占位，真实项目请接入 HybridCLR 官方 LoadDll / LoadMetadataForAOTAssembly。
        await UniTask.Yield();
        Debug.Log("[GameBootstrap] HybridCLR init (placeholder)");
    }

    async UniTask<Assembly> LoadHotUpdateAssemblyAsync()
    {
#if UNITY_EDITOR
        if (useEditorDirectLoad)
        {
            // Editor 下直接用已编译程序集，方便迭代
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (asm.GetName().Name == hotUpdateAssemblyName)
                    return asm;
            }
            Debug.LogWarning("[GameBootstrap] Editor: assembly not found in AppDomain, fallback reflection later");
            return null;
        }
#endif
        // 运行时：从 AB / StreamingAssets 加载 dll bytes 后 Assembly.Load
        // 实际项目配合 AssetBundleFramework + HybridCLR.Runtime.LoadImage
        await UniTask.Yield();
        return null;
    }
}
