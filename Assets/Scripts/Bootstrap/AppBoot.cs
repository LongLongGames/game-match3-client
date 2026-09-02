using UnityEngine;

/// <summary>
/// 挂到启动场景空物体上。负责创建 Flow 并进入 CheckUpdate。
/// </summary>
public class AppBoot : MonoBehaviour
{
    [Header("可选：覆盖本地调试地址")]
    public string mpBaseUrl = "";
    public string gameBaseUrl = "";
    public string channel = "";
    public string region = "";

    void Start()
    {
        if (!string.IsNullOrEmpty(mpBaseUrl)) ApiConfig.MpBaseUrl = mpBaseUrl;
        if (!string.IsNullOrEmpty(gameBaseUrl)) ApiConfig.GameBaseUrl = gameBaseUrl;
        if (!string.IsNullOrEmpty(channel)) ApiConfig.Channel = channel;
        if (!string.IsNullOrEmpty(region)) ApiConfig.Region = region;

        if (AppFlowController.Instance == null)
        {
            var go = new GameObject("AppFlow");
            go.AddComponent<AppFlowController>();
        }

        AppFlowController.Instance.Goto(AppState.CheckUpdate);
    }
}
