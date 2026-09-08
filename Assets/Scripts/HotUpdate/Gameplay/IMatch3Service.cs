using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.Gameplay
{
    public struct Match3Result
    {
        public bool Success;
        public int Score;
    }

    public interface IMatch3Service
    {
        /// <summary>
        /// 启动一局 Match3，返回最终分数。
        /// 真实实现替换为完整棋盘逻辑 + 动画。
        /// </summary>
        UniTask<Match3Result> PlayAsync(CancellationToken ct = default);
    }
}
