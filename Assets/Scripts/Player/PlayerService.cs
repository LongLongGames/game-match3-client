using System;
using System.Collections;
using UnityEngine;

public class PlayerService
{
    public ProfileResponse Profile { get; private set; }

    public IEnumerator EnsureProfile(Action<bool, string> onDone)
    {
        var url = $"{ApiConfig.GameBaseUrl}/api/v1/user/profile?game_id={ApiConfig.GameId}";
        string err = null;
        ProfileResponse resp = null;

        yield return HttpClient.Get(url,
            json =>
            {
                try { resp = JsonUtility.FromJson<ProfileResponse>(json); }
                catch (Exception ex) { err = "parse: " + ex.Message; }
            },
            e => err = e,
            auth: true);

        if (resp == null)
        {
            onDone?.Invoke(false, err ?? "profile failed");
            yield break;
        }

        Profile = resp;
        onDone?.Invoke(true, null);
    }

    public IEnumerator UpdateNickname(string nickname, Action<bool, string> onDone)
    {
        var body = new UpdateProfileRequest
        {
            game_id = ApiConfig.GameId,
            nickname = nickname,
            level = Profile?.level ?? 1,
            extra_json = Profile?.extra_json ?? "{}"
        };

        var url = ApiConfig.GameBaseUrl + "/api/v1/user/profile";
        string err = null;
        ProfileResponse resp = null;

        yield return HttpClient.PutJson(url, JsonUtility.ToJson(body),
            json =>
            {
                try { resp = JsonUtility.FromJson<ProfileResponse>(json); }
                catch (Exception ex) { err = "parse: " + ex.Message; }
            },
            e => err = e);

        if (resp == null)
        {
            onDone?.Invoke(false, err ?? "update failed");
            yield break;
        }

        Profile = resp;
        onDone?.Invoke(true, null);
    }
}
