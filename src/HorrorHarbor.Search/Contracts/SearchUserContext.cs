using System.Collections.Frozen;
using HorrorHarbor.Core.Validation;
using HorrorHarbor.Search.Querying;

namespace HorrorHarbor.Search.Contracts;

public sealed class SearchUserContext
{
    private readonly FrozenDictionary<string, SearchLocalStatus> _statuses;
    private readonly FrozenDictionary<string, double> _affinities;

    public SearchUserContext(
        IReadOnlyDictionary<string, SearchLocalStatus>? statuses = null,
        IReadOnlyDictionary<string, double>? affinities = null)
    {
        _statuses = CopyStatuses(statuses);
        _affinities = CopyAffinities(affinities);
    }

    public SearchLocalStatus GetStatus(string candidateId)
    {
        CandidateIdValidator.Validate(candidateId, nameof(candidateId));
        return GetStatusUnchecked(candidateId);
    }

    public double GetAffinity(string candidateId)
    {
        CandidateIdValidator.Validate(candidateId, nameof(candidateId));
        return GetAffinityUnchecked(candidateId);
    }

    internal SearchLocalStatus GetStatusUnchecked(string candidateId) =>
        _statuses.TryGetValue(candidateId, out SearchLocalStatus status)
            ? status
            : SearchLocalStatus.Unseen;

    internal double GetAffinityUnchecked(string candidateId) =>
        _affinities.TryGetValue(candidateId, out double affinity)
            ? affinity
            : 0;

    private static FrozenDictionary<string, SearchLocalStatus> CopyStatuses(
        IReadOnlyDictionary<string, SearchLocalStatus>? statuses)
    {
        if (statuses is null || statuses.Count == 0)
        {
            return FrozenDictionary<string, SearchLocalStatus>.Empty;
        }

        var copy = new Dictionary<string, SearchLocalStatus>(
            statuses.Count,
            StringComparer.Ordinal);
        foreach ((string candidateId, SearchLocalStatus status) in statuses)
        {
            CandidateIdValidator.Validate(candidateId, nameof(statuses));
            if (!Enum.IsDefined(status))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(statuses),
                    status,
                    "Unknown local search status.");
            }

            copy.Add(candidateId, status);
        }

        return copy.ToFrozenDictionary(StringComparer.Ordinal);
    }

    private static FrozenDictionary<string, double> CopyAffinities(
        IReadOnlyDictionary<string, double>? affinities)
    {
        if (affinities is null || affinities.Count == 0)
        {
            return FrozenDictionary<string, double>.Empty;
        }

        var copy = new Dictionary<string, double>(
            affinities.Count,
            StringComparer.Ordinal);
        foreach ((string candidateId, double affinity) in affinities)
        {
            CandidateIdValidator.Validate(candidateId, nameof(affinities));
            if (!double.IsFinite(affinity) || affinity is < 0 or > 100)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(affinities),
                    affinity,
                    "Search affinity must be finite and between 0 and 100.");
            }

            copy.Add(candidateId, affinity);
        }

        return copy.ToFrozenDictionary(StringComparer.Ordinal);
    }
}
