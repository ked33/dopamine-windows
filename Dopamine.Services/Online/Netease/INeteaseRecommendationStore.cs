using System.Threading;
using System.Threading.Tasks;

namespace Dopamine.Services.Online.Netease
{
    public interface INeteaseRecommendationStore
    {
        Task<NeteaseRecommendationLoadResult> LoadAsync(CancellationToken cancellationToken);

        Task<NeteaseResult<bool>> SaveAsync(
            NeteaseRecommendationSnapshot snapshot,
            CancellationToken cancellationToken);

        Task DeleteAsync();
    }
}
