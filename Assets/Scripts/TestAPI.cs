using System;
using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// 联调脚本：登录 → 资料 → 状态(体力/金币/地图) → 通关 → 排行榜。
/// 挂到 TestAPI 场景任意物体上即可；Start 自动跑一遍。
/// </summary>
public class TestAPI : MonoBehaviour
{
    [Header("服务器地址")]
    public string mpBaseUrl = "http://localhost:11080"; // 中台固定
    public string gameBaseUrl = "http://localhost:13180"; //# 严格按 G=1 公式：13000 + 100 + 80 = 13180

    [Header("测试账号")]
    public string username = "tester1";
    public string password = "test1234";
    public string deviceId = "unity-test";

    [Header("游戏参数")]
    public string gameId = "match3";
    public string boardId = "default";
    public int testScore = 100;
    public string nickname = "UnityPlayer";

    [Header("通关测试")]
    public int mapId = 1;
    public int levelId = 1;
    public int stars = 3;
    public int steps = 12;
    public long levelScore = 3500;

    [Header("UI（可选）")]
    public Text logText;
    public Button btnRunAll;

    private string accessToken;

    void Start()
    {
        if (btnRunAll != null)
            btnRunAll.onClick.AddListener(() => StartCoroutine(RunFullFlow()));

        // 启动后自动跑一遍（想手动点按钮就注释掉）
        StartCoroutine(RunFullFlow());
    }

    public IEnumerator RunFullFlow()
    {
        Log("===== 开始 API 测试 =====");

        yield return Login();
        if (string.IsNullOrEmpty(accessToken))
        {
            Log("❌ 登录失败，后续跳过");
            yield break;
        }

        yield return GetProfile();
        yield return GetState(mapId);
        yield return ClearLevel(mapId, levelId, stars, steps, levelScore);
        yield return GetState(mapId);
        yield return SubmitScore(testScore);
        yield return GetTop(5);
        yield return GetMyRank();
        yield return CheatRefillEnergy();
        yield return GetState(mapId);

        Log("===== 全部完成 =====");
    }

    #region API 调用

    IEnumerator Login()
    {
        Log("1. MP 登录...");
        var body = new LoginRequest
        {
            provider = "official",
            app_id = "test_app",
            device_id = deviceId,
            auth_payload = new AuthPayload { username = username, password = password }
        };
        string json = JsonUtility.ToJson(body);
        string url = $"{mpBaseUrl}/api/v1/auth/login";

        using (var req = NewJsonPost(url, json, auth: false))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
            {
                Log($"登录失败: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }
            var resp = JsonUtility.FromJson<LoginResponse>(req.downloadHandler.text);
            accessToken = resp != null ? resp.access_token : null;
            Log(string.IsNullOrEmpty(accessToken) ? "❌ 无 token" : "✅ 登录成功");
        }
    }

    IEnumerator GetProfile()
    {
        Log("2. 获取玩家资料...");
        string url = $"{gameBaseUrl}/api/v1/user/profile?game_id={gameId}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Log($"资料失败: {req.error}\n{req.downloadHandler.text}");
            else
                Log($"✅ 资料: {req.downloadHandler.text}");
        }
    }

    IEnumerator GetState(int map)
    {
        Log($"3. 玩家状态 map={map}（体力/金币/进度）...");
        string url = $"{gameBaseUrl}/api/v1/user/state?game_id={gameId}&map_id={map}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Log($"状态失败: {req.error}\n{req.downloadHandler.text}");
            else
                Log($"✅ 状态: {req.downloadHandler.text}");
        }
    }

    IEnumerator ClearLevel(int map, int level, int star, int step, long score)
    {
        Log($"4. 通关 map={map} level={level} stars={star} steps={step}...");
        var body = new ClearLevelRequest
        {
            game_id = gameId,
            map_id = map,
            level_id = level,
            stars = star,
            steps = step,
            score = score
        };
        string json = JsonUtility.ToJson(body);
        string url = $"{gameBaseUrl}/api/v1/user/level/clear";
        using (var req = NewJsonPost(url, json, auth: true))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Log($"通关失败: {req.error}\n{req.downloadHandler.text}");
            else
                Log($"✅ 通关: {req.downloadHandler.text}");
        }
    }

    IEnumerator CheatRefillEnergy()
    {
        Log("5b. 开发回满体力...");
        var body = new CheatRefillRequest { game_id = gameId };
        string json = JsonUtility.ToJson(body);
        string url = $"{gameBaseUrl}/api/v1/user/energy/cheat-refill";
        using (var req = NewJsonPost(url, json, auth: true))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Log($"回满失败: {req.error}\n{req.downloadHandler.text}");
            else
                Log($"✅ 回满: {req.downloadHandler.text}");
        }
    }

    IEnumerator SubmitScore(int score)
    {
        Log("5. 提交排行榜分数...");
        var body = new ScoreRequest
        {
            game_id = gameId,
            board_id = boardId,
            score = score,
            nickname = nickname
        };
        string json = JsonUtility.ToJson(body);
        string url = $"{gameBaseUrl}/api/v1/leaderboard/score";
        using (var req = NewJsonPost(url, json, auth: true))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Log($"提交失败: {req.error}\n{req.downloadHandler.text}");
            else
                Log($"✅ 提交: {req.downloadHandler.text}");
        }
    }

    IEnumerator GetTop(int limit)
    {
        Log("6. 查排行榜 Top...");
        string url = $"{gameBaseUrl}/api/v1/leaderboard/top?game_id={gameId}&board_id={boardId}&limit={limit}";
        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Log($"排行榜失败: {req.error}\n{req.downloadHandler.text}");
            else
                Log($"✅ 排行榜: {req.downloadHandler.text}");
        }
    }

    IEnumerator GetMyRank()
    {
        Log("7. 查自己排名...");
        string url = $"{gameBaseUrl}/api/v1/leaderboard/me?game_id={gameId}&board_id={boardId}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success)
                Log($"自己排名失败: {req.error}\n{req.downloadHandler.text}");
            else
                Log($"✅ 我的排名: {req.downloadHandler.text}");
        }
    }

    #endregion

    UnityWebRequest NewJsonPost(string url, string json, bool auth)
    {
        var req = new UnityWebRequest(url, "POST");
        byte[] raw = Encoding.UTF8.GetBytes(json);
        req.uploadHandler = new UploadHandlerRaw(raw);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        if (auth && !string.IsNullOrEmpty(accessToken))
            req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
        return req;
    }

    void Log(string msg)
    {
        Debug.Log(msg);
        if (logText != null)
            logText.text += msg + "\n";
    }

    #region 数据类（JsonUtility）

    [Serializable]
    public class LoginRequest
    {
        public string provider;
        public string app_id;
        public string device_id;
        public AuthPayload auth_payload;
    }

    [Serializable]
    public class AuthPayload
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
    public class CheatRefillRequest
    {
        public string game_id;
    }

    #endregion
}
