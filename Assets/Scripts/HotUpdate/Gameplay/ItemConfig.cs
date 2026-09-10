using System;
using System.Collections.Generic;
using HotUpdate.Config;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 道具静态配置（来自 Item.bytes）。
    /// Effect 约定：ClearOne / ClearRow / ClearCol / Bomb3x3 / Shuffle / ExtraSteps
    /// </summary>
    [Serializable]
    public class ItemConfig
    {
        public int Id;
        public string Name;
        public string Effect;
        public int Param;
        public string Icon;
        public string Desc;
    }

    public static class ItemConfigTable
    {
        static readonly Dictionary<int, ItemConfig> _byId = new();
        static bool _initialized;

        public static bool IsInitialized => _initialized;
        public static int Count => _byId.Count;

        public static void Initialize(Item[] rows)
        {
            _byId.Clear();
            _initialized = false;
            if (rows == null || rows.Length == 0)
            {
                Debug.LogError("[ItemConfigTable] Item 行为空");
                return;
            }

            foreach (var row in rows)
            {
                _byId[row.Id] = new ItemConfig
                {
                    Id = row.Id,
                    Name = row.Name ?? "",
                    Effect = row.Effect ?? "",
                    Param = row.Param,
                    Icon = row.Icon ?? "",
                    Desc = row.Desc ?? ""
                };
            }

            _initialized = _byId.Count > 0;
            Debug.Log($"[ItemConfigTable] loaded {_byId.Count} items");
        }

        public static ItemConfig Get(int id)
        {
            if (_byId.TryGetValue(id, out var cfg))
                return cfg;
            Debug.LogError($"[ItemConfigTable] 缺少道具 id={id}");
            return new ItemConfig { Id = id, Name = "?", Effect = "", Param = 0 };
        }

        public static bool TryGet(int id, out ItemConfig cfg)
            => _byId.TryGetValue(id, out cfg);

        public static IReadOnlyDictionary<int, ItemConfig> All => _byId;
    }
}
