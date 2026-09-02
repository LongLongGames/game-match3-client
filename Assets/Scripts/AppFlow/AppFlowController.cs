using System;
using UnityEngine;

/// <summary>
/// 全局流程控制。UI 脚本只调 Goto，不互跳。
/// </summary>
public class AppFlowController : MonoBehaviour
{
    public static AppFlowController Instance { get; private set; }

    public AppState State { get; private set; } = AppState.Boot;

    public event Action<AppState, AppState> OnStateChanged;

    // 服务单例（极简，不做 DI）
    public VersionService Version { get; } = new VersionService();
    public AuthService Auth { get; } = new AuthService();
    public PlayerService Player { get; } = new PlayerService();
    public PlayerProgress Progress { get; } = new PlayerProgress();
    public LeaderboardService Leaderboard { get; } = new LeaderboardService();
    public GameplayService Gameplay { get; } = new GameplayService();

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        Auth.TryRestoreToken();
    }

    public void Goto(AppState next)
    {
        var prev = State;
        State = next;
        Debug.Log($"[AppFlow] {prev} -> {next}");
        OnStateChanged?.Invoke(prev, next);
    }
}
