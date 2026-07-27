using HorrorHarbor.Core.Models;

namespace HorrorHarbor.Core.Abstractions;

public interface IFeedbackStore
{
    Task<IReadOnlyDictionary<string, RecommendationFeedback>> LoadAsync(
        CancellationToken cancellationToken = default);

    Task SaveAsync(
        string candidateId,
        RecommendationFeedback feedback,
        CancellationToken cancellationToken = default);
}
