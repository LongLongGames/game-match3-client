using System;
using System.Collections.Generic;

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
    public string token_type;
    public long expires_in;
}

[Serializable]
public class VersionCheckResponse
{
    public int code;
    public bool force_update_app;
    public string app_store_url;
    public string client_version;
    public int client_version_code;
    public int min_client_version_code;
    public ResourceInfo resource;
    public int gray_rate;
    public bool in_gray;
    public string status;
    public bool need_resource_update;
    public long server_time;
    public string extra_json;
}

[Serializable]
public class ResourceInfo
{
    public string resource_version;
    public string package_name;
    public string cdn_main_url;
    public string cdn_fallback_url;
    public string manifest_url;
}

[Serializable]
public class ProfileResponse
{
    public string id;
    public string mp_account_id;
    public string game_id;
    public string nickname;
    public int level;
    public string extra_json;
    public string created_at;
    public string updated_at;
}

[Serializable]
public class UpdateProfileRequest
{
    public string game_id;
    public string nickname;
    public int level;
    public string extra_json;
}

[Serializable]
public class SubmitScoreRequest
{
    public string game_id;
    public string board_id;
    public long score;
    public string nickname;
    public string extra_json;
}

[Serializable]
public class ScoreEntry
{
    public string id;
    public string mp_account_id;
    public string game_id;
    public string board_id;
    public long score;
    public string nickname;
    public string extra_json;
    public string updated_at;
}

[Serializable]
public class TopListResponse
{
    // 若后端直接返回数组，Http 层会用 JsonHelper 包一层；此处兼容对象包一层的情况
    public List<ScoreEntry> items;
}
