using System;
using System.Collections.Generic;
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

        // 邮件（mock，暂无 API）
        List<MailEntry> _mailList;
        ScrollView _mailScroll;
        VisualElement _mailListContent;
        VisualTreeAsset _mailItemTemplate;

        // 非破坏性 tip（不整页清空）
        VisualElement _tipRoot;
        Label _tipLabel;
        CancellationTokenSource _tipCts;

        string _currentUIAsset; // 当前 AB 加载的 UI 资源名（用于卸载）

        // 非全屏弹窗统一样式（Dialog / Confirm / Settings / Mail）
        static readonly Color PopupOverlayColor = new Color(0f, 0f, 0f, 0.55f);
        static readonly Color PopupCardColor = new Color(0.16f, 0.18f, 0.24f, 1f);

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
            _root.Q("UI_Dialog")?.RemoveFromHierarchy();

            var tcs = new UniTaskCompletionSource();
            using var reg = ct.Register(() => tcs.TrySetCanceled());

            var vta = await LoadPopupUxmlAsync("UI_Dialog");
            VisualElement mask;
            if (vta != null)
            {
                mask = vta.Instantiate();
                mask.name = "UI_Dialog";
            }
            else
            {
                Debug.LogWarning("[UI] UI_Dialog.uxml 缺失，回退代码构建");
                mask = BuildFallbackDialogMask();
            }

            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;

            var titleLabel = mask.Q<Label>("UI_Dialog_Title");
            if (titleLabel != null)
            {
                if (string.IsNullOrEmpty(title))
                    titleLabel.style.display = DisplayStyle.None;
                else
                {
                    titleLabel.text = title;
                    titleLabel.style.display = DisplayStyle.Flex;
                }
            }

            var body = mask.Q<Label>("UI_Dialog_Body");
            if (body != null) body.text = message ?? "";

            var ok = mask.Q<Button>("UI_Dialog_Ok");
            if (ok != null)
            {
                ok.text = string.IsNullOrEmpty(okText) ? "确定" : okText;
                WireClickSfx(ok);
                ok.clicked += () =>
                {
                    mask.RemoveFromHierarchy();
                    tcs.TrySetResult();
                };
            }

            mask.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == mask)
                {
                    mask.RemoveFromHierarchy();
                    tcs.TrySetResult();
                }
            });

            _root.Add(mask);
            mask.BringToFront();

            try { await tcs.Task; }
            catch (OperationCanceledException)
            {
                mask.RemoveFromHierarchy();
                throw;
            }
        }

        VisualElement BuildFallbackDialogMask()
        {
            var mask = new VisualElement { name = "UI_Dialog" };
            mask.style.backgroundColor = PopupOverlayColor;
            mask.style.justifyContent = Justify.Center;
            mask.style.alignItems = Align.Center;
            mask.pickingMode = PickingMode.Position;
            var card = new VisualElement { name = "UI_Dialog_Card" };
            card.style.width = 340;
            card.style.paddingTop = 20;
            card.style.paddingBottom = 18;
            card.style.paddingLeft = 20;
            card.style.paddingRight = 20;
            card.style.backgroundColor = PopupCardColor;
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.Add(new Label { name = "UI_Dialog_Title" });
            card.Add(new Label { name = "UI_Dialog_Body" });
            card.Add(new Button { name = "UI_Dialog_Ok", text = "确定" });
            mask.Add(card);
            return mask;
        }

        /// <summary>是/否确认框。返回 true = 点「是」。</summary>
        public async UniTask<bool> ShowConfirmAsync(
            string message,
            string title = "确认",
            string yesText = "是",
            string noText = "否",
            CancellationToken ct = default)
        {
            EnsureDoc();
            _root.Q("UI_Confirm")?.RemoveFromHierarchy();

            var tcs = new UniTaskCompletionSource<bool>();
            using var reg = ct.Register(() => tcs.TrySetCanceled());

            var vta = await LoadPopupUxmlAsync("UI_Confirm");
            VisualElement mask;
            if (vta != null)
            {
                mask = vta.Instantiate();
                mask.name = "UI_Confirm";
            }
            else
            {
                mask = new VisualElement { name = "UI_Confirm" };
                mask.style.backgroundColor = PopupOverlayColor;
                mask.style.justifyContent = Justify.Center;
                mask.style.alignItems = Align.Center;
                var card = new VisualElement();
                card.Add(new Label { name = "UI_Confirm_Title" });
                card.Add(new Label { name = "UI_Confirm_Body" });
                var row = new VisualElement { name = "UI_Confirm_Row" };
                row.Add(new Button { name = "UI_Confirm_No", text = "否" });
                row.Add(new Button { name = "UI_Confirm_Yes", text = "是" });
                card.Add(row);
                mask.Add(card);
            }

            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;

            var titleLabel = mask.Q<Label>("UI_Confirm_Title");
            if (titleLabel != null) titleLabel.text = string.IsNullOrEmpty(title) ? "确认" : title;
            var body = mask.Q<Label>("UI_Confirm_Body");
            if (body != null) body.text = message ?? "";

            var btnNo = mask.Q<Button>("UI_Confirm_No");
            if (btnNo != null)
            {
                btnNo.text = string.IsNullOrEmpty(noText) ? "否" : noText;
                WireClickSfx(btnNo);
                btnNo.clicked += () =>
                {
                    mask.RemoveFromHierarchy();
                    tcs.TrySetResult(false);
                };
            }
            var btnYes = mask.Q<Button>("UI_Confirm_Yes");
            if (btnYes != null)
            {
                btnYes.text = string.IsNullOrEmpty(yesText) ? "是" : yesText;
                WireClickSfx(btnYes);
                btnYes.clicked += () =>
                {
                    mask.RemoveFromHierarchy();
                    tcs.TrySetResult(true);
                };
            }

            _root.Add(mask);
            mask.BringToFront();

            try { return await tcs.Task; }
            catch (OperationCanceledException)
            {
                mask.RemoveFromHierarchy();
                throw;
            }
        }

        void ShowSettingsPopup()
        {
            ShowSettingsPopupAsync().Forget();
        }

        async UniTaskVoid ShowSettingsPopupAsync()
        {
            EnsureDoc();
            _root.Q("UI_Settings")?.RemoveFromHierarchy();

            var vta = await LoadPopupUxmlAsync("UI_Settings");
            if (vta == null)
            {
                Debug.LogError("[UI] UI_Settings.uxml 加载失败");
                return;
            }

            var mask = vta.Instantiate();
            mask.name = "UI_Settings";
            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;

            var profile = _player?.Profile;
            string nick = string.IsNullOrEmpty(profile?.nickname) ? "Player" : profile.nickname;
            string playerId = string.IsNullOrEmpty(profile?.id)
                ? (string.IsNullOrEmpty(profile?.mp_account_id) ? "—" : profile.mp_account_id)
                : profile.id;

            var nickLabel = mask.Q<Label>("UI_Settings_Nick");
            if (nickLabel != null) nickLabel.text = $"昵称：{nick}";

            var idLabel = mask.Q<Label>("UI_Settings_PlayerId");
            if (idLabel != null) idLabel.text = $"玩家ID：{playerId}";

            var btnRename = mask.Q<Button>("UI_Settings_BtnRename");
            if (btnRename != null)
            {
                WireClickSfx(btnRename);
                btnRename.clicked += () => ShowRenameNicknameDialog(nickLabel);
            }

            var bgmToggle = mask.Q<Toggle>("UI_Settings_Bgm");
            if (bgmToggle != null)
            {
                bgmToggle.value = _audio == null || _audio.BgmEnabled;
                bgmToggle.RegisterValueChangedCallback(evt => _audio?.SetBgmEnabled(evt.newValue));
            }
            var sfxToggle = mask.Q<Toggle>("UI_Settings_Sfx");
            if (sfxToggle != null)
            {
                sfxToggle.value = _audio == null || _audio.SfxEnabled;
                sfxToggle.RegisterValueChangedCallback(evt => _audio?.SetSfxEnabled(evt.newValue));
            }

            var btnCs = mask.Q<Button>("UI_Settings_BtnCs");
            if (btnCs != null)
            {
                WireClickSfx(btnCs);
                btnCs.clicked += () =>
                {
                    _ = ShowDialogAsync("如有问题请联系客服邮箱：support@example.com\n或通过游戏内反馈渠道提交。", title: "联系客服");
                };
            }
            var btnPrivacy = mask.Q<Button>("UI_Settings_BtnPrivacy");
            if (btnPrivacy != null)
            {
                WireClickSfx(btnPrivacy);
                btnPrivacy.clicked += () =>
                {
                    _ = ShowDialogAsync(
                        "我们重视您的隐私。本游戏会收集必要的账号与设备信息用于登录、存档与反作弊，不会向第三方出售个人数据。详细条款请以正式发布版本为准。",
                        title: "隐私协议");
                };
            }

            var ver = Application.version;
            if (string.IsNullOrEmpty(ver)) ver = "0.0.0";
            var verLabel = mask.Q<Label>("UI_Settings_Version");
            if (verLabel != null) verLabel.text = $"客户端版本：{ver}";

            var btnClose = mask.Q<Button>("UI_Settings_BtnClose");
            if (btnClose != null)
            {
                WireClickSfx(btnClose);
                btnClose.clicked += () => mask.RemoveFromHierarchy();
            }

            mask.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == mask)
                    mask.RemoveFromHierarchy();
            });

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


        // ==================== 邮件系统（mock + ListView 虚拟滚动） ====================

        class MailReward
        {
            public int ItemId;
            public int Count;
            public string Icon; // item_ 系列 sprite 名
            public string Name;
        }

        class MailEntry
        {
            public long Id;
            public string Title;
            public string Body;
            public long SendTimeUnix;
            public long ExpireTimeUnix;
            public bool Claimed;
            public List<MailReward> Rewards; // null/empty = 纯文本邮件
        }

        void EnsureMailMocks()
        {
            if (_mailList != null) return;
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _mailList = new List<MailEntry>();

            // 两条带奖励 + 多条纯文本，保证超过一页可滚动
            _mailList.Add(new MailEntry
            {
                Id = 1001,
                Title = "登录奖励 · 道具礼包",
                Body = "欢迎回来！这是您的登录补给，内含体力与道具，请尽快领取。过期后将无法再领取。祝游戏愉快！",
                SendTimeUnix = now - 1800,
                ExpireTimeUnix = now + 3600 * 48,
                Claimed = false,
                Rewards = new List<MailReward>
                {
                    new MailReward { ItemId = 8, Count = 1, Icon = "item_energy_10", Name = "体力+10" },
                    new MailReward { ItemId = 1, Count = 2, Icon = "item_hammer", Name = "锤子" },
                    new MailReward { ItemId = 7, Count = 100, Icon = "item_gold", Name = "金币" },
                }
            });
            _mailList.Add(new MailEntry
            {
                Id = 1002,
                Title = "周末补偿礼包",
                Body = "因上周短暂波动，特此发放补偿。请在有效期内领取全部道具。再次为带来的不便致歉。",
                SendTimeUnix = now - 3600 * 26,
                ExpireTimeUnix = now + 3600 * 72,
                Claimed = false,
                Rewards = new List<MailReward>
                {
                    new MailReward { ItemId = 9, Count = 5, Icon = "item_diamond", Name = "钻石" },
                    new MailReward { ItemId = 2, Count = 1, Icon = "item_rocket_h", Name = "横消" },
                }
            });

            string[] titles =
            {
                "系统维护通知", "版本更新说明", "活动预告：春季盛典", "好友申请提示",
                "排行榜结算", "签到提醒", "体力回满通知", "新关卡开放",
                "商城限时折扣", "安全提醒", "问卷调研邀请", "社区精选分享",
                "公会战开启", "每日任务刷新", "账号绑定奖励说明", "隐私政策更新",
            };
            string[] bodies =
            {
                "亲爱的玩家，服务器将于本周六 02:00–04:00 进行例行维护，期间无法登录。维护结束后将发放补偿邮件，敬请留意。感谢您的理解与支持！",
                "本次更新优化了三消手感与若干 UI 细节，修复了偶现的断线重连问题。建议在 Wi‑Fi 环境下完成热更。",
                "春季盛典即将开启：限定皮肤、双倍体力时段、排行榜专属称号等你来拿。活动时间与规则请见后续邮件。",
                "有玩家向你发送了好友申请，可在好友页查看并处理。互相关注后可赠送每日体力。",
                "本周排行榜已结算，你的名次与奖励已写入邮箱。奖励将在领取后自动入账。",
                "别忘了今日签到哦，连续签到可获得额外道具。断签后进度会重置。",
                "你的体力已恢复至上限，可以继续挑战下一关了。合理安排体力，冲刺更高星级！",
                "新地图关卡已开放，难度曲线已调整。通关可解锁新棋盘主题与音效。",
                "商城精选道具限时折扣中，锤子与火箭组合更划算。活动结束后恢复原价。",
                "请勿向他人泄露账号密码。如发现异常登录，请立即修改密码并联系客服。",
                "诚邀参与游戏体验问卷，填写可获得少量金币。你的反馈对我们很重要。",
                "社区本周精选通关录像与搭配攻略已更新，欢迎前往查看并留言讨论。",
                "公会战本轮已开启，报名截止前请确认公会成员在线。胜利公会将获得专属徽章。",
                "每日任务已刷新：完成指定关卡与收集目标可领取宝箱。记得在当天 24 点前领取。",
                "完成账号绑定（手机/邮箱）可领取一次性绑定礼包，并提升账号安全性。",
                "我们更新了隐私政策部分条款，主要涉及数据保存周期说明。继续使用即表示知晓相关变更。",
            };

            for (int i = 0; i < titles.Length; i++)
            {
                // 时间分布：今天、昨天、N 天前
                long send;
                if (i < 3) send = now - 600 * (i + 1);           // 今天（几小时内）
                else if (i < 6) send = now - 3600 * 24 - 3600 * i; // 昨天
                else send = now - 3600L * 24 * (i - 3);            // 多天前

                _mailList.Add(new MailEntry
                {
                    Id = 2000 + i,
                    Title = titles[i],
                    Body = bodies[i % bodies.Length],
                    SendTimeUnix = send,
                    ExpireTimeUnix = send + 3600L * 24 * 14,
                    Claimed = false,
                    Rewards = null
                });
            }
        }

        /// <summary>列表用相对时间：今天 / 昨天 / N天前</summary>
        static string FormatMailRelativeTime(long unix)
        {
            if (unix <= 0) return "—";
            try
            {
                var send = DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().Date;
                var today = DateTimeOffset.Now.ToLocalTime().Date;
                var days = (today - send).Days;
                if (days <= 0) return "今天";
                if (days == 1) return "昨天";
                return $"{days}天前";
            }
            catch { return "—"; }
        }

        static string FormatMailTime(long unix)
        {
            if (unix <= 0) return "—";
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
            }
            catch { return unix.ToString(); }
        }

        bool IsMailExpired(MailEntry m)
        {
            if (m.ExpireTimeUnix <= 0) return false;
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds() > m.ExpireTimeUnix;
        }


        /// <summary>列表缩略：按字数截断（约合窄屏一行半）。</summary>
        static string MakeMailPreview(string body, int maxChars = 36)
        {
            if (string.IsNullOrEmpty(body)) return "";
            body = body.Replace("\r", " ").Replace("\n", " ").Trim();
            if (body.Length <= maxChars) return body;
            return body.Substring(0, maxChars) + "…";
        }

        async UniTask<VisualTreeAsset> LoadPopupUxmlAsync(string assetName)
        {
            VisualTreeAsset vta = null;
#if UNITY_EDITOR
            vta = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                $"Assets/Bundles/UI/{assetName}.uxml");
