using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 对局界面占位：模拟得分结算 → 提交排行榜 → 回 Home。
/// 真正三消玩法之后在此脚本或子模块扩展。
/// </summary>
public class UI_Game : MonoBehaviour
{
    public Text statusText;
    public Button btnWin;
    public Button btnLose;
    public Button btnBack;

    void OnEnable()
    {
        if (btnWin != null)
        {
            btnWin.onClick.RemoveAllListeners();
            btnWin.onClick.AddListener(() => Finish(true));
        }
        if (btnLose != null)
        {
            btnLose.onClick.RemoveAllListeners();
            btnLose.onClick.AddListener(() => Finish(false));
        }
        if (btnBack != null)
        {
            btnBack.onClick.RemoveAllListeners();
            btnBack.onClick.AddListener(() => AppFlowController.Instance?.Goto(AppState.Home));
        }

        if (AppFlowController.Instance != null)
        {
            AppFlowController.Instance.OnStateChanged += OnFlowChanged;
            if (AppFlowController.Instance.State == AppState.Game)
                ShowStart();
        }
    }

    void OnDisable()
    {
        if (AppFlowController.Instance != null)
            AppFlowController.Instance.OnStateChanged -= OnFlowChanged;
    }

    void OnFlowChanged(AppState prev, AppState next)
    {
        if (next == AppState.Game) ShowStart();
    }

    void ShowStart()
    {
        var id = AppFlowController.Instance?.Gameplay.CurrentLevelId ?? 0;
        SetStatus($"关卡 {id} 开始（占位，点胜利/失败模拟结算）");
    }

    void Finish(bool win)
    {
        var flow = AppFlowController.Instance;
        if (flow == null) return;

        long score = win ? 1000 + flow.Gameplay.CurrentLevelId * 10 : 100;
        int stars = win ? 3 : 0;
        flow.Gameplay.FinishLevel(score, stars);

        if (win)
            flow.Progress.SetStars(flow.Gameplay.CurrentLevelId, stars);

        SetStatus($"结算 score={score} stars={stars}，提交中...");

        var nick = flow.Player.Profile?.nickname ?? "player";
        StartCoroutine(flow.Leaderboard.SubmitScore(score, nick, (ok, err) =>
        {
            SetStatus(ok ? "提交成功，返回 Home" : "提交失败: " + err);
            // 可选：把进度写回 profile.extra_json
            // 此处极简直接回 Home
            flow.Goto(AppState.Home);
        }));
    }

    void SetStatus(string s)
    {
        if (statusText != null) statusText.text = s;
        Debug.Log("[Game] " + s);
    }
}
