using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.DebugTools
{
    /// <summary>
    /// 热更运行时可注册的调试命令（替代 ContextMenu）。
    /// 任意热更脚本：DebugCmd.Reg("foo", "说明", () => { ... });
    /// </summary>
    public static class DebugCmd
    {
        public sealed class Entry
        {
            public string Name;
            public string Help;
            public Action Sync;
            public Func<string[], CancellationToken, UniTask> Async;
            public string Category;
        }

        static readonly Dictionary<string, Entry> _map =
            new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<Entry> All =>
            _map.Values.OrderBy(e => e.Category).ThenBy(e => e.Name).ToList();

        public static void Reg(string name, string help, Action action, string category = "General")
        {
            if (string.IsNullOrWhiteSpace(name) || action == null) return;
            _map[name.Trim()] = new Entry
            {
                Name = name.Trim(),
                Help = help ?? "",
                Sync = action,
                Category = category ?? "General"
            };
        }

        public static void RegAsync(string name, string help,
            Func<string[], CancellationToken, UniTask> action, string category = "General")
        {
            if (string.IsNullOrWhiteSpace(name) || action == null) return;
            _map[name.Trim()] = new Entry
            {
                Name = name.Trim(),
                Help = help ?? "",
                Async = action,
                Category = category ?? "General"
            };
        }

        public static bool TryGet(string name, out Entry entry) =>
            _map.TryGetValue(name?.Trim() ?? "", out entry);

        public static void Clear() => _map.Clear();

        public static async UniTask<(bool ok, string msg)> RunAsync(string line, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(line))
                return (false, "empty");

            var parts = SplitArgs(line);
            var name = parts[0];
            var args = parts.Length > 1 ? parts.Skip(1).ToArray() : Array.Empty<string>();

            if (!_map.TryGetValue(name, out var e))
                return (false, $"unknown cmd: {name}");

            try
            {
                if (e.Async != null)
                    await e.Async(args, ct);
                else
                    e.Sync?.Invoke();
                return (true, $"ok: {e.Name}");
            }
            catch (OperationCanceledException)
            {
                return (false, "canceled");
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return (false, ex.Message);
            }
        }

        static string[] SplitArgs(string line)
        {
            return line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        }
    }
}
