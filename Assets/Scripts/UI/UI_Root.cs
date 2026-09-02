using UnityEngine;

/// <summary>
/// 根据 AppState 显隐四个 UI 根节点。
/// 把 CheckUpdate / Login / Home / Game 四个面板拖进来即可。
/// </summary>
public class UI_Root : MonoBehaviour
{
    public GameObject panelCheckUpdate;
    public GameObject panelLogin;
    public GameObject panelHome;
    public GameObject panelGame;

    void OnEnable()
    {
        if (AppFlowController.Instance != null)
        {
            AppFlowController.Instance.OnStateChanged += OnFlowChanged;
            Apply(AppFlowController.Instance.State);
        }
    }

    void OnDisable()
    {
        if (AppFlowController.Instance != null)
            AppFlowController.Instance.OnStateChanged -= OnFlowChanged;
    }

    void OnFlowChanged(AppState prev, AppState next) => Apply(next);

    void Apply(AppState state)
    {
        Set(panelCheckUpdate, state == AppState.CheckUpdate);
        Set(panelLogin, state == AppState.Login);
        Set(panelHome, state == AppState.Home);
        Set(panelGame, state == AppState.Game);
    }

    static void Set(GameObject go, bool on)
    {
        if (go != null) go.SetActive(on);
    }
}
