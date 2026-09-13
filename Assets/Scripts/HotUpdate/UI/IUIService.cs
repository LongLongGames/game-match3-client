using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using HotUpdate.Network;

namespace HotUpdate.UI
{
    public interface IUIService
    {
        UniTask ShowPanelAsync(UIPanel panel, CancellationToken ct = default);
        UniTask ShowErrorAsync(string message, CancellationToken ct = default);

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
    }
}
