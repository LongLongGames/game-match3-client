using System.Threading;
using Cysharp.Threading.Tasks;
using HotUpdate.Network;

namespace HotUpdate.Services
{
    public interface IPlayerService
    {
        PlayerProfile Profile { get; }

        /// <summary>最近一次拉取的状态（体力/金币/进度）</summary>
        PlayerStateResponse State { get; }

        int Energy { get; }
        int EnergyMax { get; }
        long Gold { get; }
        int UnlockedMap { get; }
        int CurrentMapId { get; }

        UniTask RefreshProfileAsync(CancellationToken ct = default);
        UniTask RefreshStateAsync(int mapId = 1, CancellationToken ct = default);

        /// <summary>
        /// 选下一关可玩关卡：(mapId, levelId)。无体力或全通返回 false。
        /// </summary>
        bool TryGetNextPlayableLevel(out int mapId, out int levelId);

        /// <summary>通关上报；失败时仍可本地推进（弱网）</summary>
        UniTask<ClearLevelResponse> ClearLevelAsync(
            int mapId, int levelId, int stars, int steps, long score,
            CancellationToken ct = default);
    }
}
