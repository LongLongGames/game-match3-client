using System;
using System.Collections.Generic;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 纯逻辑棋盘：9×9（可配）、四色、交换/消除/掉落/补子。
    /// 不含 UI；由 Match3Service + UI 驱动。
    /// </summary>
    public sealed class Match3Board
    {
        public const int ColorCount = 4;

        public int Width { get; }
        public int Height { get; }
        public int[,] Grid { get; } // 0..ColorCount-1；-1 表示空

        public int Score { get; private set; }
        public int StepsUsed { get; private set; }
        public int MaxSteps { get; }
        public int GoalValue { get; }
        public string Goal { get; }

        readonly System.Random _rng;

        public Match3Board(int width, int height, int maxSteps, string goal, int goalValue, int? seed = null)
        {
            Width = Mathf.Clamp(width, 5, 12);
            Height = Mathf.Clamp(height, 5, 12);
            MaxSteps = Mathf.Max(1, maxSteps);
            Goal = string.IsNullOrEmpty(goal) ? "score" : goal;
            GoalValue = Mathf.Max(1, goalValue);
            Grid = new int[Width, Height];
            _rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
            FillInitialNoMatch();
        }

        public int Get(int x, int y) => Grid[x, y];

        public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

        public bool IsAdjacent(int x0, int y0, int x1, int y1)
        {
            var dx = Math.Abs(x0 - x1);
            var dy = Math.Abs(y0 - y1);
            return (dx == 1 && dy == 0) || (dx == 0 && dy == 1);
        }

        /// <summary>尝试交换两格。成功消耗 1 步并处理连锁；失败不扣步。</summary>
        public bool TrySwap(int x0, int y0, int x1, int y1, out List<Match3Event> events)
        {
            events = new List<Match3Event>();
            if (!InBounds(x0, y0) || !InBounds(x1, y1) || !IsAdjacent(x0, y0, x1, y1))
                return false;
            if (StepsUsed >= MaxSteps)
                return false;

            SwapCells(x0, y0, x1, y1);
            var matched = FindMatches();
            if (matched.Count == 0)
            {
                // 无效交换，换回
                SwapCells(x0, y0, x1, y1);
                return false;
            }

            StepsUsed++;
            events.Add(new Match3Event { Type = Match3EventType.Swap, X0 = x0, Y0 = y0, X1 = x1, Y1 = y1 });

            ResolveCascades(events);
            DumpBoard($"after match/swap step={StepsUsed} score={Score} cleared cascade done");
            return true;
        }

        void ResolveCascades(List<Match3Event> events)
        {
            const int maxCascade = 32;
            for (var i = 0; i < maxCascade; i++)
            {
                var matched = FindMatches();
                if (matched.Count == 0)
                    break;

                // 计分：每消 1 格 +10，同次多消额外加成
                var cleared = matched.Count;
                var add = cleared * 10 + Math.Max(0, cleared - 3) * 5;
                Score += add;
                events.Add(new Match3Event
                {
                    Type = Match3EventType.Clear,
                    Cells = matched,
                    ScoreDelta = add
                });

                foreach (var (x, y) in matched)
                    Grid[x, y] = -1;

                ApplyGravity(events);
                Refill(events);
            }
        }

        public bool IsGoalReached()
        {
            if (string.Equals(Goal, "score", StringComparison.OrdinalIgnoreCase))
                return Score >= GoalValue;
            // 扩展：collect 等目标可在此加
            return Score >= GoalValue;
        }

        public bool IsOutOfSteps() => StepsUsed >= MaxSteps;

        public bool HasAnyValidMove()
        {
            for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++)
            {
                if (x + 1 < Width)
                {
                    SwapCells(x, y, x + 1, y);
                    var ok = FindMatches().Count > 0;
                    SwapCells(x, y, x + 1, y);
                    if (ok) return true;
                }
                if (y + 1 < Height)
                {
                    SwapCells(x, y, x, y + 1);
                    var ok = FindMatches().Count > 0;
                    SwapCells(x, y, x, y + 1);
                    if (ok) return true;
                }
            }
            return false;
        }

        /// <summary>无解时洗牌（保留颜色数量大致分布）。</summary>
        public void Shuffle()
        {
            var list = new List<int>(Width * Height);
            for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++)
                list.Add(Grid[x, y]);

            for (var attempt = 0; attempt < 40; attempt++)
            {
                // Fisher–Yates
                for (var i = list.Count - 1; i > 0; i--)
                {
                    var j = _rng.Next(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }
                var idx = 0;
                for (var x = 0; x < Width; x++)
                for (var y = 0; y < Height; y++)
                    Grid[x, y] = list[idx++];

                if (FindMatches().Count == 0 && HasAnyValidMove())
                {
                    DumpBoard("after shuffle");
                    return;
                }
            }
            // 兜底：强制无初始匹配
            FillInitialNoMatch();
        }

        void FillInitialNoMatch()
        {
            for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++)
            {
                int c;
                var guard = 0;
                do
                {
                    c = _rng.Next(ColorCount);
                    guard++;
                } while (guard < 20 && WouldMatchAt(x, y, c));
                Grid[x, y] = c;
            }
            DumpBoard("initial fill");
        }

        /// <summary>
        /// Console dump full board. Top row = y max. Colors: 0=R 1=G 2=B 3=Y; -1 empty.
        /// </summary>
        public void DumpBoard(string tag)
        {
            const string names = "RGBY";
            var sb = new System.Text.StringBuilder(Width * Height * 2 + 64);
            sb.Append("[Match3Board] ").Append(tag);
            sb.Append(" size=").Append(Width).Append('x').Append(Height);
            sb.Append(" steps=").Append(StepsUsed).Append('/').Append(MaxSteps);
            sb.Append(" score=").Append(Score);
            sb.AppendLine();
            sb.Append("    ");
            for (var x = 0; x < Width; x++)
                sb.Append(x % 10).Append(' ');
            sb.AppendLine();
            for (var ui = 0; ui < Height; ui++)
            {
                var y = Height - 1 - ui;
                sb.Append(y.ToString().PadLeft(2)).Append("| ");
                for (var x = 0; x < Width; x++)
                {
                    var c = Grid[x, y];
                    if (c < 0) sb.Append(". ");
                    else if (c < names.Length) sb.Append(names[c]).Append(' ');
                    else sb.Append(c).Append(' ');
                }
                sb.AppendLine();
            }
            UnityEngine.Debug.Log(sb.ToString());
        }

        bool WouldMatchAt(int x, int y, int color)
        {
            // 水平左
            if (x >= 2 && Grid[x - 1, y] == color && Grid[x - 2, y] == color)
                return true;
            // 垂直下（生成时从下往上也可，这里 y 小为底或顶无所谓，统一检查两边已填）
            if (y >= 2 && Grid[x, y - 1] == color && Grid[x, y - 2] == color)
                return true;
            return false;
        }

        void SwapCells(int x0, int y0, int x1, int y1)
        {
            (Grid[x0, y0], Grid[x1, y1]) = (Grid[x1, y1], Grid[x0, y0]);
        }

        List<(int x, int y)> FindMatches()
        {
            var set = new HashSet<(int, int)>();
            // 水平
            for (var y = 0; y < Height; y++)
            {
                var x = 0;
                while (x < Width)
                {
                    var c = Grid[x, y];
                    if (c < 0) { x++; continue; }
                    var x2 = x + 1;
                    while (x2 < Width && Grid[x2, y] == c) x2++;
                    if (x2 - x >= 3)
                        for (var i = x; i < x2; i++) set.Add((i, y));
                    x = x2;
                }
            }
            // 垂直
            for (var x = 0; x < Width; x++)
            {
                var y = 0;
                while (y < Height)
                {
                    var c = Grid[x, y];
                    if (c < 0) { y++; continue; }
                    var y2 = y + 1;
                    while (y2 < Height && Grid[x, y2] == c) y2++;
                    if (y2 - y >= 3)
                        for (var i = y; i < y2; i++) set.Add((x, i));
                    y = y2;
                }
            }
            return new List<(int, int)>(set);
        }

        void ApplyGravity(List<Match3Event> events)
        {
            for (var x = 0; x < Width; x++)
            {
                var write = 0; // y=0 为底部
                for (var y = 0; y < Height; y++)
                {
                    if (Grid[x, y] >= 0)
                    {
                        if (write != y)
                        {
                            Grid[x, write] = Grid[x, y];
                            Grid[x, y] = -1;
                            events.Add(new Match3Event
                            {
                                Type = Match3EventType.Fall,
                                X0 = x, Y0 = y, X1 = x, Y1 = write
                            });
                        }
                        write++;
                    }
                }
            }
        }

        void Refill(List<Match3Event> events)
        {
            for (var x = 0; x < Width; x++)
            for (var y = 0; y < Height; y++)
            {
                if (Grid[x, y] < 0)
                {
                    var c = _rng.Next(ColorCount);
                    Grid[x, y] = c;
                    events.Add(new Match3Event
                    {
                        Type = Match3EventType.Spawn,
                        X0 = x, Y0 = y,
                        Color = c
                    });
                }
            }
        }
    }

    public enum Match3EventType
    {
        Swap,
        Clear,
        Fall,
        Spawn
    }

    public struct Match3Event
    {
        public Match3EventType Type;
        public int X0, Y0, X1, Y1;
        public int Color;
        public int ScoreDelta;
        public List<(int x, int y)> Cells;
    }
}
