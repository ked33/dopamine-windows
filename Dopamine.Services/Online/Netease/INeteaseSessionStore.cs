using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public interface INeteaseSessionStore
    {
        Task<NeteaseSessionLoadResult> LoadAsync(CancellationToken cancellationToken);

        Task<NeteaseResult<bool>> SaveAsync(NeteaseSessionSnapshot snapshot, CancellationToken cancellationToken);

        Task DeleteAsync();
    }
}
