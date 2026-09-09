using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using HotUpdate.Network;
using HotUpdate.Gameplay;

namespace HotUpdate.Services
{
    public class PlayerService : IPlayerService
    {
        readonly IHttpClient _http;
        readonly ApiConfig _config;

        public PlayerProfile Profile { get; private set; } = new PlayerProfile
        {
            nickname = "Player",
            level = 1
        };

        public PlayerStateResponse State { get; private set; }

        public int Energy { get; private set; } = 30;
        public int EnergyMax { get; private set; } = 30;
        public long Gold { get; private set; }
        public int UnlockedMap { get; private set; } = 1;
        public int CurrentMapId { get; private set; } = 1;

        // 本地缓存进度：level_id -> stars（弱网 / 离线）
        LevelProgressItem[] _localLevels;

        public PlayerService(IHttpClient http, ApiConfig config)
        {
            _http = http;
            _config = config;
            EnsureLocalLevels();
        }

        void EnsureLocalLevels()
        {
            if (_localLevels != null && _localLevels.Length == LevelConfigTable.LevelsPerMap)
                return;
            _localLevels = new LevelProgressItem[LevelConfigTable.LevelsPerMap];
            for (var i = 0; i < LevelConfigTable.LevelsPerMap; i++)
            {
                _localLevels[i] = new LevelProgressItem
                {
                    level_id = i + 1,
                    stars = 0,
                    best_steps = 0,
                    clear_count = 0,
                    last_score = 0
                };
            }
        }

        public async UniTask RefreshProfileAsync(CancellationToken ct = default)
        {
            try
            {
                var url = $"{_config.GameBaseUrl}/api/v1/user/profile?game_id={_config.GameId}";
                var text = await _http.GetAsync(url, auth: true, ct);
                var p = JsonUtility.FromJson<PlayerProfile>(text);
                if (p != null)
                {
                    Profile = p;
                    if (string.IsNullOrEmpty(Profile.nickname))
                        Profile.nickname = "Player";
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Player] RefreshProfile failed, use local: " + e.Message);
            }
        }

        public async UniTask RefreshStateAsync(int mapId = 1, CancellationToken ct = default)
        {
            CurrentMapId = mapId < 1 ? 1 : mapId;
            try
            {
                var url = $"{_config.GameBaseUrl}/api/v1/user/state?game_id={_config.GameId}&map_id={CurrentMapId}";
                var text = await _http.GetAsync(url, auth: true, ct);
                var s = JsonUtility.FromJson<PlayerStateResponse>(text);
                if (s != null)
                {
                    ApplyState(s);
                    return;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Player] RefreshState failed, use local cache: " + e.Message);
            }

            // 离线占位
            EnsureLocalLevels();
            State = new PlayerStateResponse
            {
                game_id = _config.GameId,
                energy = Energy,
                energy_max = EnergyMax,
                energy_regen_seconds = 300,
                seconds_to_next_energy = 0,
                gold = Gold,
                unlocked_map = UnlockedMap,
                map_id = CurrentMapId,
                levels_per_map = LevelConfigTable.LevelsPerMap,
                cleared_on_map = CountCleared(_localLevels),
                map_unlock_clear_count = 10,
                levels = _localLevels
            };
        }

        void ApplyState(PlayerStateResponse s)
        {
            State = s;
            Energy = s.energy;
            EnergyMax = s.energy_max > 0 ? s.energy_max : 30;
            Gold = s.gold;
            UnlockedMap = s.unlocked_map > 0 ? s.unlocked_map : 1;
            CurrentMapId = s.map_id > 0 ? s.map_id : CurrentMapId;

            if (s.levels != null && s.levels.Length > 0)
            {
                EnsureLocalLevels();
                foreach (var item in s.levels)
                {
                    if (item.level_id < 1 || item.level_id > _localLevels.Length) continue;
                    _localLevels[item.level_id - 1] = item;
                }
            }
        }

