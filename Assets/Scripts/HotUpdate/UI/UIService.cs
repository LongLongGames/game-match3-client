using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;
using HotUpdate.Auth;
using HotUpdate.Network;
using HotUpdate.Gameplay;
using HotUpdate.Services;

namespace HotUpdate.UI
{
    /// <summary>
    /// UIToolkit 版 UI 服务。
    /// 不依赖 IAppFlow，避免与 AppFlowController 循环依赖。
    /// 跳转由外部通过 Handler 注入。
    /// </summary>
    public class UIService : IUIService, IMatch3Hud
    {
        readonly IAuthService _auth;
        readonly IPlayerService _player;
        readonly IAudioManager _audio;

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
        int _focusLevelId; // 回主页时高亮并弹确认的关卡

        // 非破坏性 tip（不整页清空）
        VisualElement _tipRoot;
        Label _tipLabel;
        CancellationTokenSource _tipCts;

        string _currentUIAsset; // 当前 AB 加载的 UI 资源名（用于卸载）

        public UIPanel CurrentPanel { get; private set; } = UIPanel.None;
        public string CurrentUIAsset => _currentUIAsset;

        public UIService(IAuthService auth, IPlayerService player, IAudioManager audio)
        {
            _auth = auth;
            _player = player;
            _audio = audio;
        }

        /// <summary>非 Game 局内按钮统一挂 UI 点击音效。</summary>
        void WireClickSfx(Button btn)
        {
            if (btn == null) return;
            btn.clicked += () => _audio?.PlayUiClick();
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
            CurrentPanel = panel;
            Debug.Log($"[UI] ShowPanel → {panel} asset={_currentUIAsset ?? "(pending)"}");
            _root.Clear();
            _tipRoot = null;
            _tipLabel = null;
            _tipCts?.Cancel();
            _tipCts = null;
            _boardChrome = null;
            _boardHudLabel = null;

            if (!string.IsNullOrEmpty(_currentUIAsset))
            {
                UIPanelLoader.Unload(_currentUIAsset);
                _currentUIAsset = null;
            }

            // Game：UI 必须透明且不挡点击，否则 Sprite 棋盘被盖住
            // 其它页：不透明背景盖住 3D/2D 场景
            if (panel == UIPanel.Game)
            {
                _root.style.backgroundColor = Color.clear;
                _root.pickingMode = PickingMode.Ignore;
            }
            else
            {
                _root.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
                _root.pickingMode = PickingMode.Position;
            }

            switch (panel)
            {
                case UIPanel.CheckUpdate:
                    _currentUIAsset = "UI_CheckUpdate";
                    {
                        var page = await UIPanelLoader.LoadAsync(_currentUIAsset, _root, ct);
                        if (page != null) BindCheckUpdate(page);
                    }
                    break;
                case UIPanel.Login:
                    _currentUIAsset = "UI_Login";
                    {
                        var page = await UIPanelLoader.LoadAsync(_currentUIAsset, _root, ct);
                        if (page != null) BindLogin(page);
                    }
                    break;
                case UIPanel.Home:
                    _currentUIAsset = "UI_Home";
                    {
                        var page = await UIPanelLoader.LoadAsync(_currentUIAsset, _root, ct);
                        if (page != null) BindHome(page);
                    }
                    break;
                case UIPanel.Game:
                    _currentUIAsset = "UI_Game";
                    {
                        var page = await UIPanelLoader.LoadAsync(_currentUIAsset, _root, ct);
                        if (page != null) BindGame(page);
                    }
                    break;
            }

            Debug.Log($"[UI] ShowPanel done → {CurrentPanel} asset={_currentUIAsset ?? "(null)"}");
            await UniTask.Yield(ct);
        }

        public UniTask ShowErrorAsync(string message, CancellationToken ct = default)
        {
            return ShowDialogAsync(message, title: "提示", okText: "确定", ct: ct);
        }

        public void ShowToast(string message, float seconds = 2f)
        {
            ShowTip(message, seconds);
        }

