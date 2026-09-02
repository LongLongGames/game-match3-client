using UnityEngine;

public static class LocalVersionStore
{
    public static string GetResourceVersion()
    {
        return PlayerPrefs.GetString(ApiConfig.PrefKeyResourceVersion, "");
    }

    public static void SetResourceVersion(string version)
    {
        PlayerPrefs.SetString(ApiConfig.PrefKeyResourceVersion, version ?? "");
        PlayerPrefs.Save();
    }

    public static string GetAccessToken()
    {
        return PlayerPrefs.GetString(ApiConfig.PrefKeyAccessToken, "");
    }

    public static void SetAccessToken(string token)
    {
        PlayerPrefs.SetString(ApiConfig.PrefKeyAccessToken, token ?? "");
        PlayerPrefs.Save();
        HttpClient.AccessToken = token;
    }

    public static void ClearToken()
    {
        PlayerPrefs.DeleteKey(ApiConfig.PrefKeyAccessToken);
        PlayerPrefs.Save();
        HttpClient.AccessToken = null;
    }
}
