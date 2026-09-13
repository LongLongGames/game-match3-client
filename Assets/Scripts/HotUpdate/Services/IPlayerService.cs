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
        /// 选下一关可玩关卡：(mapId, levelId)。体力不足或未解锁返回 false。
        /// </summary>
        bool TryGetNextPlayableLevel(out int mapId, out int levelId);

        /// <summary>
        /// 进关时扣除体力（客户端先行）。不足返回 false。
        /// 服务端 start-level API 就绪后应在此对接。
        /// </summary>
        bool TrySpendEnergyForEnter();

        /// <summary>通关上报；不再在此处扣体力（进关已扣）</summary>
        UniTask<ClearLevelResponse> ClearLevelAsync(
            int mapId, int levelId, int stars, int steps, long score,
            CancellationToken ct = default);
    }
}
