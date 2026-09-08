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
        public string user_id;
        public string nickname;
        public int level;
    }
}
