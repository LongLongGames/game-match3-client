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
        /// 进关：POST /api/v1/user/level/enter 由服务端扣体力。
        /// 成功 (true, null) 并同步本地；失败 (false, msg) 不进关。
        /// 无 token 时离线本地扣体力。
        /// </summary>
        UniTask<(bool ok, string error)> EnterLevelAsync(int mapId, int levelId, CancellationToken ct = default);

        /// <summary>通关上报；不再在此处扣体力（进关已扣）</summary>
        UniTask<ClearLevelResponse> ClearLevelAsync(
            int mapId, int levelId, int stars, int steps, long score,
            CancellationToken ct = default);
    }
}
