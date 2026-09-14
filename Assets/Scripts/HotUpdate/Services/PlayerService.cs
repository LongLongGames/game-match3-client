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

        public int Energy { get; private set; }
        public int EnergyMax { get; private set; }
        public long Gold { get; private set; }
        public int UnlockedMap { get; private set; } = 1;
        public int CurrentMapId { get; private set; } = 1;

        LevelProgressItem[] _localLevels;

        public PlayerService(IHttpClient http, ApiConfig config)
        {
            _http = http;
            _config = config;
            EnergyMax = GameRuleConfig.EnergyMax;
            Energy = GameRuleConfig.EnergyMax;
            EnsureLocalLevels();
        }

        void EnsureLocalLevels()
        {
            var n = GameRuleConfig.LevelsPerMap > 0
                ? GameRuleConfig.LevelsPerMap
                : LevelConfigTable.LevelsPerMap;
            if (_localLevels != null && _localLevels.Length == n)
                return;
            _localLevels = new LevelProgressItem[n];
            for (var i = 0; i < n; i++)
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

            EnsureLocalLevels();
            State = new PlayerStateResponse
            {
                game_id = _config.GameId,
                energy = Energy,
                energy_max = EnergyMax,
                energy_regen_seconds = GameRuleConfig.EnergyRegenSeconds,
                seconds_to_next_energy = 0,
                gold = Gold,
                unlocked_map = UnlockedMap,
                map_id = CurrentMapId,
                levels_per_map = GameRuleConfig.LevelsPerMap,
                cleared_on_map = CountCleared(_localLevels),
                map_unlock_clear_count = GameRuleConfig.MapUnlockNeedClears,
                levels = _localLevels
            };
        }

        void ApplyState(PlayerStateResponse s)
        {
            State = s;
            Energy = s.energy;
            // 服务端有值用服务端，否则用本地规则
            EnergyMax = s.energy_max > 0 ? s.energy_max : GameRuleConfig.EnergyMax;
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
            var cost = GameRuleConfig.EnergyCostPerLevel;
            var minStars = GameRuleConfig.MinStarsToUnlockNextLevel;

            for (var i = 0; i < _localLevels.Length; i++)
            {
                var lv = i + 1;
                if (_localLevels[i].stars > 0)
                    continue;
                if (lv == 1 || _localLevels[i - 1].stars >= minStars)
                {
                    levelId = lv;
                    return Energy >= cost;
                }
                levelId = lv;
                return false;
            }

            levelId = GameRuleConfig.LevelsPerMap;
            if (CountCleared(_localLevels) >= GameRuleConfig.MapUnlockNeedClears && UnlockedMap > mapId)
            {
                mapId = mapId + 1;
                levelId = 1;
                CurrentMapId = mapId;
                EnsureLocalLevels();
                for (var i = 0; i < _localLevels.Length; i++)
                    _localLevels[i] = new LevelProgressItem { level_id = i + 1 };
            }
            return Energy >= cost;
        }

        /// <summary>
        /// 进关：优先 POST /api/v1/user/level/enter 由服务端扣体力并回写。
        /// 无 token 时离线回退本地扣。失败不改本地（除离线路径）。
        /// </summary>
        public async UniTask<(bool ok, string error)> EnterLevelAsync(int mapId, int levelId, CancellationToken ct = default)
        {
            var cost = GameRuleConfig.EnergyCostPerLevel;
            if (cost < 0) cost = 0;

            // 无 token：离线模式，仅本地扣
            if (string.IsNullOrEmpty(_http.AccessToken))
            {
                if (Energy < cost)
                {
                    Debug.LogWarning($"[Player] offline energy not enough: have={Energy} need={cost}");
                    return (false, "体力不足，请稍后再试");
                }
                Energy -= cost;
                Debug.Log($"[Player] offline spend energy for enter: -{cost}, left={Energy}");
                return (true, null);
            }

            try
            {
                var body = new EnterLevelRequest
                {
                    game_id = _config.GameId,
                    map_id = mapId,
                    level_id = levelId
                };
                var json = JsonUtility.ToJson(body);
                var url = $"{_config.GameBaseUrl}/api/v1/user/level/enter";
                var text = await _http.PostJsonAsync(url, json, auth: true, ct);
                var resp = JsonUtility.FromJson<EnterLevelResponse>(text);
                if (resp == null)
                    return (false, "进关失败：空响应");

                Energy = resp.energy;
                if (resp.energy_max > 0)
                    EnergyMax = resp.energy_max;
                Gold = resp.gold;
                if (resp.unlocked_map > 0)
                    UnlockedMap = resp.unlocked_map;

                Debug.Log($"[Player] enter OK map={mapId} lv={levelId} energy={Energy}/{EnergyMax} cost={resp.energy_cost}");
                return (true, null);
            }
            catch (UnauthorizedException)
            {
                throw;
            }
            catch (Exception e)
            {
                var msg = e.Message ?? "进关失败";
                Debug.LogWarning("[Player] EnterLevel failed: " + msg);
                if (msg.IndexOf("not enough energy", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (false, "体力不足，请稍后再试");
                if (msg.IndexOf("map locked", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (false, "地图未解锁");
                if (msg.IndexOf("previous level", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (false, "关卡未解锁");
                if (msg.IndexOf("level not in config", StringComparison.OrdinalIgnoreCase) >= 0)
                    return (false, "关卡配置不存在");
                return (false, "进关失败：" + msg);
            }
        }

        public async UniTask<ClearLevelResponse> ClearLevelAsync(
            int mapId, int levelId, int stars, int steps, long score,
            CancellationToken ct = default)
        {
            // 体力已在进关时扣除，这里不再扣
            var goldGain = (long)GameRuleConfig.GoldPerStar * Math.Max(1, stars);
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

            if (CountCleared(_localLevels) >= GameRuleConfig.MapUnlockNeedClears && mapId >= UnlockedMap)
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
                    // 服务端若仍在 clear 扣体力，会把 energy 写回来；
                    // 等服务端改为进关扣后，这里自然对齐。
                    // 在双端规则切换期：以「本地已进关扣过」为准，取更低值，避免被服务器加回。
                    if (serverResp.energy < Energy)
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
