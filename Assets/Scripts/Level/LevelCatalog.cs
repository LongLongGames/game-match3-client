using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 极简关卡表。正式项目可改为从 AB 里的 JSON 加载。
/// </summary>
[Serializable]
public class LevelDef
{
    public int id;
    public string name;
    public int chapter;      // saga 章节
    public int starsToUnlock; // 累计星数门槛（示例）
}

public static class LevelCatalog
{
    static List<LevelDef> _levels;

    public static IReadOnlyList<LevelDef> All
    {
        get
        {
            if (_levels == null) BuildDefault();
            return _levels;
        }
    }

    static void BuildDefault()
    {
        _levels = new List<LevelDef>();
        for (int i = 1; i <= 20; i++)
        {
            _levels.Add(new LevelDef
            {
                id = i,
                name = "Level " + i,
                chapter = (i - 1) / 5 + 1,
                starsToUnlock = Math.Max(0, (i - 1) * 1)
            });
        }
    }

    public static LevelDef Get(int id)
    {
        foreach (var l in All)
            if (l.id == id) return l;
        return null;
    }
}