        static int CountCleared(LevelProgressItem[] levels)
        {
            if (levels == null) return 0;
            var n = 0;
            foreach (var l in levels)
                if (l != null && l.stars > 0) n++;
            return n;
        }

        public bool TryGetNextPlayableLevel(out int mapId, out int levelId)
        {
            mapId = CurrentMapId > 0 ? CurrentMapId : 1;
            if (mapId > UnlockedMap)
                mapId = UnlockedMap;

            EnsureLocalLevels();
            // 顺序：第一关无星可打；其后需前一关至少 1 星
            for (var i = 0; i < _localLevels.Length; i++)
            {
                var lv = i + 1;
                if (_localLevels[i].stars > 0)
                    continue; // 已通关仍可重打，但优先找未通关
                if (lv == 1 || _localLevels[i - 1].stars > 0)
                {
                    levelId = lv;
                    return Energy > 0;
                }
                // 卡在前一关
                levelId = lv;
                return false;
            }

            // 全通：重打最后一关或提示下一图
            levelId = LevelConfigTable.LevelsPerMap;
            if (CountCleared(_localLevels) >= 10 && UnlockedMap > mapId)
            {
                mapId = mapId + 1;
                levelId = 1;
                CurrentMapId = mapId;
                EnsureLocalLevels();
                for (var i = 0; i < _localLevels.Length; i++)
                    _localLevels[i] = new LevelProgressItem { level_id = i + 1 };
            }
            return Energy > 0;
        }

        public async UniTask<ClearLevelResponse> ClearLevelAsync(
            int mapId, int levelId, int stars, int steps, long score,
            CancellationToken ct = default)
        {
            // 本地先扣体力 / 加金币 / 记进度（弱网立刻反馈）
            if (Energy > 0) Energy--;
            var goldGain = 50L * Math.Max(1, stars);
            Gold += goldGain;

            EnsureLocalLevels();
            if (levelId >= 1 && levelId <= _localLevels.Length)
            {
                var item = _localLevels[levelId - 1];
                item.stars = Math.Max(item.stars, stars);
                if (item.best_steps <= 0 || steps < item.best_steps)
                    item.best_steps = steps;
                item.clear_count++;
                item.last_score = score;
                _localLevels[levelId - 1] = item;
            }

            if (CountCleared(_localLevels) >= 10 && mapId >= UnlockedMap)
                UnlockedMap = mapId + 1;

            ClearLevelResponse serverResp = null;
            try
            {
                var body = new ClearLevelRequest
                {
                    game_id = _config.GameId,
                    map_id = mapId,
                    level_id = levelId,
                    stars = stars,
                    steps = steps,
                    score = score
                };
                var json = JsonUtility.ToJson(body);
                var url = $"{_config.GameBaseUrl}/api/v1/user/level/clear";
                var text = await _http.PostJsonAsync(url, json, auth: true, ct);
                serverResp = JsonUtility.FromJson<ClearLevelResponse>(text);
                if (serverResp != null)
                {
                    Energy = serverResp.energy;
                    EnergyMax = serverResp.energy_max > 0 ? serverResp.energy_max : EnergyMax;
                    Gold = serverResp.gold;
                    UnlockedMap = serverResp.unlocked_map > 0 ? serverResp.unlocked_map : UnlockedMap;
                    Debug.Log($"[Player] Clear OK energy={Energy} gold={Gold} unlocked_map={UnlockedMap}");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Player] ClearLevel server failed, kept local: " + e.Message);
            }

            return serverResp ?? new ClearLevelResponse
            {
                game_id = _config.GameId,
                map_id = mapId,
                level_id = levelId,
                stars = stars,
                best_steps = steps,
                clear_count = 1,
                energy = Energy,
                energy_max = EnergyMax,
                gold = Gold,
                gold_gained = goldGain,
                unlocked_map = UnlockedMap
            };
        }
    }
}
