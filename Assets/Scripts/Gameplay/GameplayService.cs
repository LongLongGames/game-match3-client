using System;

/// <summary>
/// 对局入口占位。真正三消逻辑以后填。
/// </summary>
public class GameplayService
{
    public int CurrentLevelId { get; private set; }
    public long LastScore { get; private set; }
    public int LastStars { get; private set; }

    public void StartLevel(int levelId)
    {
        CurrentLevelId = levelId;
        LastScore = 0;
        LastStars = 0;
    }

    /// <summary>
    /// 本地结算（示例：外部把分数传进来）。
    /// </summary>
    public void FinishLevel(long score, int stars)
    {
        LastScore = score;
        LastStars = stars;
    }
}
