using UnityEngine;

/// <summary>
/// 全局 API / 渠道配置。可改成 ScriptableObject，当前用静态方便极简项目。
/// </summary>
public static class ApiConfig
{
    // ----- 环境 -----
    public static string MpBaseUrl = "http://localhost:8080";
    public static string GameBaseUrl = "http://localhost:8081";

    public const string GameId = "match3";

    // 打包时由 CI 或 Editor 写入；运行时可被覆盖
    public static string Channel = "official";
    public static string Platform =
#if UNITY_ANDROID
        "android";
#elif UNITY_IOS
        "ios";
#elif UNITY_STANDALONE_WIN
        "windows";
#else
        "windows";
#endif
    public static string Region = "cn";

    // 整包数字版本（与后端 client_version_code 对齐）
    public static int ClientVersionCode = 10000;
    public static string ClientVersion = "1.0.0";

    // 本地资源版本键
    public const string PrefKeyResourceVersion = "local_resource_version";
    public const string PrefKeyAccessToken = "access_token";
}
