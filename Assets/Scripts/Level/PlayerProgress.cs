using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 玩家关卡进度。可序列化进 profile.extra_json。
/// </summary>
[Serializable]
public class PlayerProgressData
{
    public int maxUnlocked = 1;
    public List<int> levelIds = new List<int>();
    public List<int> stars = new List<int>();
}

public class PlayerProgress
{
    PlayerProgressData _data = new PlayerProgressData { maxUnlocked = 1 };

    public int MaxUnlocked => _data.maxUnlocked;

    public int GetStars(int levelId)
    {
        for (int i = 0; i < _data.levelIds.Count; i++)
            if (_data.levelIds[i] == levelId) return _data.stars[i];
        return 0;
    }

    public void SetStars(int levelId, int star)
    {
        for (int i = 0; i < _data.levelIds.Count; i++)
        {
            if (_data.levelIds[i] == levelId)
            {
                _data.stars[i] = Math.Max(_data.stars[i], star);
                if (star > 0 && levelId >= _data.maxUnlocked)
                    _data.maxUnlocked = levelId + 1;
                return;
            }
        }
        _data.levelIds.Add(levelId);
        _data.stars.Add(star);
        if (star > 0 && levelId >= _data.maxUnlocked)
            _data.maxUnlocked = levelId + 1;
    }

    public bool IsUnlocked(int levelId) => levelId <= _data.maxUnlocked;

    public string ToJson() => JsonUtility.ToJson(_data);

    public void LoadFromJson(string json)
    {
        if (string.IsNullOrEmpty(json) || json == "{}")
        {
            _data = new PlayerProgressData { maxUnlocked = 1 };
            return;
        }
        try
        {
            _data = JsonUtility.FromJson<PlayerProgressData>(json) ?? new PlayerProgressData { maxUnlocked = 1 };
        }
        catch
        {
            _data = new PlayerProgressData { maxUnlocked = 1 };
        }
    }
}