        public async UniTask ShowDialogAsync(string message, string title = null, string okText = "确定", CancellationToken ct = default)
        {
            EnsureDoc();
            // 去掉旧弹窗
            _root.Q("UI_Dialog")?.RemoveFromHierarchy();

            var tcs = new UniTaskCompletionSource();
            using var reg = ct.Register(() => tcs.TrySetCanceled());

            var mask = new VisualElement { name = "UI_Dialog" };
            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;
            mask.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            mask.style.justifyContent = Justify.Center;
            mask.style.alignItems = Align.Center;
            mask.pickingMode = PickingMode.Position;

            var card = new VisualElement { name = "UI_Dialog_Card" };
            card.style.width = 340;
            card.style.maxWidth = Length.Percent(90);
            card.style.paddingTop = 20;
            card.style.paddingBottom = 18;
            card.style.paddingLeft = 20;
            card.style.paddingRight = 20;
            card.style.backgroundColor = new Color(0.16f, 0.18f, 0.24f, 1f);
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.alignItems = Align.Stretch;

            if (!string.IsNullOrEmpty(title))
            {
                var titleLabel = new Label(title) { name = "UI_Dialog_Title" };
                titleLabel.style.fontSize = 20;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.color = Color.white;
                titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                titleLabel.style.marginBottom = 12;
                card.Add(titleLabel);
            }

            var body = new Label(message ?? "") { name = "UI_Dialog_Body" };
            body.style.fontSize = 16;
            body.style.color = new Color(0.92f, 0.93f, 0.96f, 1f);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            body.style.marginBottom = 18;
            card.Add(body);

            var ok = new Button { text = string.IsNullOrEmpty(okText) ? "确定" : okText, name = "UI_Dialog_Ok" };
            ok.style.height = 42;
            ok.style.fontSize = 16;
            ok.style.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 1f);
            ok.style.color = Color.white;
            ok.style.borderTopLeftRadius = 8;
            ok.style.borderTopRightRadius = 8;
            ok.style.borderBottomLeftRadius = 8;
            ok.style.borderBottomRightRadius = 8;
            WireClickSfx(ok);
            ok.clicked += () =>
            {
                mask.RemoveFromHierarchy();
                tcs.TrySetResult();
            };
            card.Add(ok);

            mask.Add(card);
            _root.Add(mask);

            try
            {
                await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                mask.RemoveFromHierarchy();
                throw;
            }
        }



        /// <summary>是/否确认框。返回 true = 点「是」。</summary>
        public async UniTask<bool> ShowConfirmAsync(
            string message,
            string title = null,
            string yesText = "是",
            string noText = "否",
            CancellationToken ct = default)
        {
            EnsureDoc();
            _root.Q("UI_Dialog")?.RemoveFromHierarchy();

            var tcs = new UniTaskCompletionSource<bool>();
            using var reg = ct.Register(() => tcs.TrySetCanceled());

            var mask = new VisualElement { name = "UI_Dialog" };
            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;
            mask.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            mask.style.justifyContent = Justify.Center;
            mask.style.alignItems = Align.Center;
            mask.pickingMode = PickingMode.Position;

            var card = new VisualElement { name = "UI_Dialog_Card" };
            card.style.width = 340;
            card.style.maxWidth = Length.Percent(90);
            card.style.paddingTop = 20;
            card.style.paddingBottom = 18;
            card.style.paddingLeft = 20;
            card.style.paddingRight = 20;
            card.style.backgroundColor = new Color(0.16f, 0.18f, 0.24f, 1f);
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.alignItems = Align.Stretch;

            if (!string.IsNullOrEmpty(title))
            {
                var titleLabel = new Label(title) { name = "UI_Dialog_Title" };
                titleLabel.style.fontSize = 20;
                titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
                titleLabel.style.color = Color.white;
                titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
                titleLabel.style.marginBottom = 12;
                card.Add(titleLabel);
            }

            var body = new Label(message ?? "") { name = "UI_Dialog_Body" };
            body.style.fontSize = 16;
            body.style.color = new Color(0.9f, 0.92f, 0.95f, 1f);
            body.style.whiteSpace = WhiteSpace.Normal;
            body.style.unityTextAlign = TextAnchor.MiddleCenter;
            body.style.marginBottom = 18;
            card.Add(body);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;

            var noBtn = new Button { text = string.IsNullOrEmpty(noText) ? "否" : noText, name = "UI_Dialog_No" };
            noBtn.style.height = 42;
            noBtn.style.width = 120;
            noBtn.style.fontSize = 16;
            noBtn.style.marginRight = 12;
            noBtn.style.backgroundColor = new Color(0.4f, 0.42f, 0.48f, 1f);
            noBtn.style.color = Color.white;
            noBtn.style.borderTopLeftRadius = 8;
            noBtn.style.borderTopRightRadius = 8;
            noBtn.style.borderBottomLeftRadius = 8;
            noBtn.style.borderBottomRightRadius = 8;
            WireClickSfx(noBtn);
            noBtn.clicked += () =>
            {
                mask.RemoveFromHierarchy();
                tcs.TrySetResult(false);
            };
            row.Add(noBtn);

            var yesBtn = new Button { text = string.IsNullOrEmpty(yesText) ? "是" : yesText, name = "UI_Dialog_Yes" };
            yesBtn.style.height = 42;
            yesBtn.style.width = 120;
            yesBtn.style.fontSize = 16;
            yesBtn.style.backgroundColor = new Color(0.85f, 0.35f, 0.35f, 1f);
            yesBtn.style.color = Color.white;
            yesBtn.style.borderTopLeftRadius = 8;
            yesBtn.style.borderTopRightRadius = 8;
            yesBtn.style.borderBottomLeftRadius = 8;
            yesBtn.style.borderBottomRightRadius = 8;
            WireClickSfx(yesBtn);
            yesBtn.clicked += () =>
            {
                mask.RemoveFromHierarchy();
                tcs.TrySetResult(true);
            };
            row.Add(yesBtn);

            card.Add(row);
            mask.Add(card);
            _root.Add(mask);

            try
            {
                return await tcs.Task;
            }
            catch (OperationCanceledException)
            {
                mask.RemoveFromHierarchy();
                throw;
            }
        }

