using System;
using System.Collections.Generic;
using HotUpdate.Config;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 签到奖励（来自 CheckInReward.bytes）。
    /// 最多 3 个道具槽：ItemIdN + ItemCountN，Id=0 表示空槽。
    /// </summary>
    [Serializable]
    public class CheckInRewardConfig
    {
        public int Id;
        public int Day;
        public int Energy;
        public int ItemId1;
        public int ItemCount1;
        public int ItemId2;
        public int ItemCount2;
        public int ItemId3;
        public int ItemCount3;
        public string Desc;

        /// <summary>非空道具槽 (itemId, count)。</summary>
        public void CollectItems(List<(int itemId, int count)> dst)
        {
            if (dst == null) return;
            if (ItemId1 > 0 && ItemCount1 > 0) dst.Add((ItemId1, ItemCount1));
            if (ItemId2 > 0 && ItemCount2 > 0) dst.Add((ItemId2, ItemCount2));
            if (ItemId3 > 0 && ItemCount3 > 0) dst.Add((ItemId3, ItemCount3));
        }
    }

    public static class CheckInRewardConfigTable
    {
        static readonly Dictionary<int, CheckInRewardConfig> _byDay = new();
        static bool _initialized;

        public static bool IsInitialized => _initialized;
        public static int Count => _byDay.Count;

        public static void Initialize(CheckInReward[] rows)
        {
            _byDay.Clear();
            _initialized = false;
            if (rows == null || rows.Length == 0)
            {
                Debug.LogError("[CheckInRewardConfigTable] CheckInReward 行为空");
                return;
            }

            foreach (var row in rows)
            {
                var cfg = new CheckInRewardConfig
                {
                    Id = row.Id,
                    Day = row.Day,
                    Energy = row.Energy,
                    ItemId1 = row.ItemId1,
                    ItemCount1 = row.ItemCount1,
                    ItemId2 = row.ItemId2,
                    ItemCount2 = row.ItemCount2,
                    ItemId3 = row.ItemId3,
                    ItemCount3 = row.ItemCount3,
                    Desc = row.Desc ?? ""
                };
                _byDay[cfg.Day] = cfg;
            }

            _initialized = _byDay.Count > 0;
            Debug.Log($"[CheckInRewardConfigTable] loaded {_byDay.Count} days");
        }

        public static CheckInRewardConfig GetByDay(int day)
        {
            if (_byDay.TryGetValue(day, out var cfg))
                return cfg;
            Debug.LogError($"[CheckInRewardConfigTable] 缺少签到 day={day}");
            return new CheckInRewardConfig { Day = day, Energy = 0, Desc = "" };
        }

        public static bool TryGetByDay(int day, out CheckInRewardConfig cfg)
            => _byDay.TryGetValue(day, out cfg);

        public static IReadOnlyDictionary<int, CheckInRewardConfig> All => _byDay;
    }
}
