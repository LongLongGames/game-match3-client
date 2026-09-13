using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.Gameplay
{
    /// <summary>
    /// 棋盘视图：纯 SpriteRenderer（不用 UGUI / UIToolkit 格子）。
    /// 四色圆角方块运行时生成；输入用屏幕坐标 → 格子索引。
    /// HUD（分数/步数）仍可走 UIToolkit，由外部可选注入。
    /// </summary>
    public sealed class Match3SpriteView : IMatch3View
    {
        static readonly Color[] Colors =
        {
            new Color(0.92f, 0.28f, 0.32f, 1f),
            new Color(0.28f, 0.72f, 0.38f, 1f),
            new Color(0.28f, 0.52f, 0.95f, 1f),
            new Color(0.95f, 0.82f, 0.22f, 1f),
        };

        readonly IMatch3Hud _hud;

        GameObject _root;
        SpriteRenderer[,] _cells;
        Sprite[] _sprites;
        Match3Board _board;
        float _cellWorld = 0.72f;
        float _originX, _originY;
        int? _selX, _selY;
        bool _busy;
        UniTaskCompletionSource<Match3Result> _tcs;
        LevelConfig _cfg;
        CancellationToken _runCt;
        Camera _cam;
        bool _camOwned; // 我们临时创建的相机，退出时销毁
        bool _camSaved;
        bool _savedOrtho;
        float _savedOrthoSize;
        CameraClearFlags _savedClearFlags;
        Color _savedBgColor;
        float _savedNear, _savedFar;

        public Match3SpriteView(IMatch3Hud hud = null)
        {
            _hud = hud;
        }

        public async UniTask<Match3Result> RunAsync(Match3Board board, LevelConfig cfg, CancellationToken ct)
        {
            _board = board;
            _cfg = cfg;
            _runCt = ct;
            _selX = _selY = null;
            _busy = false;
            _tcs = new UniTaskCompletionSource<Match3Result>();
            using var reg = ct.Register(() => _tcs.TrySetCanceled());

            EnsureCamera();
            BuildSprites();
            BuildBoard();
            RefreshHud();
            _hud?.ShowBoardChrome(cfg, OnGiveUp, OnShuffle);

            try
            {
                while (_tcs.Task.Status == UniTaskStatus.Pending)
                {
                    ct.ThrowIfCancellationRequested();
                    PollInput();
                    await UniTask.Yield(PlayerLoopTiming.Update, ct);
                }
                return await _tcs.Task;
            }
            finally
            {
                Teardown();
                _hud?.HideBoardChrome();
            }
        }

        void EnsureCamera()
        {
            _camOwned = false;
            _cam = Camera.main;
            if (_cam == null)
            {
                var go = new GameObject("Match3Camera");
                _cam = go.AddComponent<Camera>();
                _cam.tag = "MainCamera";
                _camOwned = true;
            }

            // 备份，对局结束必须还原，否则回 Home 像「整屏被刷掉」
            if (!_camSaved)
            {
                _savedOrtho = _cam.orthographic;
                _savedOrthoSize = _cam.orthographicSize;
                _savedClearFlags = _cam.clearFlags;
                _savedBgColor = _cam.backgroundColor;
                _savedNear = _cam.nearClipPlane;
                _savedFar = _cam.farClipPlane;
                _camSaved = true;
            }

            _cam.orthographic = true;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0.10f, 0.12f, 0.18f, 1f);
        }

        void RestoreCamera()
        {
            if (_camOwned)
            {
                if (_cam != null)
                    UnityEngine.Object.Destroy(_cam.gameObject);
                _cam = null;
                _camOwned = false;
                _camSaved = false;
                return;
            }

            if (_cam != null && _camSaved)
            {
                _cam.orthographic = _savedOrtho;
                _cam.orthographicSize = _savedOrthoSize;
                _cam.clearFlags = _savedClearFlags;
                _cam.backgroundColor = _savedBgColor;
                _cam.nearClipPlane = _savedNear;
                _cam.farClipPlane = _savedFar;
            }
            _camSaved = false;
            _cam = null;
        }

        void BuildSprites()
        {
            if (_sprites != null) return;
            _sprites = new Sprite[Colors.Length];
            for (var i = 0; i < Colors.Length; i++)
                _sprites[i] = MakeRoundedSprite(Colors[i], 64);
        }

        static Sprite MakeRoundedSprite(Color color, int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            var r = size * 0.5f;
            var rr = r - 2f;
            var cx = r - 0.5f;
            var cy = r - 0.5f;
            for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var dx = x - cx;
                var dy = y - cy;
                var d = Mathf.Sqrt(dx * dx + dy * dy);
                float a;
                if (d <= rr - 1.5f) a = 1f;
                else if (d >= rr + 1.5f) a = 0f;
                else a = 1f - Mathf.InverseLerp(rr - 1.5f, rr + 1.5f, d);
                // 轻微内高光
                var highlight = Mathf.Clamp01(1f - (d / rr)) * 0.18f;
                var c = color;
                c.r = Mathf.Clamp01(c.r + highlight);
                c.g = Mathf.Clamp01(c.g + highlight);
                c.b = Mathf.Clamp01(c.b + highlight);
                c.a = a;
                tex.SetPixel(x, y, c);
            }
            tex.Apply(false, true);
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
        }

        void BuildBoard()
        {
            TeardownBoardOnly();
            _root = new GameObject("Match3BoardRoot");
            var w = _board.Width;
            var h = _board.Height;
            // 适配视野
            var span = Mathf.Max(w, h) * _cellWorld;
            _cam.orthographicSize = span * 0.55f + 1.2f;
            _originX = -(w - 1) * _cellWorld * 0.5f;
            _originY = -(h - 1) * _cellWorld * 0.5f;

            _cells = new SpriteRenderer[w, h];
            for (var x = 0; x < w; x++)
            for (var y = 0; y < h; y++)
            {
                var go = new GameObject($"Gem_{x}_{y}");
                go.transform.SetParent(_root.transform, false);
                go.transform.position = CellWorld(x, y);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = _sprites[_board.Get(x, y)];
                sr.sortingOrder = 10;
                // 略缩小，格子间距
                go.transform.localScale = Vector3.one * 0.92f;
                _cells[x, y] = sr;
            }
        }

        Vector3 CellWorld(int x, int y) =>
            new Vector3(_originX + x * _cellWorld, _originY + y * _cellWorld, 0f);

        void SyncVisuals()
        {
            if (_cells == null || _board == null) return;
            for (var x = 0; x < _board.Width; x++)
            for (var y = 0; y < _board.Height; y++)
            {
                var sr = _cells[x, y];
                if (sr == null) continue;
                var c = _board.Get(x, y);
                sr.enabled = c >= 0;
                if (c >= 0 && c < _sprites.Length)
                    sr.sprite = _sprites[c];
                sr.transform.position = CellWorld(x, y);
                var selected = _selX == x && _selY == y;
                sr.transform.localScale = Vector3.one * (selected ? 1.08f : 0.92f);
                sr.color = Color.white;
            }
        }

        void PollInput()
        {
            if (_busy || _board == null) return;
            if (!Input.GetMouseButtonDown(0)) return;
            if (_cam == null) return;

            // UIToolkit 点在屏幕上方 HUD 时尽量别点穿：简单按 y 比例过滤底部 88%
            var screen = Input.mousePosition;
            if (screen.y > Screen.height * 0.92f) return; // 顶栏留给 HUD

            var world = _cam.ScreenToWorldPoint(screen);
            if (!TryWorldToCell(world, out var cx, out var cy))
            {
                _selX = _selY = null;
                SyncVisuals();
                return;
            }
            OnCell(cx, cy);
        }

        bool TryWorldToCell(Vector3 world, out int cx, out int cy)
        {
            cx = cy = -1;
            var fx = (world.x - _originX) / _cellWorld;
            var fy = (world.y - _originY) / _cellWorld;
            var ix = Mathf.RoundToInt(fx);
            var iy = Mathf.RoundToInt(fy);
            if (!_board.InBounds(ix, iy)) return false;
            // 点击容差
            if (Mathf.Abs(fx - ix) > 0.48f || Mathf.Abs(fy - iy) > 0.48f) return false;
            cx = ix;
            cy = iy;
            return true;
        }

        void OnCell(int x, int y)
        {
            if (_busy) return;
            OnCellAsync(x, y).Forget();
        }

        async UniTaskVoid OnCellAsync(int x, int y)
        {
            if (_selX == null)
            {
                _selX = x;
                _selY = y;
                SyncVisuals();
                return;
            }
            if (_selX == x && _selY == y)
            {
                _selX = _selY = null;
                SyncVisuals();
                return;
            }

            var x0 = _selX.Value;
            var y0 = _selY.Value;
            _selX = _selY = null;

            if (!_board.IsAdjacent(x0, y0, x, y))
            {
                _selX = x;
                _selY = y;
                SyncVisuals();
                return;
            }

            _busy = true;
            try
            {
                if (_board.TrySwap(x0, y0, x, y, out var events))
                {
                    await PlayEventsAsync(events, _runCt);
                    SyncVisuals();
                    RefreshHud();

                    if (_board.IsGoalReached())
                    {
                        Finish(true);
                        return;
                    }
                    if (_board.IsOutOfSteps())
                    {
                        Finish(false);
                        return;
                    }
                    if (!_board.HasAnyValidMove())
                    {
                        _board.Shuffle();
                        SyncVisuals();
                        _hud?.ShowTip("无解，已洗牌");
                    }
                }
                else
                {
                    // 无效：快速晃一下再复原
                    await AnimateSwapAsync(x0, y0, x, y, 0.08f, _runCt);
                    await AnimateSwapAsync(x, y, x0, y0, 0.08f, _runCt);
                    SyncVisuals();
                    _hud?.ShowTip("无效交换");
                }
            }
            finally
            {
                _busy = false;
            }
        }

        async UniTask PlayEventsAsync(System.Collections.Generic.List<Match3Event> events, CancellationToken ct)
        {
            if (events == null || events.Count == 0)
            {
                SyncVisuals();
                return;
            }

            var i = 0;
            while (i < events.Count)
            {
                ct.ThrowIfCancellationRequested();
                var e = events[i];
                switch (e.Type)
                {
                    case Match3EventType.Swap:
                        await AnimateSwapAsync(e.X0, e.Y0, e.X1, e.Y1, 0.12f, ct);
                        i++;
                        break;

                    case Match3EventType.Clear:
                        await AnimateClearAsync(e.Cells, 0.12f, ct);
                        i++;
                        break;

                    case Match3EventType.Fall:
                    {
                        var falls = new System.Collections.Generic.List<Match3Event>();
                        while (i < events.Count && events[i].Type == Match3EventType.Fall)
                        {
                            falls.Add(events[i]);
                            i++;
                        }
                        await AnimateFallsAsync(falls, 0.16f, ct);
                        break;
                    }

                    case Match3EventType.Spawn:
                    {
                        var spawns = new System.Collections.Generic.List<Match3Event>();
                        while (i < events.Count && events[i].Type == Match3EventType.Spawn)
                        {
                            spawns.Add(events[i]);
                            i++;
                        }
                        await AnimateSpawnsAsync(spawns, 0.16f, ct);
                        break;
                    }

                    default:
                        i++;
                        break;
                }
            }
        }

        async UniTask AnimateSwapAsync(int x0, int y0, int x1, int y1, float duration, CancellationToken ct)
        {
            var a = _cells[x0, y0];
            var b = _cells[x1, y1];
            if (a == null || b == null) return;

            var pa = CellWorld(x0, y0);
            var pb = CellWorld(x1, y1);
            // 交换时提高 sorting，避免穿插
            var oa = a.sortingOrder;
            var ob = b.sortingOrder;
            a.sortingOrder = 20;
            b.sortingOrder = 20;

            var t = 0f;
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();
                t += UnityEngine.Time.deltaTime;
                var u = Mathf.Clamp01(t / duration);
                // ease out
                u = 1f - (1f - u) * (1f - u);
                a.transform.position = Vector3.Lerp(pa, pb, u);
                b.transform.position = Vector3.Lerp(pb, pa, u);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // 交换 sprite，位置归位（槽位模型）
            var tmpSprite = a.sprite;
            a.sprite = b.sprite;
            b.sprite = tmpSprite;
            a.transform.position = pa;
            b.transform.position = pb;
            a.sortingOrder = oa;
            b.sortingOrder = ob;
        }

        async UniTask AnimateClearAsync(System.Collections.Generic.List<(int x, int y)> cells, float duration, CancellationToken ct)
        {
            if (cells == null || cells.Count == 0) return;
            var t = 0f;
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();
                t += UnityEngine.Time.deltaTime;
                var u = Mathf.Clamp01(t / duration);
                var s = 0.92f * (1f - u);
                var alpha = 1f - u;
                foreach (var (x, y) in cells)
                {
                    var sr = _cells[x, y];
                    if (sr == null) continue;
                    sr.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
                    var c = sr.color;
                    c.a = alpha;
                    sr.color = c;
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            foreach (var (x, y) in cells)
            {
                var sr = _cells[x, y];
                if (sr == null) continue;
                sr.enabled = false;
                sr.color = Color.white;
                sr.transform.localScale = Vector3.one * 0.92f;
            }
        }

        async UniTask AnimateFallsAsync(System.Collections.Generic.List<Match3Event> falls, float duration, CancellationToken ct)
        {
            if (falls == null || falls.Count == 0) return;

            // 槽位模型：把「源格」的 sprite 挪到「目标格」显示，并从源位置 lerp
            var movers = new System.Collections.Generic.List<(SpriteRenderer sr, Vector3 from, Vector3 to, Sprite sp)>();
            foreach (var e in falls)
            {
                var src = _cells[e.X0, e.Y0];
                var dst = _cells[e.X1, e.Y1];
                if (src == null || dst == null) continue;
                var sp = src.sprite;
                // 源已空（被消或已搬走）
                src.enabled = false;
                dst.enabled = true;
                dst.sprite = sp;
                dst.transform.position = CellWorld(e.X0, e.Y0);
                movers.Add((dst, CellWorld(e.X0, e.Y0), CellWorld(e.X1, e.Y1), sp));
            }

            var t = 0f;
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();
                t += UnityEngine.Time.deltaTime;
                var u = Mathf.Clamp01(t / duration);
                // 下落 ease-in
                u = u * u;
                foreach (var (sr, from, to, _) in movers)
                {
                    if (sr == null) continue;
                    sr.transform.position = Vector3.Lerp(from, to, u);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            foreach (var (sr, _, to, _) in movers)
            {
                if (sr == null) continue;
                sr.transform.position = to;
                sr.enabled = true;
                sr.color = Color.white;
                sr.transform.localScale = Vector3.one * 0.92f;
            }
        }

        async UniTask AnimateSpawnsAsync(System.Collections.Generic.List<Match3Event> spawns, float duration, CancellationToken ct)
        {
            if (spawns == null || spawns.Count == 0) return;

            // 按列分组，同列从高处错落落下
            var movers = new System.Collections.Generic.List<(SpriteRenderer sr, Vector3 from, Vector3 to)>();
            foreach (var e in spawns)
            {
                var sr = _cells[e.X0, e.Y0];
                if (sr == null) continue;
                if (e.Color >= 0 && e.Color < _sprites.Length)
                    sr.sprite = _sprites[e.Color];
                sr.enabled = true;
                sr.color = Color.white;
                sr.transform.localScale = Vector3.one * 0.92f;
                var to = CellWorld(e.X0, e.Y0);
                // 从棋盘上方进入
                var from = CellWorld(e.X0, _board.Height + 1 + e.Y0);
                sr.transform.position = from;
                movers.Add((sr, from, to));
            }

            var t = 0f;
            while (t < duration)
            {
                ct.ThrowIfCancellationRequested();
                t += UnityEngine.Time.deltaTime;
                var u = Mathf.Clamp01(t / duration);
                u = u * u;
                foreach (var (sr, from, to) in movers)
                {
                    if (sr == null) continue;
                    sr.transform.position = Vector3.Lerp(from, to, u);
                }
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            foreach (var (sr, _, to) in movers)
            {
                if (sr == null) continue;
                sr.transform.position = to;
            }
        }

        void OnGiveUp()
        {
            if (_busy) return;
            Finish(false);
        }

        void OnShuffle()
        {
            if (_busy || _board == null) return;
            _board.Shuffle();
            _selX = _selY = null;
            SyncVisuals();
            _hud?.ShowTip("已洗牌");
        }

        void RefreshHud()
        {
            if (_board == null || _cfg == null) return;
            var left = Mathf.Max(0, _board.MaxSteps - _board.StepsUsed);
            _hud?.UpdateHud(_cfg.MapId, _cfg.LevelId, _board.Score, _board.GoalValue, left);
        }

        void Finish(bool success)
        {
            _busy = false;
            _tcs?.TrySetResult(new Match3Result
            {
                Success = success,
                MapId = _cfg.MapId,
                LevelId = _cfg.LevelId,
                Steps = _board.StepsUsed,
                Score = _board.Score,
                Stars = 0
            });
        }

        void TeardownBoardOnly()
        {
            if (_root != null)
            {
                UnityEngine.Object.Destroy(_root);
                _root = null;
            }
            _cells = null;
        }

        void Teardown()
        {
            TeardownBoardOnly();
            RestoreCamera();
            _board = null;
            _cfg = null;
        }
    }

    /// <summary>棋盘 HUD：分数/步数/放弃/洗牌。由 UIService 实现，与 Sprite 棋盘解耦。</summary>
    public interface IMatch3Hud
    {
        void ShowBoardChrome(LevelConfig cfg, Action onGiveUp, Action onShuffle);
        void HideBoardChrome();
        void UpdateHud(int mapId, int levelId, int score, int goal, int stepsLeft);
        void ShowTip(string message);
    }
}
