using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace HotUpdate.Services
{
    public interface IResService
    {
        UniTask InitializeAsync(CancellationToken ct = default);
        UniTask<T> LoadAsync<T>(string bundle, string asset, CancellationToken ct = default) where T : Object;
        UniTask<GameObject> LoadUIAsync(string name, CancellationToken ct = default);
    }
}
