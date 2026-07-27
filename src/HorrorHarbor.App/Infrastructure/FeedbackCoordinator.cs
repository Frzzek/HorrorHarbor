using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.Validation;

namespace HorrorHarbor.App.Infrastructure;

public sealed record FeedbackChange(
    string CandidateId,
    RecommendationFeedback Previous,
    RecommendationFeedback Applied);

public sealed record FeedbackUndoResult(
    bool WasUndone,
    RecommendationFeedback Current);

public interface IFeedbackCoordinator
{
    Task<FeedbackChange> ApplyAsync(
        string candidateId,
        RecommendationFeedback previous,
        RecommendationFeedback feedback,
        CancellationToken cancellationToken = default);

    Task<FeedbackUndoResult> UndoAsync(
        FeedbackChange change,
        CancellationToken cancellationToken = default);
}

public sealed class FeedbackCoordinator(
    IFeedbackStore feedbackStore,
    IUserDataStore userDataStore,
    TimeProvider timeProvider) : IFeedbackCoordinator, IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public async Task<FeedbackChange> ApplyAsync(
        string candidateId,
        RecommendationFeedback previous,
        RecommendationFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Validate(candidateId, previous, feedback);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await feedbackStore.SaveAsync(candidateId, feedback, cancellationToken).ConfigureAwait(false);
            try
            {
                await userDataStore.RecordFeedbackAsync(
                    candidateId,
                    feedback,
                    timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception persistenceFailure)
            {
                await CompensateAsync(
                    () => feedbackStore.SaveAsync(candidateId, previous, CancellationToken.None),
                    persistenceFailure).ConfigureAwait(false);
                throw;
            }

            return new FeedbackChange(candidateId, previous, feedback);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<FeedbackUndoResult> UndoAsync(
        FeedbackChange change,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(change);
        Validate(change.CandidateId, change.Previous, change.Applied);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RecommendationFeedback? undone = await userDataStore.UndoLatestFeedbackAsync(
                change.CandidateId,
                cancellationToken).ConfigureAwait(false);
            if (undone is null)
            {
                return new FeedbackUndoResult(false, change.Applied);
            }

            if (undone != change.Applied)
            {
                await RestoreEventOrThrowAsync(change.CandidateId, undone.Value).ConfigureAwait(false);
                return new FeedbackUndoResult(false, change.Applied);
            }

            try
            {
                await feedbackStore.SaveAsync(
                    change.CandidateId,
                    change.Previous,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception persistenceFailure)
            {
                await CompensateAsync(
                    () => RestoreEventOrThrowAsync(change.CandidateId, change.Applied),
                    persistenceFailure).ConfigureAwait(false);
                throw;
            }

            return new FeedbackUndoResult(true, change.Previous);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    private static void Validate(
        string candidateId,
        RecommendationFeedback previous,
        RecommendationFeedback feedback)
    {
        CandidateIdValidator.Validate(candidateId, nameof(candidateId));
        if (!Enum.IsDefined(previous))
        {
            throw new ArgumentOutOfRangeException(nameof(previous));
        }

        if (!Enum.IsDefined(feedback) || feedback == RecommendationFeedback.None)
        {
            throw new ArgumentOutOfRangeException(nameof(feedback));
        }
    }

    private async Task RestoreEventOrThrowAsync(
        string candidateId,
        RecommendationFeedback feedback)
    {
        bool restored = await userDataStore.RestoreLatestFeedbackAsync(
            candidateId,
            feedback,
            CancellationToken.None).ConfigureAwait(false);
        if (!restored)
        {
            throw new InvalidOperationException("The feedback event could not be restored.");
        }
    }

    private static async Task CompensateAsync(
        Func<Task> compensation,
        Exception persistenceFailure)
    {
        try
        {
            await compensation().ConfigureAwait(false);
        }
        catch (Exception compensationFailure)
        {
            throw new AggregateException(
                "Feedback persistence and its consistency compensation both failed.",
                persistenceFailure,
                compensationFailure);
        }
    }
}
