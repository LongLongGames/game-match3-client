using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 挂在检查更新界面。不要求 Prefab 字段齐全，缺什么就空着。
/// </summary>
public class UI_CheckUpdate : MonoBehaviour
{
    public Text statusText;
    public Slider progressBar;
    public Button btnRetry;
    public Button btnOpenStore;

    void OnEnable()
    {
        if (btnRetry != null)
        {
            btnRetry.onClick.RemoveAllListeners();
            btnRetry.onClick.AddListener(StartCheck);
            btnRetry.gameObject.SetActive(false);
        }
        if (btnOpenStore != null)
        {
            btnOpenStore.onClick.RemoveAllListeners();
            btnOpenStore.onClick.AddListener(OpenStore);
            btnOpenStore.gameObject.SetActive(false);
        }

        if (AppFlowController.Instance != null &&
            AppFlowController.Instance.State == AppState.CheckUpdate)
        {
            StartCheck();
        }

        if (AppFlowController.Instance != null)
            AppFlowController.Instance.OnStateChanged += OnFlowChanged;
    }

    void OnDisable()
    {
        if (AppFlowController.Instance != null)
            AppFlowController.Instance.OnStateChanged -= OnFlowChanged;
    }

    void OnFlowChanged(AppState prev, AppState next)
    {
        if (next == AppState.CheckUpdate)
            StartCheck();
    }

    public void StartCheck()
    {
        if (btnRetry != null) btnRetry.gameObject.SetActive(false);
        if (btnOpenStore != null) btnOpenStore.gameObject.SetActive(false);
        SetStatus("检查版本...");
        SetProgress(0);

        var flow = AppFlowController.Instance;
        if (flow == null) return;

        StartCoroutine(flow.Version.CheckAndUpdate(
            (p, msg) =>
            {
                SetProgress(p);
                SetStatus(msg);
            },
            (result, err) =>
            {
                switch (result)
                {
                    case VersionResult.Ok:
                        SetStatus("更新完成");
                        SetProgress(1);
                        flow.Goto(AppState.Login);
                        break;
                    case VersionResult.ForceAppUpdate:
                        SetStatus("需要更新客户端");
                        if (btnOpenStore != null) btnOpenStore.gameObject.SetActive(true);
                        break;
                    default:
                        SetStatus("失败: " + err);
                        if (btnRetry != null) btnRetry.gameObject.SetActive(true);
                        break;
                }
            }));
    }

    void OpenStore()
    {
        var url = AppFlowController.Instance?.Version.AppStoreUrl;
        if (!string.IsNullOrEmpty(url))
            Application.OpenURL(url);
    }

    void SetStatus(string s)
    {
        if (statusText != null) statusText.text = s;
        Debug.Log("[CheckUpdate] " + s);
    }

    void SetProgress(float p)
    {
        if (progressBar != null) progressBar.value = Mathf.Clamp01(p);
    }
}
