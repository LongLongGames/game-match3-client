using System;
using System.Text;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

public class TestAPI : MonoBehaviour
{
    [Header("服务器地址")]
    public string mpBaseUrl = "http://localhost:8080";      // MP 网关
    public string gameBaseUrl = "http://localhost:8081";    // game-match3-server 网关

    [Header("测试账号")]
    public string username = "tester1";
    public string password = "test1234";
    public string deviceId = "unity-test";

    [Header("游戏参数")]
    public string gameId = "match3";
    public string boardId = "default";
    public int testScore = 100;
    public string nickname = "UnityPlayer";

    [Header("UI（可选）")]
    public Text logText;          // 拖一个 Text 显示日志
    public Button btnRunAll;      // 一键跑完整流程

    private string accessToken;

    void Start()
    {
        if (btnRunAll != null)
            btnRunAll.onClick.AddListener(() => StartCoroutine(RunFullFlow()));

        // 启动后自动跑一遍（想手动点按钮就注释掉）
        StartCoroutine(RunFullFlow());
    }

    /// <summary>
    /// 完整测试流程
    /// </summary>
    public IEnumerator RunFullFlow()
    {
        Log("===== 开始 API 测试 =====");

        // 1. 登录拿 Token
        yield return Login();
        if (string.IsNullOrEmpty(accessToken))
        {
            Log("❌ 登录失败，后续跳过");
            yield break;
        }

        // 2. 获取/创建玩家资料
        yield return GetProfile();

        // 3. 提交分数
        yield return SubmitScore(testScore);

        // 4. 查排行榜 Top
        yield return GetTop(5);

        // 5. 查自己排名
        yield return GetMyRank();

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
            auth_payload = new AuthPayload
            {
                username = username,
                password = password
            }
        };

        string json = JsonUtility.ToJson(body);
        using (var req = new UnityWebRequest($"{mpBaseUrl}/api/v1/auth/login", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Log($"登录失败: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            var resp = JsonUtility.FromJson<LoginResponse>(req.downloadHandler.text);
            accessToken = resp.access_token;
            Log($"✅ 登录成功, token 前 20 位: {accessToken?.Substring(0, Math.Min(20, accessToken.Length))}...");
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
            {
                Log($"获取资料失败: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            Log($"✅ 资料: {req.downloadHandler.text}");
        }
    }

    IEnumerator SubmitScore(int score)
    {
        Log($"3. 提交分数 {score}...");

        var body = new ScoreRequest
        {
            game_id = gameId,
            board_id = boardId,
            score = score,
            nickname = nickname
        };

        string json = JsonUtility.ToJson(body);
        using (var req = new UnityWebRequest($"{gameBaseUrl}/api/v1/leaderboard/score", "POST"))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Authorization", $"Bearer {accessToken}");

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Log($"提交分数失败: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            Log($"✅ 提交结果: {req.downloadHandler.text}");
        }
    }

    IEnumerator GetTop(int limit = 50)
    {
        Log($"4. 查排行榜 Top{limit}...");

        string url = $"{gameBaseUrl}/api/v1/leaderboard/top?game_id={gameId}&board_id={boardId}&limit={limit}";
        using (var req = UnityWebRequest.Get(url))
        {
            // 排行榜公开接口，可不带 Token
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Log($"查排行榜失败: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            Log($"✅ 排行榜: {req.downloadHandler.text}");
        }
    }

    IEnumerator GetMyRank()
    {
        Log("5. 查自己排名...");

        string url = $"{gameBaseUrl}/api/v1/leaderboard/me?game_id={gameId}&board_id={boardId}";
        using (var req = UnityWebRequest.Get(url))
        {
            req.SetRequestHeader("Authorization", $"Bearer {accessToken}");
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Log($"查自己排名失败: {req.error}\n{req.downloadHandler.text}");
                yield break;
            }

            Log($"✅ 我的排名: {req.downloadHandler.text}");
        }
    }

    #endregion

    #region 数据类（JsonUtility 用）

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
        // 其他字段按需加
    }

    [Serializable]
    public class ScoreRequest
    {
        public string game_id;
        public string board_id;
        public int score;
        public string nickname;
    }

    #endregion

    void Log(string msg)
    {
        Debug.Log(msg);
        if (logText != null)
            logText.text += msg + "\n";
    }
}