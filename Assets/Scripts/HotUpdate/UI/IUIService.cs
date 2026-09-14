using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HotUpdate.Network;

namespace HotUpdate.UI
{
    public interface IUIService
    {
        UniTask ShowPanelAsync(UIPanel panel, CancellationToken ct = default);

        /// <summary>兼容旧调用：走 Dialog（不整页清空）。</summary>
        UniTask ShowErrorAsync(string message, CancellationToken ct = default);

        /// <summary>轻提示，不挡操作，自动消失。</summary>
        void ShowToast(string message, float seconds = 2f);

        /// <summary>模态弹窗，点确认后返回。title 可空。</summary>
        UniTask ShowDialogAsync(string message, string title = null, string okText = "确定", CancellationToken ct = default);

        void SetLoginHandler(Func<string, string, UniTask> handler);
        void SetOfflineEnterHandler(Func<UniTask> handler);
        void SetStartGameHandler(Func<UniTask> handler);
        /// <summary>点击某个关卡按钮时触发（mapId, levelId）</summary>
        void SetStartLevelHandler(Func<int, int, UniTask> handler);
        void SetLogoutHandler(Action handler);

        /// <summary>主页状态条：体力 / 金币 / 地图进度 / 下一可玩关卡号</summary>
        void SetHomeStatus(int energy, int energyMax, long gold, int unlockedMap, int clearedOnMap, int levelsPerMap, int nextLevelId = 1);

        /// <summary>当前地图关卡星级（长度 10，index 0 = 第 1 关）</summary>
        void SetHomeLevels(int mapId, LevelProgressItem[] levels);

        /// <summary>对局中展示关卡信息</summary>
        void SetGameStatus(int mapId, int levelId, int maxSteps);

        /// <summary>对局结束结果弹窗。成功：星级+下一局；失败：步数耗尽+关闭。</summary>
        UniTask ShowLevelResultAsync(bool success, int stars, int score, int steps, CancellationToken ct = default);

        /// <summary>主页高亮某关，并弹出「是否进入」确认。</summary>
        void PromptEnterLevel(int mapId, int levelId);
    }
}

