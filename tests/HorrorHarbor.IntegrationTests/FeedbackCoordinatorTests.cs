using HorrorHarbor.App.Infrastructure;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Data.Feedback;
using HorrorHarbor.Data.UserData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class FeedbackCoordinatorTests
{
    [TestMethod]
    public async Task ApplyAndUndo_PublishConsistentCurrentStateAndHistory()
    {
        string directory = CreateDirectory();
        try
        {
            using var feedbackStore = new JsonFeedbackStore(Path.Combine(directory, "feedback.json"));
            using var keyProvider = new TestUserDataEncryptionKeyProvider();
            using var userDataStore = new SqliteUserDataStore(
                Path.Combine(directory, "user-data.db"),
                keyProvider);
            await userDataStore.InitializeAsync();
            var coordinator = new FeedbackCoordinator(
                feedbackStore,
                userDataStore,
                new FixedTimeProvider(new DateTimeOffset(2026, 7, 26, 20, 0, 0, TimeSpan.Zero)));

            FeedbackChange change = await coordinator.ApplyAsync(
                "candidate-1",
                RecommendationFeedback.None,
                RecommendationFeedback.Interested);
            FeedbackUndoResult undo = await coordinator.UndoAsync(change);

            Assert.IsTrue(undo.WasUndone);
            Assert.AreEqual(RecommendationFeedback.None, undo.Current);
            Assert.IsFalse((await feedbackStore.LoadAsync()).ContainsKey("candidate-1"));
            Assert.IsNull(await userDataStore.UndoLatestFeedbackAsync("candidate-1"));
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    [TestMethod]
    public async Task Undo_WhenCurrentStateWriteFails_RestoresHistoryEvent()
    {
        string directory = CreateDirectory();
        try
        {
            using var innerStore = new JsonFeedbackStore(Path.Combine(directory, "feedback.json"));
            using var keyProvider = new TestUserDataEncryptionKeyProvider();
            using var userDataStore = new SqliteUserDataStore(
                Path.Combine(directory, "user-data.db"),
                keyProvider);
            await userDataStore.InitializeAsync();
            var store = new FailOnSaveFeedbackStore(innerStore, failOnSave: 2);
            var coordinator = new FeedbackCoordinator(store, userDataStore, TimeProvider.System);
            FeedbackChange change = await coordinator.ApplyAsync(
                "candidate-1",
                RecommendationFeedback.None,
                RecommendationFeedback.Dislike);

            await Assert.ThrowsAsync<IOException>(() => coordinator.UndoAsync(change));

            Assert.AreEqual(
                RecommendationFeedback.Dislike,
                await userDataStore.UndoLatestFeedbackAsync("candidate-1"));
            Assert.AreEqual(
                RecommendationFeedback.Dislike,
                (await innerStore.LoadAsync())["candidate-1"]);
        }
        finally
        {
            DeleteDirectory(directory);
        }
    }

    private static string CreateDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "HorrorHarbor.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void DeleteDirectory(string directory)
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => value;
    }

    private sealed class FailOnSaveFeedbackStore(
        IFeedbackStore inner,
        int failOnSave) : IFeedbackStore
    {
        private int _saveCount;

        public Task<IReadOnlyDictionary<string, RecommendationFeedback>> LoadAsync(
            CancellationToken cancellationToken = default) => inner.LoadAsync(cancellationToken);

        public Task SaveAsync(
            string candidateId,
            RecommendationFeedback feedback,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _saveCount) == failOnSave)
            {
                throw new IOException("Injected feedback persistence failure.");
            }

            return inner.SaveAsync(candidateId, feedback, cancellationToken);
        }
    }
}
