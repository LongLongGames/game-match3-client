using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LeaderboardService
{
    public IEnumerator SubmitScore(long score, string nickname, Action<bool, string> onDone)
    {
        var body = new SubmitScoreRequest
        {
            game_id = ApiConfig.GameId,
            board_id = "default",
            score = score,
            nickname = nickname ?? "",
            extra_json = "{}"
        };

        var url = ApiConfig.GameBaseUrl + "/api/v1/leaderboard/score";
        string err = null;
        bool ok = false;

        yield return HttpClient.PostJson(url, JsonUtility.ToJson(body),
            _ => ok = true,
            e => err = e,
            auth: true);

        onDone?.Invoke(ok, err);
    }

    public IEnumerator GetTop(int limit, Action<List<ScoreEntry>, string> onDone)
    {
        var url = $"{ApiConfig.GameBaseUrl}/api/v1/leaderboard/top?game_id={ApiConfig.GameId}&limit={limit}";
        string err = null;
        var list = new List<ScoreEntry>();

        yield return HttpClient.Get(url,
            json =>
            {
                // 后端若直接返回数组，JsonUtility 不支持顶层数组，需包一层或手写解析。
                // 极简：尝试当对象；失败则忽略。
                try
                {
                    // 兼容 {"items":[...]} 或 后端改成对象包装
                    if (json.TrimStart().StartsWith("["))
                        json = "{\"items\":" + json + "}";
                    var wrap = JsonUtility.FromJson<TopListResponse>(json);
                    if (wrap?.items != null) list = wrap.items;
                }
                catch (Exception ex) { err = "parse: " + ex.Message; }
            },
            e => err = e);

        onDone?.Invoke(list, err);
    }
}
