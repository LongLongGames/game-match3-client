using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using HotUpdate.Auth;
using HotUpdate.Gameplay;
using HotUpdate.Network;
using HotUpdate.Services;
using HotUpdate.UI;
using UnityEngine;
using VContainer.Unity;

namespace HotUpdate.DebugTools
{
    /// <summary>
    /// 运行时调试台：热更 + VContainer 可调临时函数。
    /// 开关：~ / F12；执行：输入命令回车，或点列表。
    /// 仅 Editor / Development Build。
    /// </summary>
    public sealed class DebugConsole : IStartable, ITickable, IDisposable
    {
        readonly IAuthService _auth;
        readonly IPlayerService _player;
        readonly IUIService _ui;
        readonly IHttpClient _http;
        readonly ApiConfig _api;

        bool _open;
        string _input = "";
        string _lastResult = "";
        Vector2 _scroll;
        readonly List<string> _log = new List<string>(64);
        const int MaxLog = 80;
        bool _registered;
        GUIStyle _btnStyle;
        GUIStyle _titleStyle;
        GUIStyle _statusStyle;
        GUIStyle _labelStyle;
        // 默认更大，避免手机/高 DPI 上看不清
        Rect _win = new Rect(20, 40, 720, 860);

        public DebugConsole(
            IAuthService auth,
            IPlayerService player,
            IUIService ui,
            IHttpClient http,
            ApiConfig api)
        {
            _auth = auth;
            _player = player;
            _ui = ui;
            _http = http;
            _api = api;
        }

        public void Start()
        {
            if (!IsDebugEnv()) return;
            RegisterBuiltin();
            var go = new GameObject("[DebugConsole]");
            UnityEngine.Object.DontDestroyOnLoad(go);
            var hook = go.AddComponent<DebugConsoleHook>();
            hook.Bind(this);
            Log("DebugConsole ready. Toggle: F12 or ~");
        }

        public void Tick()
        {
            if (!IsDebugEnv()) return;
            if (Input.GetKeyDown(KeyCode.F12) || Input.GetKeyDown(KeyCode.BackQuote))
                _open = !_open;
        }

        public void Dispose() { }

        static bool IsDebugEnv() => Application.isEditor || Debug.isDebugBuild;

        public void OnGUI()
        {
            if (!_open || !IsDebugEnv()) return;
            EnsureStyles();
            _win = GUI.Window(0xD3B06, _win, DrawWindow, "HotUpdate DebugConsole  (F12/~)");
        }

