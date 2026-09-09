using System;

namespace HotUpdate.Network
{
    [Serializable]
    public class LoginRequest
    {
        public string provider;
        public string app_id;
        public string device_id;
        public LoginAuthPayload auth_payload;
    }

    [Serializable]
    public class LoginAuthPayload
    {
        public string username;
        public string password;
    }

    [Serializable]
    public class LoginResponse
    {
        public string access_token;
        public string refresh_token;
    }

    [Serializable]
    public class ScoreRequest
    {
        public string game_id;
        public string board_id;
        public int score;
        public string nickname;
    }

    [Serializable]
    public class PlayerProfile
    {
        public string id;
        public string mp_account_id;
        public string game_id;
        public string nickname;
        public int level;
        public string extra_json;
    }

    // ---------- Match3 进度 / 经济（与 server 对齐）----------

    [Serializable]
    public class LevelProgressItem
    {
        public int level_id;
        public int stars;
        public int best_steps; // JsonUtility 无 int?，0 表示未通关时可配合 clear_count 判断
        public int clear_count;
        public long last_score;
    }

    [Serializable]
    public class PlayerStateResponse
    {
        public string game_id;
        public int energy;
        public int energy_max;
        public int energy_regen_seconds;
        public int seconds_to_next_energy;
        public long gold;
        public int unlocked_map;
        public int map_id;
        public int levels_per_map;
        public int cleared_on_map;
        public int map_unlock_clear_count;
        public LevelProgressItem[] levels;
    }

    [Serializable]
    public class ClearLevelRequest
    {
        public string game_id;
        public int map_id;
        public int level_id;
        public int stars;
        public int steps;
        public long score;
    }

    [Serializable]
    public class ClearLevelResponse
    {
        public string game_id;
        public int map_id;
        public int level_id;
        public int stars;
        public int best_steps;
        public int clear_count;
        public int energy;
        public int energy_max;
        public long gold;
        public long gold_gained;
        public int unlocked_map;
    }

    [Serializable]
    public class CheatRefillRequest
    {
        public string game_id;
    }
}
