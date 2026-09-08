using System.Threading;
using Cysharp.Threading.Tasks;

namespace HotUpdate.Network
{
    public interface IHttpClient
    {
        string AccessToken { get; set; }
        UniTask<string> GetAsync(string url, bool auth = false, CancellationToken ct = default);
        UniTask<string> PostJsonAsync(string url, string json, bool auth = false, CancellationToken ct = default);
        UniTask<string> PutJsonAsync(string url, string json, bool auth = true, CancellationToken ct = default);
    }
}