#endif
            if (vta == null)
            {
                if (!ResManager.IsReady)
                    await ResManager.InitializeAsync();
                vta = await ResManager.LoadUIAsync(assetName);
            }
            return vta;
        }

        void ShowMailPopup()
        {
            ShowMailPopupAsync().Forget();
        }

        async UniTaskVoid ShowMailPopupAsync()
        {
            EnsureDoc();
            EnsureMailMocks();
            _root.Q("UI_Mail")?.RemoveFromHierarchy();
            _root.Q("UI_MailDetail")?.RemoveFromHierarchy();

            var vta = await LoadPopupUxmlAsync("UI_Mail");
            if (vta == null)
            {
                Debug.LogError("[Mail] 加载 UI_Mail.uxml 失败");
                ShowTip("邮件界面加载失败");
                return;
            }

            if (_mailItemTemplate == null)
                _mailItemTemplate = await LoadPopupUxmlAsync("UI_MailItem");

            var mask = vta.Instantiate();
            mask.name = "UI_Mail";
            // 强制实心底，防止样式丢失透出主页
            mask.style.position = Position.Absolute;
            mask.style.left = 0;
            mask.style.right = 0;
            mask.style.top = 0;
            mask.style.bottom = 0;
            mask.style.backgroundColor = PopupOverlayColor;
            mask.style.justifyContent = Justify.Center;
            mask.style.alignItems = Align.Center;

            var card = mask.Q("UI_Mail_Card");
            if (card != null)
            {
                card.style.backgroundColor = PopupCardColor;
                card.pickingMode = PickingMode.Position;
            }

            var btnClose = mask.Q<Button>("UI_Mail_BtnClose");
            if (btnClose != null)
            {
                WireClickSfx(btnClose);
                btnClose.clicked += () => mask.RemoveFromHierarchy();
            }

            var emptyHint = mask.Q("UI_Mail_Empty");
            var scroll = mask.Q<ScrollView>("UI_Mail_Scroll");
            var content = mask.Q("UI_Mail_ListContent");
            if (content == null && scroll != null)
            {
                content = new VisualElement { name = "UI_Mail_ListContent" };
                content.style.flexDirection = FlexDirection.Column;
                scroll.Add(content);
            }

            _mailScroll = scroll;
            _mailListContent = content;

            if (scroll != null)
            {
                scroll.mode = ScrollViewMode.Vertical;
                scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                scroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
                scroll.elasticity = 0.1f;
                scroll.scrollDecelerationRate = 0.135f;
                scroll.mouseWheelScrollSize = 40f;
                EnableMouseDragScroll(scroll);
            }

            RebuildMailListContent();

            if (emptyHint != null)
                emptyHint.style.display = _mailList.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (scroll != null)
                scroll.style.display = _mailList.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;

            mask.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == mask)
                    mask.RemoveFromHierarchy();
            });

            _root.Add(mask);
            mask.BringToFront();
            Debug.Log($"[Mail] popup open, items={_mailList.Count}");
        }

        void RebuildMailListContent()
        {
            if (_mailListContent == null) return;
            _mailListContent.Clear();
            if (_mailList == null) return;
            for (int i = 0; i < _mailList.Count; i++)
            {
                var row = MakeMailRow();
                BindMailRow(row, i);
                _mailListContent.Add(row);
            }
        }

        /// <summary>可滚范围：0 ~ contentHeight - viewportHeight。</summary>
        static float GetMailScrollMaxY(ScrollView sv)
        {
            if (sv == null) return 0f;
            var content = sv.contentContainer;
            if (content == null) return 0f;
            float contentH = content.layout.height;
            float viewH = sv.layout.height;
            if (float.IsNaN(contentH) || float.IsNaN(viewH) || contentH <= 0f || viewH <= 0f)
                return 0f;
            return Mathf.Max(0f, contentH - viewH);
        }

        static void SetMailScrollY(ScrollView sv, float y)
        {
            if (sv == null) return;
            float max = GetMailScrollMaxY(sv);
            y = Mathf.Clamp(y, 0f, max);
            var off = sv.scrollOffset;
            off.y = y;
            sv.scrollOffset = off;
        }

        /// <summary>
        /// 鼠标/触屏拖动滚动：头尾钳制 + 松手惯性。
        /// 超过阈值才捕获，避免挡 item 点击。
        /// </summary>
        static void EnableMouseDragScroll(ScrollView sv)
        {
            if (sv == null) return;

            bool holding = false;
            bool dragging = false;
            Vector2 last = default;
            Vector2 down = default;
            int pointerId = -1;
            float velocityY = 0f; // px / sec，内容方向（向下拖 → 内容上移 → offset 增大 → 速度为正）
            long lastMoveTicks = 0;
            IVisualElementScheduledItem inertiaJob = null;
            const float kThreshold = 10f;
            const float kFriction = 6.5f;   // 每秒衰减系数
            const float kStopSpeed = 28f;   // 低于此速度停

            void StopInertia()
            {
                inertiaJob?.Pause();
                inertiaJob = null;
                velocityY = 0f;
            }

            void StartInertia()
            {
                StopInertia();
                if (Mathf.Abs(velocityY) < kStopSpeed) return;

                float v = velocityY;
                inertiaJob = sv.schedule.Execute(() =>
                {
                    // 固定约 60fps 步进
                    const float dt = 1f / 60f;
                    // 指数摩擦
                    v *= Mathf.Exp(-kFriction * dt);
                    if (Mathf.Abs(v) < kStopSpeed)
                    {
                        SetMailScrollY(sv, sv.scrollOffset.y);
                        StopInertia();
                        return;
                    }
                    float next = sv.scrollOffset.y + v * dt;
                    float max = GetMailScrollMaxY(sv);
                    if (next <= 0f)
                    {
                        SetMailScrollY(sv, 0f);
                        StopInertia();
                        return;
                    }
                    if (next >= max)
                    {
                        SetMailScrollY(sv, max);
                        StopInertia();
                        return;
                    }
                    SetMailScrollY(sv, next);
                }).Every(16);
            }

            sv.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button != 0) return;
                StopInertia();
                holding = true;
                dragging = false;
                down = last = evt.position;
                pointerId = evt.pointerId;
                velocityY = 0f;
                lastMoveTicks = DateTime.UtcNow.Ticks;
            });

            sv.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!holding || evt.pointerId != pointerId) return;
                var pos = evt.position;
                if (!dragging)
                {
                    if (Vector2.Distance(down, pos) < kThreshold) return;
                    dragging = true;
                    sv.CapturePointer(pointerId);
                }

                float dy = pos.y - last.y; // 手指下移 dy>0 → 列表跟手下移 → offset 减小
                last = pos;

                long now = DateTime.UtcNow.Ticks;
                float dt = (now - lastMoveTicks) / (float)TimeSpan.TicksPerSecond;
                lastMoveTicks = now;
                if (dt > 0.0001f && dt < 0.1f)
                {
                    // offset 变化量 / dt
                    float inst = (-dy) / dt;
                    velocityY = Mathf.Lerp(velocityY, inst, 0.35f);
                }

                SetMailScrollY(sv, sv.scrollOffset.y - dy);
                evt.StopPropagation();
            });

            sv.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (evt.pointerId != pointerId) return;
                if (dragging && sv.HasPointerCapture(pointerId))
                    sv.ReleasePointer(pointerId);

                bool wasDragging = dragging;
                holding = false;
                dragging = false;
                pointerId = -1;

                // 最终钳一次，再惯性
                SetMailScrollY(sv, sv.scrollOffset.y);
                if (wasDragging)
                    StartInertia();
            });

            sv.RegisterCallback<PointerCaptureOutEvent>(_ =>
            {
                holding = false;
                dragging = false;
                pointerId = -1;
                SetMailScrollY(sv, sv.scrollOffset.y);
            });

            // 滚轮也钳制
            sv.RegisterCallback<WheelEvent>(evt =>
            {
                StopInertia();
                SetMailScrollY(sv, sv.scrollOffset.y + evt.delta.y * 0.5f);
                // 不 StopPropagation，让默认也处理；再钳一次
                sv.schedule.Execute(() => SetMailScrollY(sv, sv.scrollOffset.y)).ExecuteLater(0);
            });
        }

        VisualElement MakeMailRow()
        {
            // 整行：外层只负责间距，内层卡片实心底
            var row = new VisualElement { name = "MailRow" };
            row.pickingMode = PickingMode.Position;
            row.style.flexDirection = FlexDirection.Column;
            row.style.marginBottom = 10;
            row.style.flexShrink = 0;

            var card = new VisualElement { name = "Mail_Card" };
            card.pickingMode = PickingMode.Position;
            card.style.flexDirection = FlexDirection.Column;
            card.style.paddingTop = 12;
            card.style.paddingBottom = 12;
            card.style.paddingLeft = 12;
            card.style.paddingRight = 12;
            card.style.backgroundColor = new Color(0.20f, 0.22f, 0.28f, 1f);
            card.style.borderTopLeftRadius = 8;
            card.style.borderTopRightRadius = 8;
            card.style.borderBottomLeftRadius = 8;
            card.style.borderBottomRightRadius = 8;
            card.style.minHeight = 72;

            var top = new VisualElement { name = "Mail_Top" };
            top.style.flexDirection = FlexDirection.Row;
            top.style.justifyContent = Justify.SpaceBetween;
            top.style.alignItems = Align.Center;
            top.style.marginBottom = 6;
            top.style.flexShrink = 0;

            var title = new Label { name = "Mail_Title" };
            title.style.fontSize = 15;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = Color.white;
            title.style.flexGrow = 1;
            title.style.flexShrink = 1;
            title.style.marginRight = 8;
            title.style.overflow = Overflow.Hidden;
            title.style.textOverflow = TextOverflow.Ellipsis;
            title.style.whiteSpace = WhiteSpace.NoWrap;
            title.style.unityTextAlign = TextAnchor.MiddleLeft;
            title.style.height = 20;
            top.Add(title);

            var time = new Label { name = "Mail_Time" };
            time.style.fontSize = 11;
            time.style.color = new Color(0.55f, 0.59f, 0.66f, 1f);
            time.style.flexShrink = 0;
            time.style.unityTextAlign = TextAnchor.MiddleRight;
            time.style.height = 18;
            time.style.minWidth = 40;
            top.Add(time);
            card.Add(top);

            var preview = new Label { name = "Mail_Preview" };
            preview.style.fontSize = 12;
            preview.style.color = new Color(0.70f, 0.73f, 0.78f, 1f);
            preview.style.whiteSpace = WhiteSpace.Normal;
            preview.style.overflow = Overflow.Hidden;
            preview.style.height = 34;
            preview.style.flexShrink = 0;
            preview.style.unityTextAlign = TextAnchor.UpperLeft;
            card.Add(preview);

            var badge = new Label { name = "Mail_Badge" };
            badge.style.fontSize = 11;
            badge.style.color = new Color(0.47f, 0.78f, 0.55f, 1f);
            badge.style.marginTop = 4;
            badge.style.height = 16;
            badge.style.display = DisplayStyle.None;
            badge.style.flexShrink = 0;
            card.Add(badge);

            row.Add(card);
            return row;
        }

        void BindMailRow(VisualElement row, int index)
        {
            if (index < 0 || index >= _mailList.Count) return;
            var mail = _mailList[index];

            var title = row.Q<Label>("Mail_Title");
            if (title != null) title.text = mail.Title ?? "";

            var time = row.Q<Label>("Mail_Time");
            if (time != null) time.text = FormatMailRelativeTime(mail.SendTimeUnix);

            var preview = row.Q<Label>("Mail_Preview");
            if (preview != null) preview.text = MakeMailPreview(mail.Body, 36);

            var badge = row.Q<Label>("Mail_Badge");
            if (badge != null)
            {
                bool hasReward = mail.Rewards != null && mail.Rewards.Count > 0;
                if (hasReward && !mail.Claimed && !IsMailExpired(mail))
                {
                    badge.text = "有奖励可领";
                    badge.style.display = DisplayStyle.Flex;
                }
                else if (hasReward && mail.Claimed)
                {
                    badge.text = "已领取";
                    badge.style.color = new Color(0.55f, 0.6f, 0.65f, 1f);
                    badge.style.display = DisplayStyle.Flex;
                }
                else
                {
                    badge.text = "";
                    badge.style.display = DisplayStyle.None;
                }
            }

            // 整行可点进详情；与拖动滚动区分（移动超过阈值不触发）
            row.userData = mail.Id;
            ClearListItemHover(row);
            row.UnregisterCallback<PointerDownEvent>(OnMailRowPointerDown);
            row.UnregisterCallback<PointerMoveEvent>(OnMailRowPointerMove);
            row.UnregisterCallback<PointerUpEvent>(OnMailRowPointerUp);
            row.RegisterCallback<PointerDownEvent>(OnMailRowPointerDown);
            row.RegisterCallback<PointerMoveEvent>(OnMailRowPointerMove);
            row.RegisterCallback<PointerUpEvent>(OnMailRowPointerUp);
        }

        Vector2 _mailPointerDown;
        bool _mailPointerMoved;
        long _mailPointerId = -1;

        void OnMailRowPointerDown(PointerDownEvent evt)
        {
            _mailPointerDown = evt.position;
            _mailPointerMoved = false;
            _mailPointerId = evt.pointerId;
        }

        void OnMailRowPointerMove(PointerMoveEvent evt)
        {
            if (evt.pointerId != _mailPointerId) return;
            if (Vector2.Distance(_mailPointerDown, evt.position) > 12f)
                _mailPointerMoved = true;
        }

        void OnMailRowPointerUp(PointerUpEvent evt)
        {
            if (evt.pointerId != _mailPointerId) return;
            _mailPointerId = -1;
            if (_mailPointerMoved) return;
            var row = evt.currentTarget as VisualElement;
            if (row?.userData is long id)
            {
                var mail = _mailList?.Find(m => m.Id == id);
                if (mail != null)
                    ShowMailDetail(mail);
            }
        }

        void ShowMailDetail(MailEntry mail)
        {
            ShowMailDetailAsync(mail).Forget();
        }

        async UniTaskVoid ShowMailDetailAsync(MailEntry mail)
        {
            if (mail == null) return;
            EnsureDoc();
            _root.Q("UI_MailDetail")?.RemoveFromHierarchy();

            var vta = await LoadPopupUxmlAsync("UI_MailDetail");
            VisualElement root;
            if (vta != null)
            {
                root = vta.Instantiate();
                root.name = "UI_MailDetail";
            }
            else
            {
                Debug.LogWarning("[Mail] UI_MailDetail.uxml 缺失，使用简易详情");
                root = BuildMailDetailFallback();
            }

            root.style.position = Position.Absolute;
            root.style.left = 0;
            root.style.right = 0;
            root.style.top = 0;
            root.style.bottom = 0;

            void CloseDetail() => root.RemoveFromHierarchy();

            var btnBack = root.Q<Button>("UI_MailDetail_BtnBack");
            if (btnBack != null)
            {
                WireClickSfx(btnBack);
                btnBack.clicked += CloseDetail;
            }
            var btnClose = root.Q<Button>("UI_MailDetail_BtnClose");
            if (btnClose != null)
            {
                WireClickSfx(btnClose);
                btnClose.clicked += () =>
                {
                    CloseDetail();
                    _root.Q("UI_Mail")?.RemoveFromHierarchy();
                };
            }

            var title = root.Q<Label>("UI_MailDetail_Title");
            if (title != null) title.text = mail.Title ?? "";

            var meta = root.Q<Label>("UI_MailDetail_Meta");
            if (meta != null)
            {
                var expired = IsMailExpired(mail);
                meta.text =
                    $"发送：{FormatMailTime(mail.SendTimeUnix)}    过期：{FormatMailTime(mail.ExpireTimeUnix)}"
                    + (expired ? "  (已过期)" : "")
                    + (mail.Claimed ? "  (已领取)" : "");
                meta.style.color = expired
                    ? new Color(0.85f, 0.45f, 0.4f, 1f)
                    : new Color(0.55f, 0.59f, 0.66f, 1f);
            }

            var body = root.Q<Label>("UI_MailDetail_Body");
            if (body != null) body.text = mail.Body ?? "";

            var rewardRow = root.Q("UI_MailDetail_Rewards");
            if (rewardRow != null)
            {
                rewardRow.Clear();
                if (mail.Rewards != null && mail.Rewards.Count > 0)
                {
                    foreach (var r in mail.Rewards)
                    {
                        var chip = new VisualElement();
                        chip.style.flexDirection = FlexDirection.Row;
                        chip.style.alignItems = Align.Center;
                        chip.style.marginRight = 8;
                        chip.style.marginBottom = 4;
                        chip.style.paddingLeft = 6;
                        chip.style.paddingRight = 8;
                        chip.style.paddingTop = 4;
                        chip.style.paddingBottom = 4;
                        chip.style.backgroundColor = new Color(0.12f, 0.14f, 0.18f, 1f);
                        chip.style.borderTopLeftRadius = 6;
                        chip.style.borderTopRightRadius = 6;
                        chip.style.borderBottomLeftRadius = 6;
                        chip.style.borderBottomRightRadius = 6;

                        var icon = new VisualElement();
                        icon.style.width = 28;
                        icon.style.height = 28;
                        icon.style.marginRight = 4;
                        icon.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                        if (!string.IsNullOrEmpty(r.Icon))
                            ApplyMailIcon(icon, r.Icon);
                        chip.Add(icon);

                        var lbl = new Label($"{r.Name} x{r.Count}");
                        lbl.style.fontSize = 12;
                        lbl.style.color = Color.white;
                        chip.Add(lbl);
                        rewardRow.Add(chip);
                    }
                }
            }

            var actionRow = root.Q("UI_MailDetail_Actions");
            if (actionRow != null)
            {
                actionRow.Clear();
                bool hasReward = mail.Rewards != null && mail.Rewards.Count > 0;
                bool expired = IsMailExpired(mail);

                if (hasReward && !mail.Claimed && !expired)
                {
                    var btnClaim = new Button { text = "领取" };
                    StyleMailActionBtn(btnClaim, new Color(0.22f, 0.55f, 0.35f, 1f));
                    WireClickSfx(btnClaim);
                    var captured = mail;
                    btnClaim.clicked += () =>
                    {
                        OnMailClaim(captured);
                        root.RemoveFromHierarchy();
                        ShowMailDetail(captured);
                    };
                    actionRow.Add(btnClaim);
                }
                else if (hasReward && mail.Claimed)
                {
                    var done = new Label("已领取");
                    done.style.fontSize = 13;
                    done.style.color = new Color(0.55f, 0.75f, 0.6f, 1f);
                    done.style.marginRight = 8;
                    actionRow.Add(done);
                }

                var btnDel = new Button { text = "删除" };
                StyleMailActionBtn(btnDel, new Color(0.5f, 0.28f, 0.28f, 1f));
                WireClickSfx(btnDel);
                var delTarget = mail;
                btnDel.clicked += () =>
                {
                    OnMailDelete(delTarget);
                    root.RemoveFromHierarchy();
                };
                actionRow.Add(btnDel);
            }

            root.RegisterCallback<ClickEvent>(evt =>
            {
                if (evt.target == root)
                    root.RemoveFromHierarchy();
            });

            // 详情正文也触屏拖动、无滚动条
            var detailScroll = root.Q<ScrollView>("UI_MailDetail_Scroll");
            if (detailScroll != null)
            {
                detailScroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
                detailScroll.verticalScrollerVisibility = ScrollerVisibility.Hidden;
                detailScroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
                detailScroll.elasticity = 0.1f;
                detailScroll.scrollDecelerationRate = 0.135f;
            }

            _root.Add(root);
            root.BringToFront();
        }

        VisualElement BuildMailDetailFallback()
        {
            var root = new VisualElement { name = "UI_MailDetail" };
            root.style.backgroundColor = new Color(0, 0, 0, 0.45f);
            root.style.justifyContent = Justify.Center;
            root.style.alignItems = Align.Center;
            var card = new VisualElement { name = "UI_MailDetail_Card" };
            card.style.width = 400;
            card.style.maxWidth = Length.Percent(94);
            card.style.height = 520;
            card.style.paddingTop = 14;
            card.style.paddingBottom = 12;
            card.style.paddingLeft = 14;
            card.style.paddingRight = 14;
            card.style.backgroundColor = new Color(0.14f, 0.16f, 0.22f, 1f);
            card.style.borderTopLeftRadius = 12;
            card.style.borderTopRightRadius = 12;
            card.style.borderBottomLeftRadius = 12;
            card.style.borderBottomRightRadius = 12;
            card.style.flexDirection = FlexDirection.Column;
            var titleRow = new VisualElement { name = "UI_MailDetail_TitleRow" };
            titleRow.style.flexDirection = FlexDirection.Row;
            titleRow.style.justifyContent = Justify.SpaceBetween;
            titleRow.Add(new Button { text = "‹ 返回", name = "UI_MailDetail_BtnBack" });
            titleRow.Add(new Button { text = "×", name = "UI_MailDetail_BtnClose" });
            card.Add(titleRow);
            card.Add(new Label { name = "UI_MailDetail_Title" });
            card.Add(new Label { name = "UI_MailDetail_Meta" });
            var scroll = new ScrollView { name = "UI_MailDetail_Scroll" };
            scroll.style.flexGrow = 1;
            scroll.Add(new Label { name = "UI_MailDetail_Body" });
            scroll.Add(new VisualElement { name = "UI_MailDetail_Rewards" });
            card.Add(scroll);
            card.Add(new VisualElement { name = "UI_MailDetail_Actions" });
            root.Add(card);
            return root;
        }


        /// <summary>模仿手机列表：按住拖动 + 惯性，隐藏桌面滚动条。</summary>
        static void ConfigureTouchScroll(ListView listView)
        {
            if (listView == null) return;

            // 只通过查询取内部 ScrollView（不要用 listView.scrollView，部分版本无此 API）
            var sv = listView.Q<ScrollView>();
            if (sv == null) return;

            sv.mode = ScrollViewMode.Vertical;
            sv.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            sv.touchScrollBehavior = ScrollView.TouchScrollBehavior.Elastic;
            sv.elasticity = 0.1f;
            sv.scrollDecelerationRate = 0.135f;
            sv.mouseWheelScrollSize = 40f;
        }

        /// <summary>清掉 ListView 行容器默认 hover 白底。</summary>
        static void ClearListItemHover(VisualElement row)
        {
            if (row == null) return;
            // makeItem 返回的元素外层常被包成 unity-list-view__item
            var p = row.parent;
            for (int i = 0; i < 3 && p != null; i++, p = p.parent)
            {
                p.style.backgroundColor = Color.clear;
                p.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    if (evt.currentTarget is VisualElement ve)
                        ve.style.backgroundColor = Color.clear;
                });
                p.RegisterCallback<PointerLeaveEvent>(evt =>
                {
                    if (evt.currentTarget is VisualElement ve)
                        ve.style.backgroundColor = Color.clear;
                });
            }
        }

        static void StyleMailActionBtn(Button btn, Color bg)
        {
            btn.style.height = 32;
            btn.style.minWidth = 72;
            btn.style.marginLeft = 8;
            btn.style.fontSize = 13;
            btn.style.backgroundColor = bg;
            btn.style.color = Color.white;
            btn.style.borderTopLeftRadius = 6;
            btn.style.borderTopRightRadius = 6;
            btn.style.borderBottomLeftRadius = 6;
            btn.style.borderBottomRightRadius = 6;
        }

        /// <summary>
        /// 道具图标：优先读 Art/Sprites 的 .meta 对应资源（与 UI_Home 的 project:// URL 同源），
        /// Editor 用 AssetDatabase；真机再走 ResManager sprites/{name}。
        /// </summary>
        static readonly Dictionary<string, (string path, long fileId, string guid, string spriteName)> ItemIconMeta = new()
        {
            // 从 Assets/Art/Sprites/item_*.png.meta 解析
            ["item_energy_10"] = ("Assets/Art/Sprites/item_energy_10.png", -5040561177488907544L, "f04116923d6845241aa01e8eefce281c", "icon_03_0"),
            ["item_hammer"]    = ("Assets/Art/Sprites/item_hammer.png",     2407735339859285361L,  "bd4543475cd514d4f9787527a80b2242", "icon_04_0"),
            ["item_gold"]      = ("Assets/Art/Sprites/item_gold.png",       4673908504923276263L,  "105bbc148c38e3f4b932eb77b379e48c", "icon_01_0"),
            ["item_diamond"]   = ("Assets/Art/Sprites/item_diamond.png",   -9065347158251605998L, "85ea7ce944e75a54fa108a76c6a0376a", "icon_02_0"),
            ["item_steps_3"]   = ("Assets/Art/Sprites/item_steps_3.png",   -5608538184493476184L, "e0fa01ea9e4828c4ea314cd7dc5c6ef0", "icon_05_0"),
            ["item_score_20"]  = ("Assets/Art/Sprites/item_score_20.png",   904365746381333925L,  "efe8d27332740b4499b78292958b64c7", "icon_06_0"),
            ["item_rocket_h"]  = ("Assets/Art/Sprites/item_rocket_h.png",  -5644966865477618822L, "5d6b70159f1abc74984be18ce0a9fcdd", "icon_07_0"),
            ["item_rocket_v"]  = ("Assets/Art/Sprites/item_rocket_v.png",  -6240440480978129834L, "ae71ed0a3ec4fb440b7641c79e44d3b8", "icon_08_0"),
            ["item_flower_5col"]= ("Assets/Art/Sprites/item_flower_5col.png",8558041250643444950L, "f488ab70fca4cd14bb2c5d25aedd89f8", "icon_09_0"),
        };

        /// <summary>与 UI_Home.uxml 相同格式的 project:// background URL。</summary>
        static string BuildItemIconProjectUrl(string iconName)
        {
            if (!ItemIconMeta.TryGetValue(iconName, out var m))
                return null;
            return $"project://database/{m.path}?fileID={m.fileId}&guid={m.guid}&type=3#{m.spriteName}";
        }

        void ApplyMailIcon(VisualElement iconVe, string iconName)
        {
            if (iconVe == null || string.IsNullOrEmpty(iconName)) return;

            // 1) Editor：直接 AssetDatabase 取 Sprite（最稳，等同读 .meta）
#if UNITY_EDITOR
            if (ItemIconMeta.TryGetValue(iconName, out var meta))
            {
                var objs = UnityEditor.AssetDatabase.LoadAllAssetsAtPath(meta.path);
                Sprite sp = null;
                if (objs != null)
                {
                    foreach (var o in objs)
                    {
                        if (o is Sprite s && (s.name == meta.spriteName || sp == null))
                            sp = s;
                    }
                }
                if (sp == null)
                    sp = UnityEditor.AssetDatabase.LoadAssetAtPath<Sprite>(meta.path);
                if (sp != null)
                {
                    iconVe.style.backgroundImage = new StyleBackground(sp);
                    iconVe.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                    return;
                }
            }
#endif
            // 2) 异步 AB / ResManager
            LoadMailIconAsync(iconVe, iconName).Forget();
        }

        async UniTaskVoid LoadMailIconAsync(VisualElement iconVe, string iconName)
        {
            try
            {
                Sprite sp = null;
                // 先按配置表里的原名，再试 Bundles/Sprites
                try { sp = await ResManager.LoadSpriteAsync(iconName); } catch { /* ignore */ }
                if (sp == null && ItemIconMeta.TryGetValue(iconName, out var meta))
                {
                    try { sp = await ResManager.LoadSpriteAsync(meta.spriteName); } catch { /* ignore */ }
                }
                if (sp != null && iconVe != null && iconVe.panel != null)
                {
                    iconVe.style.backgroundImage = new StyleBackground(sp);
                    iconVe.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
                }
                else
                {
                    Debug.LogWarning($"[Mail] icon missing: {iconName} (请确认 Art/Sprites 与 Bundles/Sprites 已导入)");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Mail] load icon {iconName} failed: {e.Message}");
            }
        }

        void OnMailClaim(MailEntry mail)
        {
            if (mail == null) return;
            if (mail.Claimed)
            {
                ShowTip("已经领取过了");
                return;
            }
            if (IsMailExpired(mail))
            {
                ShowTip("邮件已过期，无法领取");
                return;
            }
            mail.Claimed = true;
            var names = mail.Rewards != null
                ? string.Join("、", mail.Rewards.ConvertAll(r => $"{r.Name}x{r.Count}"))
                : "";
            ShowTip(string.IsNullOrEmpty(names) ? "领取成功" : $"已领取：{names}");
            RebuildMailListContent();
        }

        void OnMailDelete(MailEntry mail)
        {
            if (mail == null || _mailList == null) return;
            _mailList.RemoveAll(m => m.Id == mail.Id);
            RebuildMailListContent();
            var popup = _root?.Q("UI_Mail");
            var empty = popup?.Q("UI_Mail_Empty");
            var scroll = popup?.Q("UI_Mail_Scroll");
            if (empty != null) empty.style.display = _mailList.Count == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (scroll != null) scroll.style.display = _mailList.Count == 0 ? DisplayStyle.None : DisplayStyle.Flex;
            ShowTip("已删除");
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
        /// <summary>进关确认：走通用 Confirm（message + 是/否），不单独做 EnterConfirm 面板。</summary>
        void ShowEnterConfirm(int mapId, int levelId)
        {
            ShowEnterConfirmAsync(mapId, levelId).Forget();
        }

        async UniTaskVoid ShowEnterConfirmAsync(int mapId, int levelId)
        {
            int cost = GameRuleConfig.EnergyCostPerLevel;
            bool ok = await ShowConfirmAsync(
                $"是否进入第 {levelId} 关？\n（消耗 {cost} 点体力）",
                title: "进入关卡",
                yesText: "进入",
                noText: "取消");
            if (!ok) return;

            Debug.Log($"[UI] confirm enter map={mapId} level={levelId}");
            if (_onStartLevel != null)
                await _onStartLevel(mapId, levelId);
            else if (_onStartGame != null)
                await _onStartGame();
        }

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
                btnMail.clicked += () => ShowMailPopup();
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