        void EnsureStyles()
        {
            if (_btnStyle != null) return;
            _btnStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 40,
                padding = new RectOffset(12, 12, 6, 6)
            };
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _statusStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 20,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                padding = new RectOffset(14, 14, 12, 12),
                normal = { textColor = new Color(0.25f, 1f, 0.5f, 1f) }
            };
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16 };
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // ---- 当前 UI 状态 + Profile 名字 + 资源加载模式（常驻，分行大字）----
            var panel = _ui != null ? _ui.CurrentPanel.ToString() : "?";
            var asset = _ui?.CurrentUIAsset;
            var uiLine = string.IsNullOrEmpty(asset)
                ? $"界面: {panel}"
                : $"界面: {panel}   |   资源: {asset}";
            var nick = _player?.Profile?.nickname;
            if (string.IsNullOrEmpty(nick)) nick = "(未拉取)";
            var profileLine =
                $"昵称: {nick}\n" +
                $"体力: {_player?.Energy ?? 0}/{_player?.EnergyMax ?? 0}   |   金币: {_player?.Gold ?? 0}   |   地图: {_player?.UnlockedMap ?? 0}";
            var resLine = ResourceLoadMode.StatusText;
            var status = uiLine + "\n" + profileLine + "\n" + resLine;
            GUILayout.Box(status, _statusStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(110));

            GUILayout.Space(8);
            GUILayout.Label("热更命令：DebugCmd.Reg(\"name\", \"help\", () => {...})", _labelStyle);

            GUILayout.Space(4);
            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("dbg_input");
            var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 18, fixedHeight = 36 };
            _input = GUILayout.TextField(_input, fieldStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Run", _btnStyle, GUILayout.Width(90)))
                RunLine(_input);
            if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return
                && GUI.GetNameOfFocusedControl() == "dbg_input")
            {
                RunLine(_input);
                Event.current.Use();
            }
            GUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_lastResult))
                GUILayout.Label("-> " + _lastResult, _labelStyle);

            GUILayout.Space(6);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(360));
            string cat = null;
            foreach (var e in DebugCmd.All)
            {
                if (e.Category != cat)
                {
                    cat = e.Category;
                    GUILayout.Space(10);
                    GUILayout.Label("[" + cat + "]", _titleStyle);
                }
                if (GUILayout.Button(e.Name + "  —  " + e.Help, _btnStyle))
                    RunLine(e.Name);
            }
            GUILayout.EndScrollView();

            GUILayout.Space(6);
            GUILayout.Label("Log", _titleStyle);
            var sb = new StringBuilder();
            for (var i = Mathf.Max(0, _log.Count - 30); i < _log.Count; i++)
                sb.AppendLine(_log[i]);
            var areaStyle = new GUIStyle(GUI.skin.textArea) { fontSize = 15 };
            GUILayout.TextArea(sb.ToString(), areaStyle, GUILayout.Height(160));

            GUILayout.Space(4);
            if (GUILayout.Button("Close (F12 / ~)", _btnStyle, GUILayout.Height(42)))
                _open = false;

            GUILayout.EndVertical();
            GUI.DragWindow(new Rect(0, 0, 10000, 28));
        }

        void RunLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            Log("> " + line);
            DebugCmd.RunAsync(line, CancellationToken.None).ContinueWith(r =>
            {
                _lastResult = r.msg;
                Log(r.ok ? r.msg : "ERR " + r.msg);
            }).Forget();
        }

        void Log(string s)
        {
            var line = $"[{DateTime.Now:HH:mm:ss}] {s}";
            _log.Add(line);
            while (_log.Count > MaxLog) _log.RemoveAt(0);
            Debug.Log("[DebugConsole] " + s);
        }

        void RegisterBuiltin()
        {
            if (_registered) return;
            _registered = true;

            DebugCmd.Reg("help", "列出命令", () =>
            {
                foreach (var e in DebugCmd.All)
                    Log($"{e.Category}/{e.Name}: {e.Help}");
            }, "System");

            DebugCmd.Reg("ui", "打印当前 UI 面板 / 资源名", () =>
            {
                Log($"CurrentPanel={_ui.CurrentPanel}");
                Log($"CurrentUIAsset={_ui.CurrentUIAsset ?? "(null)"}");
            }, "UI");

            DebugCmd.Reg("toast", "测试 Toast", () =>
            {
                _ui.ShowToast("Toast " + DateTime.Now.ToLongTimeString());
            }, "UI");

            DebugCmd.RegAsync("dialog", "测试 Dialog", async (args, ct) =>
            {
                var msg = args.Length > 0 ? string.Join(" ", args) : "Dialog test";
                await _ui.ShowDialogAsync(msg, "Debug", "OK", ct);
            }, "UI");

            DebugCmd.Reg("token", "打印 access_token", () =>
            {
                Log("token=" + (_http.AccessToken ?? "(null)"));
                Log("loggedIn=" + _auth.IsLoggedIn);
            }, "Auth");

            DebugCmd.Reg("logout", "登出", () => { _auth.Logout(); Log("logged out"); }, "Auth");

            DebugCmd.Reg("energy", "打印体力/金币", () =>
            {
                Log($"energy={_player.Energy}/{_player.EnergyMax} gold={_player.Gold} map={_player.UnlockedMap}");
            }, "Player");

            DebugCmd.Reg("profile", "打印 profile（昵称/level 等）", () =>
            {
                var p = _player.Profile;
                if (p == null)
                {
                    Log("profile=(null)");
                    return;
                }
                Log($"nickname={p.nickname ?? "(null)"}");
                Log($"level={p.level} id={p.id ?? "(null)"} game_id={p.game_id ?? "(null)"}");
                Log($"mp_account_id={p.mp_account_id ?? "(null)"}");
                Log($"extra_json={p.extra_json ?? "(null)"}");
            }, "Player");

            DebugCmd.RegAsync("refresh_profile", "重新拉取 profile", async (args, ct) =>
            {
                await _player.RefreshProfileAsync(ct);
                var p = _player.Profile;
                Log($"refreshed nickname={p?.nickname ?? "(null)"} level={p?.level ?? 0}");
            }, "Player");

            DebugCmd.RegAsync("refill", "体力 cheat-refill", async (args, ct) =>
            {
                try
                {
                    var url = $"{_api.GameBaseUrl}/api/v1/user/energy/cheat-refill";
                    await _http.PostJsonAsync(url, "{}", auth: true, ct);
                    Log("cheat-refill ok");
                }
                catch (Exception e)
                {
                    Log("cheat-refill fail: " + e.Message);
                }
            }, "Player");

            DebugCmd.Reg("cfg", "打印 ApiConfig", () =>
            {
                Log($"mp={_api.MpBaseUrl}");
                Log($"game={_api.GameBaseUrl}");
                Log($"gameId={_api.GameId} appId={_api.AppId}");
            }, "Net");

            DebugCmd.Reg("levelcfg", "打印 1-1 关卡配置", () =>
            {
                var c = LevelConfigTable.Get(1, 1);
                Log($"map={c.MapId} lv={c.LevelId} {c.BoardWidth}x{c.BoardHeight} steps={c.MaxSteps} goal={c.Goal}:{c.GoalValue}");
                Log($"table init={LevelConfigTable.IsInitialized} count={LevelConfigTable.Count}");
            }, "Match3");

            DebugCmd.Reg("ping", "热更命令连通性", () =>
            {
                Log("pong " + DateTime.Now.ToString("O"));
            }, "General");
        }
    }

    public sealed class DebugConsoleHook : MonoBehaviour
    {
        DebugConsole _console;
        public void Bind(DebugConsole c) => _console = c;
        void OnGUI() => _console?.OnGUI();
    }
}
