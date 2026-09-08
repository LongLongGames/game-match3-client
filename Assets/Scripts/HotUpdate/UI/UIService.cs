using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using HotUpdate.Auth;

namespace HotUpdate.UI
{
    /// <summary>
    /// UIToolkit 版 UI 服务。
    /// 不依赖 IAppFlow，避免与 AppFlowController 循环依赖。
    /// 跳转由外部通过 Handler 注入。
    /// </summary>
    public class UIService : IUIService
    {
        readonly IAuthService _auth;

        UIDocument _doc;
        VisualElement _root;

        Func<string, string, UniTask> _onLogin;
        Func<UniTask> _onOfflineEnter;
        Func<UniTask> _onStartGame;
        Action _onLogout;

        public UIService(IAuthService auth)
        {
            _auth = auth;
        }

        public void SetLoginHandler(Func<string, string, UniTask> handler) => _onLogin = handler;
        public void SetOfflineEnterHandler(Func<UniTask> handler) => _onOfflineEnter = handler;
        public void SetStartGameHandler(Func<UniTask> handler) => _onStartGame = handler;
        public void SetLogoutHandler(Action handler) => _onLogout = handler;

        public async UniTask ShowPanelAsync(UIPanel panel, CancellationToken ct = default)
        {
            EnsureDoc();
            _root.Clear();

            switch (panel)
            {
                case UIPanel.CheckUpdate:
                    BuildCheckUpdate();
                    break;
                case UIPanel.Login:
                    BuildLogin();
                    break;
                case UIPanel.Home:
                    BuildHome();
                    break;
                case UIPanel.Game:
                    BuildGame();
                    break;
            }

            await UniTask.Yield(ct);
        }

        public async UniTask ShowErrorAsync(string message, CancellationToken ct = default)
        {
            EnsureDoc();
            _root.Clear();
            var label = new Label($"错误: {message}");
            label.style.fontSize = 24;
            label.style.color = Color.red;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.flexGrow = 1;
            _root.Add(label);
            await UniTask.Delay(2000, cancellationToken: ct);
        }

        void EnsureDoc()
        {
            if (_doc != null) return;

            // 1) 场景里已有就复用
            _doc = UnityEngine.Object.FindObjectOfType<UIDocument>();
            if (_doc == null)
            {
                var go = new GameObject("UIRoot");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _doc = go.AddComponent<UIDocument>();
            }

            // 2) 用工程里的 PanelSettings，不要运行时 CreateInstance
            if (_doc.panelSettings == null)
            {
                var ps = Resources.Load<PanelSettings>("UI/PanelSettings");
                // 或：放在 Resources/UI/PanelSettings.asset
                if (ps != null)
                    _doc.panelSettings = ps;
                else
                    Debug.LogError("[UI] 缺少 PanelSettings，请放到 Resources/UI/PanelSettings.asset");
            }

            _root = new VisualElement();
            _root.style.flexGrow = 1;
            _root.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            _doc.rootVisualElement.Clear();
            _doc.rootVisualElement.Add(_root);
        }

        void BuildCheckUpdate()
        {
            var label = new Label("检查更新中...");
            StyleCenter(label);
            _root.Add(label);
        }

        void BuildLogin()
        {
            var box = new VisualElement();
            box.style.flexGrow = 1;
            box.style.justifyContent = Justify.Center;
            box.style.alignItems = Align.Center;
            box.style.backgroundColor = new Color(0.12f, 0.12f, 0.18f, 1f);

            var card = new VisualElement();
            card.style.width = 420;
            card.style.paddingTop = 32;
            card.style.paddingBottom = 32;
            card.style.paddingLeft = 28;
            card.style.paddingRight = 28;
            card.style.backgroundColor = new Color(0.18f, 0.18f, 0.26f, 1f);
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;

            var title = new Label("登录");
            title.style.fontSize = 36;
            title.style.color = Color.white;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 28;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(title);

            var user = new TextField("账号");
            user.value = "test";
            user.style.marginBottom = 12;
            user.style.fontSize = 18;
            user.style.color = Color.white;
            StyleField(user);
            card.Add(user);

            var pass = new TextField("密码");
            pass.isPasswordField = true;
            pass.value = "123456";
            pass.style.marginBottom = 24;
            pass.style.fontSize = 18;
            pass.style.color = Color.white;
            StyleField(pass);
            card.Add(pass);

            var btn = new Button { text = "登录" };
            btn.style.height = 48;
            btn.style.fontSize = 20;
            btn.style.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 1f);
            btn.style.color = Color.white;
            btn.style.marginBottom = 12;
            btn.clicked += async () =>
            {
                if (_onLogin == null) return;
                btn.SetEnabled(false);
                try { await _onLogin(user.value, pass.value); }
                finally { btn.SetEnabled(true); }
            };
            card.Add(btn);

            var offline = new Button { text = "离线进入（演示）" };
            offline.style.height = 44;
            offline.style.fontSize = 16;
            offline.style.backgroundColor = new Color(0.28f, 0.28f, 0.36f, 1f);
            offline.style.color = Color.white;
            offline.clicked += async () =>
            {
                if (_onOfflineEnter != null)
                    await _onOfflineEnter();
            };
            card.Add(offline);

            box.Add(card);
            _root.Add(box);
        }

        static void StyleField(TextField field)
        {
            field.style.height = 40;
            // 输入框背景（不同 Unity 版本选择器略有差异，没有也不影响）
            var input = field.Q("unity-text-input");
            if (input != null)
            {
                input.style.backgroundColor = new Color(0.1f, 0.1f, 0.14f, 1f);
                input.style.color = Color.white;
                input.style.paddingLeft = 8;
                input.style.paddingRight = 8;
            }
        }

        void BuildHome()
        {
            var box = new VisualElement();
            StyleCenter(box);

            var title = new Label("主页 - Match3");
            title.style.fontSize = 36;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            box.Add(title);

            var start = new Button { text = "开始游戏" };
            start.style.marginTop = 24;
            start.style.width = 240;
            start.style.height = 56;
            start.clicked += async () =>
            {
                if (_onStartGame != null)
                    await _onStartGame();
            };
            box.Add(start);

            var logout = new Button { text = "退出登录" };
            logout.style.marginTop = 12;
            logout.clicked += () => _onLogout?.Invoke();
            box.Add(logout);

            _root.Add(box);
        }

        void BuildGame()
        {
            var label = new Label("游戏中...\n（Match3 逻辑运行中，结束后自动提交分数）");
            StyleCenter(label);
            label.style.whiteSpace = WhiteSpace.Normal;
            _root.Add(label);
        }

        static void StyleCenter(VisualElement e)
        {
            e.style.flexGrow = 1;
            e.style.justifyContent = Justify.Center;
            e.style.alignItems = Align.Center;
            e.style.color = Color.white;
        }
    }
}
