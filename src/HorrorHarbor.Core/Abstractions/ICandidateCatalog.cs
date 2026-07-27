using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Core.Abstractions;

public interface ICandidateCatalog
{
    Task<IReadOnlyList<RecommendationCandidate>> LoadAsync(
        CancellationToken cancellationToken = default);
}