        /// <summary>
        /// 主页设置弹窗：昵称 / 玩家ID / BGM·音效开关 / 联系客服 / 隐私协议 / 客户端版本。
        /// </summary>
        void ShowSettingsPopup()
        {
            EnsureDoc();
            _root.Q("UI_Settings")?.RemoveFromHierarchy();

            var mask = new VisualElement { name = "UI_Settings" };
            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;
            mask.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            mask.style.justifyContent = Justify.Center;
            mask.style.alignItems = Align.Center;
            mask.pickingMode = PickingMode.Position;

            var card = new VisualElement { name = "UI_Settings_Card" };
            card.style.width = 360;
            card.style.maxWidth = Length.Percent(92);
            card.style.paddingTop = 20;
            card.style.paddingBottom = 18;
            card.style.paddingLeft = 20;
            card.style.paddingRight = 20;
            card.style.backgroundColor = new Color(0.16f, 0.18f, 0.24f, 1f);
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.alignItems = Align.Stretch;

            // 标题
            var titleLabel = new Label("设置") { name = "UI_Settings_Title" };
            titleLabel.style.fontSize = 20;
            titleLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            titleLabel.style.color = Color.white;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            titleLabel.style.marginBottom = 16;
            card.Add(titleLabel);

            var profile = _player?.Profile;
            string nick = string.IsNullOrEmpty(profile?.nickname) ? "Player" : profile.nickname;
            string playerId = string.IsNullOrEmpty(profile?.id)
                ? (string.IsNullOrEmpty(profile?.mp_account_id) ? "—" : profile.mp_account_id)
                : profile.id;

            // 昵称行：昵称 + 修改按钮
            var nickRow = MakeSettingsRow();
            var nickLabel = new Label($"昵称：{nick}");
            StyleSettingsLabel(nickLabel);
            nickLabel.style.flexGrow = 1;
            nickRow.Add(nickLabel);
            var btnRename = new Button { text = "修改", name = "UI_Settings_BtnRename" };
            StyleSmallButton(btnRename, new Color(0.25f, 0.55f, 0.95f, 1f));
            WireClickSfx(btnRename);
            btnRename.clicked += () =>
            {
                ShowRenameNicknameDialog(nickLabel);
            };
            nickRow.Add(btnRename);
            card.Add(nickRow);

            // 玩家ID
            var idRow = MakeSettingsRow();
            var idLabel = new Label($"玩家ID：{playerId}");
            StyleSettingsLabel(idLabel);
            idRow.Add(idLabel);
            card.Add(idRow);

            // 背景音乐 Toggle → AudioManager
            var bgmToggle = new Toggle("背景音乐") { name = "UI_Settings_Bgm", value = _audio == null || _audio.BgmEnabled };
            StyleSettingsToggle(bgmToggle);
            bgmToggle.RegisterValueChangedCallback(evt =>
            {
                _audio?.SetBgmEnabled(evt.newValue);
            });
            card.Add(bgmToggle);

            // 音效 Toggle → AudioManager
            var sfxToggle = new Toggle("音效") { name = "UI_Settings_Sfx", value = _audio == null || _audio.SfxEnabled };
            StyleSettingsToggle(sfxToggle);
            sfxToggle.RegisterValueChangedCallback(evt =>
            {
                _audio?.SetSfxEnabled(evt.newValue);
            });
            card.Add(sfxToggle);

            // 联系客服
            var btnCs = new Button { text = "联系客服", name = "UI_Settings_BtnCs" };
            StyleFullWidthButton(btnCs, new Color(0.22f, 0.48f, 0.72f, 1f));
            WireClickSfx(btnCs);
            btnCs.clicked += () =>
            {
                _ = ShowDialogAsync("如有问题请联系客服邮箱：support@example.com\n或通过游戏内反馈渠道提交。", title: "联系客服");
            };
            card.Add(btnCs);

            // 隐私协议
            var btnPrivacy = new Button { text = "隐私协议", name = "UI_Settings_BtnPrivacy" };
            StyleFullWidthButton(btnPrivacy, new Color(0.22f, 0.48f, 0.72f, 1f));
            WireClickSfx(btnPrivacy);
            btnPrivacy.clicked += () =>
            {
                _ = ShowDialogAsync(
                    "我们重视您的隐私。本游戏会收集必要的账号与设备信息用于登录、存档与反作弊，不会向第三方出售个人数据。详细条款请以正式发布版本为准。",
                    title: "隐私协议");
            };
            card.Add(btnPrivacy);

            // 客户端版本
            var ver = Application.version;
            if (string.IsNullOrEmpty(ver)) ver = "0.0.0";
            var verLabel = new Label($"客户端版本：{ver}") { name = "UI_Settings_Version" };
            verLabel.style.fontSize = 13;
            verLabel.style.color = new Color(0.7f, 0.72f, 0.78f, 1f);
            verLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            verLabel.style.marginTop = 10;
            verLabel.style.marginBottom = 12;
            card.Add(verLabel);

            // 关闭
            var btnClose = new Button { text = "关闭", name = "UI_Settings_BtnClose" };
            StyleFullWidthButton(btnClose, new Color(0.35f, 0.38f, 0.45f, 1f));
            WireClickSfx(btnClose);
            btnClose.clicked += () => mask.RemoveFromHierarchy();
            card.Add(btnClose);

            // 点击遮罩关闭
            mask.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == mask)
                    mask.RemoveFromHierarchy();
            });

            mask.Add(card);
            _root.Add(mask);
            mask.BringToFront();
        }

        /// <summary>改名弹窗：输入新昵称 → PUT /api/v1/user/profile</summary>
        void ShowRenameNicknameDialog(Label nickLabel)
        {
            EnsureDoc();
            _root.Q("UI_Rename")?.RemoveFromHierarchy();

            var current = _player?.Profile?.nickname ?? "Player";

            var mask = new VisualElement { name = "UI_Rename" };
            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;
            mask.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            mask.style.justifyContent = Justify.Center;
            mask.style.alignItems = Align.Center;
            mask.pickingMode = PickingMode.Position;

            var card = new VisualElement { name = "UI_Rename_Card" };
            card.style.width = 340;
            card.style.maxWidth = Length.Percent(90);
            card.style.paddingTop = 20;
            card.style.paddingBottom = 18;
            card.style.paddingLeft = 20;
            card.style.paddingRight = 20;
            card.style.backgroundColor = new Color(0.16f, 0.18f, 0.24f, 1f);
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;

            var title = new Label("修改昵称");
            title.style.fontSize = 20;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.unityTextAlign = TextAnchor.MiddleCenter;
            title.style.marginBottom = 14;
            card.Add(title);

            var field = new TextField { name = "UI_Rename_Input", value = current };
            field.style.marginBottom = 14;
            field.style.height = 36;
            field.style.fontSize = 16;
            card.Add(field);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.justifyContent = Justify.Center;

            var cancel = new Button { text = "取消" };
            cancel.style.width = 120;
            cancel.style.height = 42;
            cancel.style.marginRight = 12;
            cancel.style.fontSize = 16;
            cancel.style.backgroundColor = new Color(0.4f, 0.42f, 0.48f, 1f);
            cancel.style.color = Color.white;
            cancel.style.borderTopLeftRadius = 8;
            cancel.style.borderTopRightRadius = 8;
            cancel.style.borderBottomLeftRadius = 8;
            cancel.style.borderBottomRightRadius = 8;
            WireClickSfx(cancel);
            cancel.clicked += () => mask.RemoveFromHierarchy();
            row.Add(cancel);

            var ok = new Button { text = "确定" };
            ok.style.width = 120;
            ok.style.height = 42;
            ok.style.fontSize = 16;
            ok.style.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 1f);
            ok.style.color = Color.white;
            ok.style.borderTopLeftRadius = 8;
            ok.style.borderTopRightRadius = 8;
            ok.style.borderBottomLeftRadius = 8;
            ok.style.borderBottomRightRadius = 8;
            WireClickSfx(ok);
            ok.clicked += async () =>
            {
                var name = field.value?.Trim() ?? "";
                if (string.IsNullOrEmpty(name))
                {
                    ShowToast("昵称不能为空");
                    return;
                }
                if (name == current)
                {
                    mask.RemoveFromHierarchy();
                    return;
                }

                ok.SetEnabled(false);
                cancel.SetEnabled(false);
                try
                {
                    if (_player == null)
                    {
                        ShowToast("玩家服务未就绪");
                        return;
                    }
                    var (success, err) = await _player.UpdateNicknameAsync(name);
                    if (success)
                    {
                        var n = _player.Profile?.nickname ?? name;
                        if (nickLabel != null)
                            nickLabel.text = $"昵称：{n}";
                        mask.RemoveFromHierarchy();
                        ShowToast("昵称已更新");
                    }
                    else
                    {
                        ShowToast(string.IsNullOrEmpty(err) ? "修改失败" : err);
                    }
                }
                finally
                {
                    ok.SetEnabled(true);
                    cancel.SetEnabled(true);
                }
            };
            row.Add(ok);

            card.Add(row);
            mask.Add(card);
            _root.Add(mask);
            mask.BringToFront();
            field.Focus();
        }

        static VisualElement MakeSettingsRow()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 10;
            row.style.minHeight = 36;
            return row;
        }

        static void StyleSettingsLabel(Label label)
        {
            label.style.fontSize = 15;
            label.style.color = new Color(0.92f, 0.93f, 0.96f, 1f);
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
        }

        static void StyleSettingsToggle(Toggle toggle)
        {
            toggle.style.marginBottom = 10;
            toggle.style.fontSize = 15;
            toggle.style.color = new Color(0.92f, 0.93f, 0.96f, 1f);
            toggle.style.height = 36;
        }

        static void StyleSmallButton(Button btn, Color bg)
        {
            btn.style.width = 72;
            btn.style.height = 32;
            btn.style.fontSize = 14;
            btn.style.backgroundColor = bg;
            btn.style.color = Color.white;
            btn.style.borderTopLeftRadius = 6;
            btn.style.borderTopRightRadius = 6;
            btn.style.borderBottomLeftRadius = 6;
            btn.style.borderBottomRightRadius = 6;
            btn.style.marginLeft = 8;
        }

        static void StyleFullWidthButton(Button btn, Color bg)
        {
            btn.style.height = 40;
            btn.style.fontSize = 15;
            btn.style.backgroundColor = bg;
            btn.style.color = Color.white;
            btn.style.borderTopLeftRadius = 8;
            btn.style.borderTopRightRadius = 8;
            btn.style.borderBottomLeftRadius = 8;
            btn.style.borderBottomRightRadius = 8;
            btn.style.marginBottom = 8;
        }

        public async UniTask ShowLevelResultAsync(bool success, int stars, int score, int steps, CancellationToken ct = default)
        {
            EnsureDoc();
            _root.Q("UI_LevelResult")?.RemoveFromHierarchy();

            var tcs = new UniTaskCompletionSource();
            using var reg = ct.Register(() => tcs.TrySetCanceled());

            var mask = new VisualElement { name = "UI_LevelResult" };
            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;
            mask.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            mask.style.justifyContent = Justify.Center;
            mask.style.alignItems = Align.Center;
            mask.pickingMode = PickingMode.Position;

            var card = new VisualElement { name = "UI_LevelResult_Card" };
            card.style.width = 360;
            card.style.maxWidth = Length.Percent(92);
            card.style.paddingTop = 28;
            card.style.paddingBottom = 22;
            card.style.paddingLeft = 22;
            card.style.paddingRight = 22;
            card.style.backgroundColor = new Color(0.14f, 0.16f, 0.22f, 1f);
            card.style.borderTopLeftRadius = 14;
            card.style.borderTopRightRadius = 14;
            card.style.borderBottomLeftRadius = 14;
            card.style.borderBottomRightRadius = 14;
            card.style.alignItems = Align.Center;

            if (success)
            {
                var title = new Label("过关！") { name = "LevelResult_Title" };
                title.style.fontSize = 28;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.color = new Color(1f, 0.9f, 0.3f, 1f);
                title.style.marginBottom = 12;
                card.Add(title);

                // 0~3 星
                stars = Mathf.Clamp(stars, 0, 3);
                var starRow = new VisualElement();
                starRow.style.flexDirection = FlexDirection.Row;
                starRow.style.justifyContent = Justify.Center;
                starRow.style.marginBottom = 14;
                for (var i = 0; i < 3; i++)
                {
                    var s = new Label(i < stars ? "★" : "☆");
                    s.style.fontSize = 36;
                    s.style.color = i < stars
                        ? new Color(1f, 0.85f, 0.15f, 1f)
                        : new Color(0.45f, 0.45f, 0.5f, 1f);
                    s.style.marginLeft = 6;
                    s.style.marginRight = 6;
                    starRow.Add(s);
                }
                card.Add(starRow);

                var info = new Label($"得分 {score}    步数 {steps}") { name = "LevelResult_Info" };
                info.style.fontSize = 15;
                info.style.color = new Color(0.9f, 0.92f, 0.95f, 1f);
                info.style.marginBottom = 20;
                card.Add(info);

                var next = new Button { text = "下一局", name = "LevelResult_Next" };
                next.style.height = 48;
                next.style.width = 200;
                next.style.fontSize = 18;
                next.style.backgroundColor = new Color(0.2f, 0.7f, 0.4f, 1f);
                next.style.color = Color.white;
                next.style.borderTopLeftRadius = 10;
                next.style.borderTopRightRadius = 10;
                next.style.borderBottomLeftRadius = 10;
                next.style.borderBottomRightRadius = 10;
                WireClickSfx(next);
                next.clicked += () =>
                {
                    mask.RemoveFromHierarchy();
                    tcs.TrySetResult();
                };
                card.Add(next);
            }
            else
            {
                // 右上角 X
                var topBar = new VisualElement();
                topBar.style.width = Length.Percent(100);
                topBar.style.flexDirection = FlexDirection.Row;
                topBar.style.justifyContent = Justify.FlexEnd;
                topBar.style.marginBottom = 4;
                var closeX = new Button { text = "✕", name = "LevelResult_CloseX" };
                closeX.style.width = 36;
                closeX.style.height = 36;
                closeX.style.fontSize = 18;
                closeX.style.backgroundColor = new Color(0.35f, 0.35f, 0.4f, 1f);
                closeX.style.color = Color.white;
                closeX.style.borderTopLeftRadius = 8;
                closeX.style.borderTopRightRadius = 8;
                closeX.style.borderBottomLeftRadius = 8;
                closeX.style.borderBottomRightRadius = 8;
                WireClickSfx(closeX);
                closeX.clicked += () =>
                {
                    mask.RemoveFromHierarchy();
                    tcs.TrySetResult();
                };
                topBar.Add(closeX);
                card.Add(topBar);

                var title = new Label("步数已耗尽") { name = "LevelResult_Title" };
                title.style.fontSize = 26;
                title.style.unityFontStyleAndWeight = FontStyle.Bold;
                title.style.color = new Color(1f, 0.45f, 0.4f, 1f);
                title.style.marginBottom = 12;
                card.Add(title);

                var info = new Label($"得分 {score}    已用步数 {steps}") { name = "LevelResult_Info" };
                info.style.fontSize = 15;
                info.style.color = new Color(0.9f, 0.92f, 0.95f, 1f);
                info.style.marginBottom = 8;
                card.Add(info);

                var hint = new Label("点击右上角关闭，返回主页") { name = "LevelResult_Hint" };
                hint.style.fontSize = 13;
                hint.style.color = new Color(0.7f, 0.72f, 0.78f, 1f);
                card.Add(hint);
            }

            mask.Add(card);
            _root.Add(mask);
            mask.BringToFront();

            try { await tcs.Task; }
            catch (OperationCanceledException)
            {
                mask.RemoveFromHierarchy();
                throw;
            }
        }

        public void PromptEnterLevel(int mapId, int levelId)
        {
            _focusLevelId = levelId;
            // 高亮已建好的关卡按钮
            HighlightLevelButton(levelId);
            ShowEnterConfirm(mapId, levelId);
        }

        void HighlightLevelButton(int levelId)
        {
            if (_root == null) return;
            for (var i = 1; i <= 10; i++)
            {
                var btn = _root.Q<Button>($"Home_Level_{i}");
                if (btn == null) continue;
                if (i == levelId)
                {
                    btn.style.borderTopWidth = 3;
                    btn.style.borderBottomWidth = 3;
                    btn.style.borderLeftWidth = 3;
                    btn.style.borderRightWidth = 3;
                    btn.style.borderTopColor = Color.white;
                    btn.style.borderBottomColor = Color.white;
                    btn.style.borderLeftColor = Color.white;
                    btn.style.borderRightColor = Color.white;
                    btn.style.scale = new Scale(new Vector3(1.08f, 1.08f, 1f));
                }
                else
                {
                    btn.style.borderTopWidth = 0;
                    btn.style.borderBottomWidth = 0;
                    btn.style.borderLeftWidth = 0;
                    btn.style.borderRightWidth = 0;
                    btn.style.scale = new Scale(Vector3.one);
                }
            }
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
            WireClickSfx(cancel);
            cancel.clicked += () => mask.RemoveFromHierarchy();
            row.Add(cancel);

            var ok = new Button { text = "进入", name = "EnterConfirm_Ok" };
            ok.style.width = 110;
            ok.style.height = 42;
            ok.style.backgroundColor = new Color(0.2f, 0.65f, 0.35f, 1f);
            ok.style.color = Color.white;
            int m = mapId, lv = levelId;
            WireClickSfx(ok);
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
        // IMatch3Hud 要求精确签名 ShowTip(string)
        public void ShowTip(string message) => ShowTip(message, 1.6f);

        public void ShowTip(string message, float seconds)
        {
            EnsureDoc();
            EnsureToast();

            _tipCts?.Cancel();
            _tipCts = new CancellationTokenSource();
            var ct = _tipCts.Token;

            _tipLabel.text = message ?? "";
            _tipRoot.style.display = DisplayStyle.Flex;
            _tipRoot.style.opacity = 1f;
            // 提到最前，避免被页面盖住
            _tipRoot.BringToFront();

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

        void EnsureToast()
        {
            if (_tipRoot != null && _tipLabel != null && _tipRoot.parent == _root)
                return;

            _root.Q("UI_Toast")?.RemoveFromHierarchy();

            _tipRoot = new VisualElement { name = "UI_Toast" };
            _tipRoot.style.position = Position.Absolute;
            _tipRoot.style.left = 16;
            _tipRoot.style.right = 16;
            _tipRoot.style.bottom = 48;
            _tipRoot.style.paddingTop = 12;
            _tipRoot.style.paddingBottom = 12;
            _tipRoot.style.paddingLeft = 16;
            _tipRoot.style.paddingRight = 16;
            _tipRoot.style.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 0.94f);
            _tipRoot.style.borderTopLeftRadius = 10;
            _tipRoot.style.borderTopRightRadius = 10;
            _tipRoot.style.borderBottomLeftRadius = 10;
            _tipRoot.style.borderBottomRightRadius = 10;
            _tipRoot.style.alignItems = Align.Center;
            _tipRoot.style.display = DisplayStyle.None;
            _tipRoot.pickingMode = PickingMode.Ignore;

            _tipLabel = new Label { name = "UI_Toast_Label" };
            _tipLabel.style.fontSize = 15;
            _tipLabel.style.color = Color.white;
            _tipLabel.style.whiteSpace = WhiteSpace.Normal;
            _tipLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _tipLabel.pickingMode = PickingMode.Ignore;
            _tipRoot.Add(_tipLabel);
            _root.Add(_tipRoot);
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
            _root.style.width = Length.Percent(100);
            _root.style.height = Length.Percent(100);
            // 默认不透明；进 Game 时由 ShowPanelAsync 改成 clear + Ignore
            _root.style.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
            _doc.rootVisualElement.Clear();
            // Panel 本身也不要清成不透明色（若 PanelSettings.ClearColor 开了）
            _doc.rootVisualElement.style.backgroundColor = Color.clear;
            _doc.rootVisualElement.Add(_root);
        }


        void BindCheckUpdate(VisualElement page)
        {
            // UXML 已含默认文案，可按需改：
            // var label = page.Q<Label>("CheckUpdate_Label");
            // if (label != null) label.text = "检查更新中...";
        }

        void BindLogin(VisualElement page)
        {
            var user = page.Q<TextField>("Login_User");
            var pass = page.Q<TextField>("Login_Pass");
            var btn = page.Q<Button>("Login_BtnSubmit");
            var offline = page.Q<Button>("Login_BtnOffline");

            if (user != null && string.IsNullOrEmpty(user.value))
                user.value = "test";
            if (pass != null)
            {
                pass.isPasswordField = true;
                if (string.IsNullOrEmpty(pass.value))
                    pass.value = "123456";
            }

            if (btn != null)
            {
                WireClickSfx(btn);
                btn.clicked += async () =>
                {
                    if (_onLogin == null) return;
                    btn.SetEnabled(false);
                    try { await _onLogin(user?.value ?? "", pass?.value ?? ""); }
                    finally { btn.SetEnabled(true); }
                };
            }

            if (offline != null)
            {
                WireClickSfx(offline);
                offline.clicked += async () =>
                {
                    if (_onOfflineEnter != null)
                        await _onOfflineEnter();
                };
            }
        }

        void BindHome(VisualElement page)
        {
            var title = page.Q<Label>("Home_Title");
            if (title != null)
                title.text = $"地图 {_homeMapId}";

            // 顶部：体力 icon + 背景数值
            var energyText = page.Q<Label>("Home_EnergyText");
            if (energyText != null)
                energyText.text = $"{_energy}/{_energyMax}";

            // 顶部：金币 icon + 背景数值
            var goldText = page.Q<Label>("Home_GoldText");
            if (goldText != null)
                goldText.text = _gold.ToString();

            var status = page.Q<Label>("Home_Status");
            if (status != null)
            {
                status.text =
                    $"已解锁地图 {_unlockedMap}    本图进度 {_clearedOnMap}/{_levelsPerMap}";
            }

            // 邮件 / 设置 按钮
            var btnMail = page.Q<Button>("Home_BtnMail");
            if (btnMail != null)
            {
                WireClickSfx(btnMail);
                btnMail.clicked += () => { /* TODO: 打开邮件 */ };
            }
            var btnSettings = page.Q<Button>("Home_BtnSettings");
            if (btnSettings != null)
            {
                WireClickSfx(btnSettings);
                btnSettings.clicked += () => ShowSettingsPopup();
            }

            var pathArea = page.Q("Home_PathArea");
            if (pathArea != null)
                FillHomeLevels(pathArea);

            var nextBtn = page.Q<Button>("Home_BtnNext");
            if (nextBtn != null)
            {
                nextBtn.text = $"第{_nextLevelId}关";
                WireClickSfx(nextBtn);
                nextBtn.clicked += () => ShowEnterConfirm(_homeMapId, _nextLevelId);
            }

            var logout = page.Q<Button>("Home_BtnLogout");
            if (logout != null)
            {
                WireClickSfx(logout);
                logout.clicked += async () =>
                {
                    var ok = await ShowConfirmAsync("确定要退出登录吗？", title: "退出登录", yesText: "是", noText: "否");
                    if (ok)
                        _onLogout?.Invoke();
                };
            }
        }

        /// <summary>
        /// 关卡圆按钮仍由代码动态生成（蛇形坐标），挂到 UXML 的 Home_PathArea。
        /// </summary>
        void FillHomeLevels(VisualElement pathArea)
        {
            pathArea.Clear();

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
                    btn.style.backgroundColor = new Color(0.25f, 0.55f, 0.95f, 1f);
                else
                    btn.style.backgroundColor = new Color(0.45f, 0.45f, 0.48f, 1f);

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

                WireClickSfx(btn);
                btn.clicked += () =>
                {
                    if (!capturedUnlocked)
                    {
                        ShowTip("关卡未解锁，请先通关前一关");
                        return;
                    }

                    Debug.Log($"[UI] click level map={capturedMap} level={capturedLevel}");
                    ShowEnterConfirm(capturedMap, capturedLevel);
                };

                if (_focusLevelId == levelId)
                {
                    btn.style.borderTopWidth = 3;
                    btn.style.borderBottomWidth = 3;
                    btn.style.borderLeftWidth = 3;
                    btn.style.borderRightWidth = 3;
                    btn.style.borderTopColor = Color.white;
                    btn.style.borderBottomColor = Color.white;
                    btn.style.borderLeftColor = Color.white;
                    btn.style.borderRightColor = Color.white;
                    btn.style.scale = new Scale(new Vector3(1.08f, 1.08f, 1f));
                }

                pathArea.Add(btn);
            }
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

        void BindGame(VisualElement page)
        {
            page.pickingMode = PickingMode.Ignore;
            page.style.backgroundColor = Color.clear;

            var status = page.Q<Label>("Game_StatusLabel");
            if (status != null)
                status.text = $"对局中  地图 {_gameMapId}  关 {_gameLevelId}";
        }

        // ---------- IMatch3Hud：与 Sprite 棋盘配合的顶栏 ----------

        Action _boardGiveUp;
        Action _boardShuffle;
        Label _boardHudLabel;
        VisualElement _boardChrome;

        public void ShowBoardChrome(LevelConfig cfg, Action onGiveUp, Action onShuffle)
        {
            EnsureDoc();
            _boardGiveUp = onGiveUp;
            _boardShuffle = onShuffle;

            // 若当前不在 Game 页，先清出 HUD 层
            var existing = _root.Q("Game_BoardChrome");
            existing?.RemoveFromHierarchy();

            var chrome = new VisualElement { name = "Game_BoardChrome" };
            chrome.style.position = Position.Absolute;
            chrome.style.left = 0;
            chrome.style.right = 0;
            chrome.style.top = 0;
            chrome.style.bottom = 0;
            // 不拦截棋盘点击：整页 pickingMode 忽略，只给按钮开启
            chrome.pickingMode = PickingMode.Ignore;

            var top = new VisualElement { name = "Game_HudTop" };
            top.style.position = Position.Absolute;
            top.style.left = 0;
            top.style.right = 0;
            top.style.top = 0;
            top.style.height = 100;
            top.style.backgroundColor = new Color(0.08f, 0.09f, 0.14f, 0.88f);
            top.style.paddingTop = 12;
            top.style.paddingLeft = 12;
            top.style.paddingRight = 12;
            top.pickingMode = PickingMode.Ignore;

            _boardHudLabel = new Label { name = "Game_HudLabel" };
            _boardHudLabel.style.fontSize = 16;
            _boardHudLabel.style.color = Color.white;
            _boardHudLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            _boardHudLabel.style.whiteSpace = WhiteSpace.Normal;
            _boardHudLabel.pickingMode = PickingMode.Ignore;
            top.Add(_boardHudLabel);

            var btnRow = new VisualElement();
            btnRow.style.flexDirection = FlexDirection.Row;
            btnRow.style.justifyContent = Justify.Center;
            btnRow.style.marginTop = 6;
            // 按钮需要可点
            btnRow.pickingMode = PickingMode.Position;

            var btnGiveUp = new Button { text = "放弃", name = "Game_BtnGiveUp" };
            StyleHudBtn(btnGiveUp, new Color(0.45f, 0.2f, 0.2f, 1f));
            btnGiveUp.clicked += () => _boardGiveUp?.Invoke();
            btnRow.Add(btnGiveUp);

            var btnShuffle = new Button { text = "洗牌", name = "Game_BtnShuffle" };
            btnShuffle.style.marginLeft = 12;
            StyleHudBtn(btnShuffle, new Color(0.25f, 0.35f, 0.55f, 1f));
            btnShuffle.clicked += () => _boardShuffle?.Invoke();
            btnRow.Add(btnShuffle);

            top.Add(btnRow);
            chrome.Add(top);
            _root.Add(chrome);
            _boardChrome = chrome;

            UpdateHud(cfg.MapId, cfg.LevelId, 0, cfg.GoalValue, cfg.MaxSteps);
        }

        static void StyleHudBtn(Button btn, Color bg)
        {
            btn.style.height = 36;
            btn.style.width = 96;
            btn.style.fontSize = 15;
            btn.style.backgroundColor = bg;
            btn.style.color = Color.white;
            btn.style.borderTopLeftRadius = 6;
            btn.style.borderTopRightRadius = 6;
            btn.style.borderBottomLeftRadius = 6;
            btn.style.borderBottomRightRadius = 6;
            btn.pickingMode = PickingMode.Position;
        }

        public void HideBoardChrome()
        {
            _boardChrome?.RemoveFromHierarchy();
            _boardChrome = null;
            _boardHudLabel = null;
            _boardGiveUp = null;
            _boardShuffle = null;
        }

        public void UpdateHud(int mapId, int levelId, int score, int goal, int stepsLeft)
        {
            if (_boardHudLabel == null) return;
            _boardHudLabel.text =
                $"地图 {mapId}  关 {levelId}    分数 {score}/{goal}\n" +
                $"剩余步数 {stepsLeft}    （Sprite 棋盘：点选相邻交换）";
        }

        // ShowTip 已存在

        static void StyleCenter(VisualElement e)
        {
            e.style.flexGrow = 1;
            e.style.justifyContent = Justify.Center;
            e.style.alignItems = Align.Center;
            e.style.color = Color.white;
        }
    }
}
