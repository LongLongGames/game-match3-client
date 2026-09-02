using UnityEngine;
using UnityEngine.UI;

public class UI_Login : MonoBehaviour
{
    public InputField inputUser;
    public InputField inputPass;
    public Button btnLogin;
    public Text statusText;

    void OnEnable()
    {
        if (btnLogin != null)
        {
            btnLogin.onClick.RemoveAllListeners();
            btnLogin.onClick.AddListener(OnClickLogin);
        }

        // 已有 Token 可直接尝试进 Home（可选）
        var flow = AppFlowController.Instance;
        if (flow != null && flow.Auth.IsLoggedIn && flow.State == AppState.Login)
        {
            SetStatus("已有登录态，拉取资料...");
            StartCoroutine(AfterLogin());
        }

        if (flow != null)
            flow.OnStateChanged += OnFlowChanged;
    }

    void OnDisable()
    {
        if (AppFlowController.Instance != null)
            AppFlowController.Instance.OnStateChanged -= OnFlowChanged;
    }

    void OnFlowChanged(AppState prev, AppState next)
    {
        // 进入 Login 时清空状态即可
        if (next == AppState.Login)
            SetStatus("请登录");
    }

    public void OnClickLogin()
    {
        var user = inputUser != null ? inputUser.text : "tester1";
        var pass = inputPass != null ? inputPass.text : "test1234";
        SetStatus("登录中...");
        if (btnLogin != null) btnLogin.interactable = false;

        var flow = AppFlowController.Instance;
        StartCoroutine(flow.Auth.Login(user, pass, (ok, err) =>
        {
            if (btnLogin != null) btnLogin.interactable = true;
            if (!ok)
            {
                SetStatus("登录失败: " + err);
                return;
            }
            SetStatus("登录成功，拉取资料...");
            StartCoroutine(AfterLogin());
        }));
    }

    System.Collections.IEnumerator AfterLogin()
    {
        var flow = AppFlowController.Instance;
        bool ok = false;
        string err = null;
        yield return flow.Player.EnsureProfile((s, e) => { ok = s; err = e; });

        if (!ok)
        {
            SetStatus("资料失败: " + err);
            yield break;
        }

        // 从 extra_json 恢复进度
        flow.Progress.LoadFromJson(flow.Player.Profile?.extra_json);
        flow.Goto(AppState.Home);
    }

    void SetStatus(string s)
    {
        if (statusText != null) statusText.text = s;
        Debug.Log("[Login] " + s);
    }
}
