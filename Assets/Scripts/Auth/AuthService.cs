using System;
using System.Collections;
using UnityEngine;

public class AuthService
{
    public bool IsLoggedIn => !string.IsNullOrEmpty(HttpClient.AccessToken);

    public IEnumerator Login(string username, string password, Action<bool, string> onDone)
    {
        var body = new LoginRequest
        {
            provider = "official",
            app_id = "test_app",
            device_id = SystemInfo.deviceUniqueIdentifier,
            auth_payload = new LoginAuthPayload
            {
                username = username,
                password = password
            }
        };

        var json = JsonUtility.ToJson(body);
        var url = ApiConfig.MpBaseUrl + "/api/v1/auth/login";

        string err = null;
        LoginResponse resp = null;

        yield return HttpClient.PostJson(url, json,
            text =>
            {
                try { resp = JsonUtility.FromJson<LoginResponse>(text); }
                catch (Exception ex) { err = "parse: " + ex.Message; }
            },
            e => err = e);

        if (resp == null || string.IsNullOrEmpty(resp.access_token))
        {
            onDone?.Invoke(false, err ?? "login failed");
            yield break;
        }

        LocalVersionStore.SetAccessToken(resp.access_token);
        onDone?.Invoke(true, null);
    }

    public void TryRestoreToken()
    {
        var t = LocalVersionStore.GetAccessToken();
        if (!string.IsNullOrEmpty(t))
            HttpClient.AccessToken = t;
    }

    public void Logout()
    {
        LocalVersionStore.ClearToken();
    }
}
