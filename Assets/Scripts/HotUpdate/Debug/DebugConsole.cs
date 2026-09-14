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
        Rect _win = new Rect(24, 48, 640, 720);

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
                fontSize = 16,
                alignment = TextAnchor.MiddleLeft,
                fixedHeight = 32,
                padding = new RectOffset(10, 10, 4, 4)
            };
            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _statusStyle = new GUIStyle(GUI.skin.box)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(12, 12, 8, 8),
                normal = { textColor = new Color(0.2f, 1f, 0.45f, 1f) }
            };
            _labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 15 };
        }

        void DrawWindow(int id)
        {
            GUILayout.BeginVertical();

            // ---- 当前 UI 状态 + 资源加载模式（常驻）----
            var panel = _ui != null ? _ui.CurrentPanel.ToString() : "?";
            var asset = _ui?.CurrentUIAsset;
            var uiLine = string.IsNullOrEmpty(asset)
                ? $"当前界面: {panel}"
                : $"当前界面: {panel}    资源: {asset}";
            var resLine = ResourceLoadMode.StatusText;
            var status = uiLine + "\n" + resLine;
            GUILayout.Box(status, _statusStyle, GUILayout.ExpandWidth(true), GUILayout.MinHeight(56));

            GUILayout.Space(4);
            GUILayout.Label("热更临时函数：DebugCmd.Reg(\"name\", \"help\", () => {...})", _labelStyle);

            GUILayout.BeginHorizontal();
            GUI.SetNextControlName("dbg_input");
            var fieldStyle = new GUIStyle(GUI.skin.textField) { fontSize = 16, fixedHeight = 30 };
            _input = GUILayout.TextField(_input, fieldStyle, GUILayout.ExpandWidth(true));
            if (GUILayout.Button("Run", _btnStyle, GUILayout.Width(72)))
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

            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(340));
            string cat = null;
            foreach (var e in DebugCmd.All)
            {
                if (e.Category != cat)
                {
                    cat = e.Category;
                    GUILayout.Space(8);
                    GUILayout.Label("[" + cat + "]", _titleStyle);
                }
                if (GUILayout.Button(e.Name + "  —  " + e.Help, _btnStyle))
                    RunLine(e.Name);
            }
            GUILayout.EndScrollView();

            GUILayout.Label("Log", _titleStyle);
            var sb = new StringBuilder();
            for (var i = Mathf.Max(0, _log.Count - 30); i < _log.Count; i++)
                sb.AppendLine(_log[i]);
            var areaStyle = new GUIStyle(GUI.skin.textArea) { fontSize = 14 };
            GUILayout.TextArea(sb.ToString(), areaStyle, GUILayout.Height(150));

            if (GUILayout.Button("Close (F12 / ~)", _btnStyle, GUILayout.Height(36)))
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
