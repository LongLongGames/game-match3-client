using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.Gameplay
{
    public struct Match3Result
    {
        public bool Success;
        public int MapId;
        public int LevelId;
        public int Stars;
        public int Steps;
        public int Score;
    }

    public interface IMatch3Service
    {
        /// <summary>
        /// 启动一局 Match3。cfg 为客户端本地关卡配置。
        /// </summary>
        UniTask<Match3Result> PlayAsync(LevelConfig cfg, CancellationToken ct = default);
    }
}
