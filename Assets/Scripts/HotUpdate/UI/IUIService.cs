using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.UI
{
    public interface IUIService
    {
        UniTask ShowPanelAsync(UIPanel panel, CancellationToken ct = default);
        UniTask ShowErrorAsync(string message, CancellationToken ct = default);

        void SetLoginHandler(Func<string, string, UniTask> handler);
        void SetOfflineEnterHandler(Func<UniTask> handler);
        void SetStartGameHandler(Func<UniTask> handler);
        void SetLogoutHandler(Action handler);

        /// <summary>主页状态条：体力 / 金币 / 地图进度</summary>
        void SetHomeStatus(int energy, int energyMax, long gold, int unlockedMap, int clearedOnMap, int levelsPerMap);

        /// <summary>对局中展示关卡信息</summary>
        void SetGameStatus(int mapId, int levelId, int maxSteps);
    }
}
