using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using HotUpdate.Auth;
using HotUpdate.Network;
using HotUpdate.Gameplay;

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
        Func<int, int, UniTask> _onStartLevel;
        Action _onLogout;

        int _energy = 30, _energyMax = 30, _unlockedMap = 1, _clearedOnMap, _levelsPerMap = 10;
        long _gold;
        int _gameMapId = 1, _gameLevelId = 1, _gameMaxSteps = 20;

        int _homeMapId = 1;
        LevelProgressItem[] _homeLevels;
        int _nextLevelId = 1;

        // 非破坏性 tip（不整页清空）
        VisualElement _tipRoot;
        Label _tipLabel;
        CancellationTokenSource _tipCts;

        public UIService(IAuthService auth)
        {
            _auth = auth;
        }

        public void SetLoginHandler(Func<string, string, UniTask> handler) => _onLogin = handler;
        public void SetOfflineEnterHandler(Func<UniTask> handler) => _onOfflineEnter = handler;
        public void SetStartGameHandler(Func<UniTask> handler) => _onStartGame = handler;
        public void SetStartLevelHandler(Func<int, int, UniTask> handler) => _onStartLevel = handler;
        public void SetLogoutHandler(Action handler) => _onLogout = handler;

        public void SetHomeStatus(int energy, int energyMax, long gold, int unlockedMap, int clearedOnMap, int levelsPerMap, int nextLevelId = 1)
        {
            _energy = energy;
            _energyMax = energyMax;
            _gold = gold;
            _unlockedMap = unlockedMap;
            _clearedOnMap = clearedOnMap;
            _levelsPerMap = levelsPerMap > 0 ? levelsPerMap : 10;
            _nextLevelId = nextLevelId > 0 ? nextLevelId : 1;
        }

        public void SetHomeLevels(int mapId, LevelProgressItem[] levels)
        {
            _homeMapId = mapId > 0 ? mapId : 1;
            _homeLevels = levels;
        }

        public void SetGameStatus(int mapId, int levelId, int maxSteps)
        {
            _gameMapId = mapId;
            _gameLevelId = levelId;
            _gameMaxSteps = maxSteps;
        }

        public async UniTask ShowPanelAsync(UIPanel panel, CancellationToken ct = default)
        {
            EnsureDoc();
            _root.Clear();
            _tipRoot = null;
            _tipLabel = null;
            _tipCts?.Cancel();
            _tipCts = null;

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
            var label = new Label($"错误: {message}") { name = "UI_Error" };
            label.style.fontSize = 24;
            label.style.color = Color.red;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            label.style.flexGrow = 1;
            _root.Add(label);
            await UniTask.Delay(2000, cancellationToken: ct);
        }


        /// <summary>
        /// 进入关卡确认层（防误点）。确认后才回调 _onStartLevel。
        /// 不 Clear 整页；体力仍由通关上报接口扣除，无需新 API。
        /// </summary>
        void ShowEnterConfirm(int mapId, int levelId)
        {
            var old = _root.Q("Home_EnterConfirm");
            old?.RemoveFromHierarchy();

            var mask = new VisualElement { name = "Home_EnterConfirm" };
            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;
            mask.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            mask.style.justifyContent = Justify.Center;
            mask.style.alignItems = Align.Center;

            var card = new VisualElement { name = "EnterConfirm_Card" };
            card.style.width = 320;
            card.style.paddingTop = 24;
            card.style.paddingBottom = 20;
            card.style.paddingLeft = 20;
            card.style.paddingRight = 20;
            card.style.backgroundColor = new Color(0.16f, 0.18f, 0.24f, 1f);
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.alignItems = Align.Center;

            var msg = new Label($"是否进入第 {levelId} 关？\n（消耗 {GameRuleConfig.EnergyCostPerLevel} 点体力）") { name = "EnterConfirm_Msg" };
            msg.style.fontSize = 18;
            msg.style.color = Color.white;
            msg.style.unityTextAlign = TextAnchor.MiddleCenter;
            msg.style.whiteSpace = WhiteSpace.Normal;
            msg.style.marginBottom = 20;
            card.Add(msg);

            var row = new VisualElement { name = "EnterConfirm_Row" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;

            var cancel = new Button { text = "取消", name = "EnterConfirm_Cancel" };
            cancel.style.width = 110;
            cancel.style.height = 42;
            cancel.style.marginRight = 12;
            cancel.style.backgroundColor = new Color(0.35f, 0.35f, 0.4f, 1f);
            cancel.style.color = Color.white;
            cancel.clicked += () => mask.RemoveFromHierarchy();
            row.Add(cancel);

            var ok = new Button { text = "进入", name = "EnterConfirm_Ok" };
            ok.style.width = 110;
            ok.style.height = 42;
            ok.style.backgroundColor = new Color(0.2f, 0.65f, 0.35f, 1f);
            ok.style.color = Color.white;
            int m = mapId, lv = levelId;
            ok.clicked += () =>
            {
                mask.RemoveFromHierarchy();
                Debug.Log($"[UI] confirm enter map={m} level={lv}");
                if (_onStartLevel != null)
                    _onStartLevel(m, lv).Forget();
                else if (_onStartGame != null)
                    _onStartGame().Forget();
            };
            row.Add(ok);

            card.Add(row);
            mask.Add(card);
            _root.Add(mask);
            mask.BringToFront();
        }

        /// <summary>
        /// 不破坏当前界面的轻量 tip（用于关卡锁定等提示）。
        /// </summary>
        void ShowTip(string message, float seconds = 1.6f)
        {
            if (_tipLabel == null || _tipRoot == null)
            {
                Debug.Log("[UI Tip] " + message);
                return;
            }

            _tipCts?.Cancel();
            _tipCts = new CancellationTokenSource();
            var ct = _tipCts.Token;

            _tipLabel.text = message;
            _tipRoot.style.display = DisplayStyle.Flex;
            _tipRoot.style.opacity = 1f;

            UniTask.Void(async () =>
            {
                try
                {
                    await UniTask.Delay(TimeSpan.FromSeconds(seconds), cancellationToken: ct);
                    if (!ct.IsCancellationRequested && _tipRoot != null)
                        _tipRoot.style.display = DisplayStyle.None;
                }
                catch (OperationCanceledException) { }
            });
        }

        void EnsureDoc()
        {
            if (_doc != null) return;

            _doc = UnityEngine.Object.FindObjectOfType<UIDocument>();
            if (_doc == null)
            {
                var go = new GameObject("UIRoot");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _doc = go.AddComponent<UIDocument>();
            }

            if (_doc.panelSettings == null)
            {
                var ps = Resources.Load<PanelSettings>("UI/PanelSettings");
                if (ps != null)
                    _doc.panelSettings = ps;
                else
                    Debug.LogError("[UI] 缺少 PanelSettings，请放到 Resources/UI/PanelSettings.asset");
            }

            _root = new VisualElement { name = "UI_Root" };
            _root.style.flexGrow = 1;
            _root.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            _doc.rootVisualElement.Clear();
            _doc.rootVisualElement.Add(_root);
        }

        void BuildCheckUpdate()
        {
            var page = new VisualElement { name = "UI_CheckUpdate" };
            page.style.flexGrow = 1;
            var label = new Label("检查更新中...") { name = "CheckUpdate_Label" };
            StyleCenter(label);
            page.Add(label);
            _root.Add(page);
        }

        void BuildLogin()
        {
            var box = new VisualElement { name = "UI_Login" };
            box.style.flexGrow = 1;
            box.style.justifyContent = Justify.Center;
            box.style.alignItems = Align.Center;
            box.style.backgroundColor = new Color(0.12f, 0.12f, 0.18f, 1f);

            var card = new VisualElement { name = "Login_Card" };
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

            var title = new Label("登录") { name = "Login_Title" };
            title.style.fontSize = 36;
            title.style.color = Color.white;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 28;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            card.Add(title);

            var user = new TextField("账号") { name = "Login_User" };
            user.value = "test";
            user.style.marginBottom = 12;
            user.style.fontSize = 18;
            user.style.color = Color.white;
            StyleField(user);
            card.Add(user);

            var pass = new TextField("密码") { name = "Login_Pass" };
            pass.isPasswordField = true;
            pass.value = "123456";
            pass.style.marginBottom = 24;
            pass.style.fontSize = 18;
            pass.style.color = Color.white;
            StyleField(pass);
            card.Add(pass);

            var btn = new Button { text = "登录", name = "Login_BtnSubmit" };
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

            var offline = new Button { text = "离线进入（演示）", name = "Login_BtnOffline" };
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
            // 整体：竖版，上状态 → 中地图路径 → 底按钮
            var rootCol = new VisualElement { name = "UI_Home" };
            rootCol.style.flexGrow = 1;
            rootCol.style.flexDirection = FlexDirection.Column;
            rootCol.style.backgroundColor = new Color(0.18f, 0.55f, 0.28f, 1f);

            // ---- 顶部状态 ----
            var header = new VisualElement { name = "Home_Header" };
            header.style.paddingTop = 16;
            header.style.paddingBottom = 8;
            header.style.paddingLeft = 12;
            header.style.paddingRight = 12;

            var title = new Label($"地图 {_homeMapId}") { name = "Home_Title" };
            title.style.fontSize = 28;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            header.Add(title);

            var status = new Label(
                $"体力 {_energy}/{_energyMax}    金币 {_gold}\n" +
                $"已解锁地图 {_unlockedMap}    本图进度 {_clearedOnMap}/{_levelsPerMap}") { name = "Home_Status" };
            status.style.fontSize = 14;
            status.style.color = new Color(0.95f, 0.98f, 1f, 1f);
            status.style.unityTextAlign = TextAnchor.MiddleCenter;
            status.style.whiteSpace = WhiteSpace.Normal;
            status.style.marginTop = 4;
            header.Add(status);
            rootCol.Add(header);

            // ---- 中部：蛇形关卡路径（1 在下，10 在上）----
            var pathArea = new VisualElement { name = "Home_PathArea" };
            pathArea.style.flexGrow = 1;
            pathArea.style.position = Position.Relative;
            pathArea.style.minHeight = 520;
            pathArea.style.marginLeft = 8;
            pathArea.style.marginRight = 8;

            // level -> (x, y)  y=0 底部, y=1 顶部；左右交替蛇形
            var positions = new (float x, float y)[]
            {
                (0.28f, 0.04f), // 1 底部偏左
                (0.22f, 0.16f), // 2
                (0.48f, 0.26f), // 3 中间
                (0.72f, 0.36f), // 4 偏右
                (0.78f, 0.48f), // 5
                (0.55f, 0.58f), // 6
                (0.28f, 0.66f), // 7 偏左
                (0.35f, 0.76f), // 8
                (0.55f, 0.86f), // 9
                (0.48f, 0.96f), // 10 顶部
            };

            const float btnSize = 72f;
            var levels = _homeLevels;

            for (int i = 0; i < 10; i++)
            {
                int levelId = i + 1;
                int stars = GetStars(levelId, levels);
                bool unlocked = IsLevelUnlocked(levelId, levels);
                var (nx, ny) = positions[i];

                var btn = new Button { name = $"Home_Level_{levelId}" };
                btn.style.position = Position.Absolute;
                btn.style.width = btnSize;
                btn.style.height = btnSize;
                btn.style.left = Length.Percent(nx * 100f);
                btn.style.bottom = Length.Percent(ny * 100f);
                btn.style.marginLeft = -btnSize * 0.5f;
                btn.style.marginBottom = -btnSize * 0.5f;
                btn.style.borderTopLeftRadius = btnSize * 0.5f;
                btn.style.borderTopRightRadius = btnSize * 0.5f;
                btn.style.borderBottomLeftRadius = btnSize * 0.5f;
                btn.style.borderBottomRightRadius = btnSize * 0.5f;
                btn.style.unityTextAlign = TextAnchor.MiddleCenter;
                btn.style.paddingTop = 4;
                btn.style.paddingBottom = 4;

                if (unlocked)
                {
                    btn.style.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 1f);
                }
                else
                {
                    btn.style.backgroundColor = new Color(0.45f, 0.45f, 0.48f, 1f);
                }
                // 锁定关也允许点击，但只弹 tip，绝不跳转
                btn.SetEnabled(true);

                var num = new Label(levelId.ToString());
                num.style.fontSize = 22;
                num.style.unityFontStyleAndWeight = FontStyle.Bold;
                num.style.color = Color.white;
                num.style.unityTextAlign = TextAnchor.MiddleCenter;
                num.style.marginBottom = 0;
                btn.Add(num);

                var starLabel = new Label(BuildStarText(stars));
                starLabel.style.fontSize = 12;
                starLabel.style.color = new Color(1f, 0.88f, 0.2f, 1f);
                starLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                btn.Add(starLabel);

                if (!unlocked)
                {
                    var lockMark = new Label("🔒");
                    lockMark.style.fontSize = 11;
                    lockMark.style.color = new Color(0.9f, 0.9f, 0.9f, 1f);
                    lockMark.style.unityTextAlign = TextAnchor.MiddleCenter;
                    btn.Add(lockMark);
                }

                int capturedLevel = levelId;
                int capturedMap = _homeMapId;
                bool capturedUnlocked = unlocked;

                btn.clicked += () =>
                {
                    if (!capturedUnlocked)
                    {
                        // 不调用 ShowErrorAsync，避免整页被清、退不出
                        ShowTip("关卡未解锁，请先通关前一关");
                        return;
                    }

                    Debug.Log($"[UI] click level map={capturedMap} level={capturedLevel}");
                    // 防误点：先弹出确认层
                    ShowEnterConfirm(capturedMap, capturedLevel);
                };

                pathArea.Add(btn);
            }

            rootCol.Add(pathArea);

            // ---- 底部操作栏（贴底）----
            var bottom = new VisualElement { name = "Home_BottomBar" };
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.justifyContent = Justify.Center;
            bottom.style.alignItems = Align.Center;
            bottom.style.paddingTop = 10;
            bottom.style.paddingBottom = 18;
            bottom.style.paddingLeft = 12;
            bottom.style.paddingRight = 12;
            bottom.style.backgroundColor = new Color(0.1f, 0.28f, 0.16f, 0.85f);

            var nextBtn = new Button { text = $"第{_nextLevelId}关", name = "Home_BtnNext" };
            nextBtn.style.width = 180;
            nextBtn.style.height = 48;
            nextBtn.style.marginRight = 12;
            nextBtn.style.backgroundColor = new Color(0.18f, 0.7f, 0.38f, 1f);
            nextBtn.style.color = Color.white;
            nextBtn.style.fontSize = 17;
            nextBtn.style.borderTopLeftRadius = 8;
            nextBtn.style.borderTopRightRadius = 8;
            nextBtn.style.borderBottomLeftRadius = 8;
            nextBtn.style.borderBottomRightRadius = 8;
            nextBtn.clicked += () =>
            {
                // 与点圆圈一致：先确认再进
                ShowEnterConfirm(_homeMapId, _nextLevelId);
            };
            bottom.Add(nextBtn);

            var logout = new Button { text = "退出登录", name = "Home_BtnLogout" };
            logout.style.width = 120;
            logout.style.height = 48;
            logout.style.backgroundColor = new Color(0.5f, 0.28f, 0.28f, 1f);
            logout.style.color = Color.white;
            logout.style.fontSize = 15;
            logout.style.borderTopLeftRadius = 8;
            logout.style.borderTopRightRadius = 8;
            logout.style.borderBottomLeftRadius = 8;
            logout.style.borderBottomRightRadius = 8;
            logout.clicked += () => _onLogout?.Invoke();
            bottom.Add(logout);

            rootCol.Add(bottom);

            // ---- 非破坏 tip 浮层 ----
            _tipRoot = new VisualElement { name = "Home_Tip" };
            _tipRoot.style.position = Position.Absolute;
            _tipRoot.style.left = Length.Percent(10);
            _tipRoot.style.right = Length.Percent(10);
            _tipRoot.style.bottom = 80;
            _tipRoot.style.paddingTop = 12;
            _tipRoot.style.paddingBottom = 12;
            _tipRoot.style.paddingLeft = 16;
            _tipRoot.style.paddingRight = 16;
            _tipRoot.style.backgroundColor = new Color(0.08f, 0.08f, 0.1f, 0.92f);
            _tipRoot.style.borderTopLeftRadius = 10;
            _tipRoot.style.borderTopRightRadius = 10;
            _tipRoot.style.borderBottomLeftRadius = 10;
            _tipRoot.style.borderBottomRightRadius = 10;
            _tipRoot.style.display = DisplayStyle.None;
            _tipRoot.style.alignItems = Align.Center;

            _tipLabel = new Label("") { name = "Home_TipLabel" };
            _tipLabel.style.fontSize = 16;
            _tipLabel.style.color = Color.white;
            _tipLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tipLabel.style.whiteSpace = WhiteSpace.Normal;
            _tipRoot.Add(_tipLabel);

            rootCol.Add(_tipRoot);
            _tipRoot.BringToFront();

            _root.Add(rootCol);
        }

        static int GetStars(int levelId, LevelProgressItem[] levels)
        {
            if (levels == null) return 0;
            foreach (var item in levels)
            {
                if (item != null && item.level_id == levelId)
                    return Mathf.Clamp(item.stars, 0, 3);
            }
            return 0;
        }

        static string BuildStarText(int stars)
        {
            stars = Mathf.Clamp(stars, 0, 3);
            return new string('★', stars) + new string('☆', 3 - stars);
        }

        /// <summary>
        /// 第 1 关永远可打；之后关卡需前一关 stars ≥ 1。
        /// </summary>
        static bool IsLevelUnlocked(int levelId, LevelProgressItem[] levels)
        {
            if (levelId <= 1) return true;
            if (levels == null) return false;

            int prevStars = 0;
            foreach (var item in levels)
            {
                if (item != null && item.level_id == levelId - 1)
                {
                    prevStars = item.stars;
                    break;
                }
            }
            return prevStars >= 1;
        }

        void BuildGame()
        {
            var page = new VisualElement { name = "UI_Game" };
            page.style.flexGrow = 1;
            var label = new Label(
                $"对局中\n地图 {_gameMapId}  关卡 {_gameLevelId}\n最大步数 {_gameMaxSteps}\n\n（Mock Match3，结束后自动上报通关）") { name = "Game_StatusLabel" };
            StyleCenter(label);
            label.style.whiteSpace = WhiteSpace.Normal;
            page.Add(label);
            _root.Add(page);
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
