using HorrorHarbor.Core.Models;
using HorrorHarbor.Data.UserData;
using Microsoft.Data.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class UserDataStoreIntegrationTests
{
    [TestMethod]
    public async Task ImportPlexHistoryIsIdempotentAndBuildsExclusions()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HorrorHarbor.Tests", Guid.NewGuid().ToString("N"));
        string databasePath = Path.Combine(directory, "user-data.db");
        try
        {
            using var store = new SqliteUserDataStore(databasePath);
            await store.InitializeAsync();
            await store.SavePlexServerAsync(
                "primary",
                new PlexConnectionSettings("https://plex.example:32400"),
                new PlexServerIdentity("machine", "Test Plex", "1.0"));

            PlexHistoryItem[] history =
            {
                new("42", "movie", "Talk to Me", 2022, "plex://movie/42", DateTime.SpecifyKind(new DateTime(2026, 7, 1), DateTimeKind.Utc), 1_000, 5_000),
            };

            PlexSyncResult first = await store.ImportPlexHistoryAsync("primary", history);
            PlexSyncResult second = await store.ImportPlexHistoryAsync("primary", history);
            HashSet<string> keys = await store.GetPlexWatchedExclusionKeysAsync();

            Assert.AreEqual(1, first.ImportedCount);
            Assert.AreEqual(0, second.ImportedCount);
            Assert.AreEqual(1, second.DuplicateCount);
            Assert.IsTrue(keys.Contains("movie|talk to me|2022"));
            Assert.IsTrue(keys.Contains("movie|talk to me|*"));
            Assert.HasCount(1, await store.GetPlexHistoryAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task FeedbackCanBeRecordedAndUndone()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HorrorHarbor.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new SqliteUserDataStore(Path.Combine(directory, "user-data.db"));
            await store.InitializeAsync();
            await store.RecordFeedbackAsync("candidate-1", RecommendationFeedback.Interested, DateTime.UtcNow);

            RecommendationFeedback? undone = await store.UndoLatestFeedbackAsync("candidate-1");
            bool restored = await store.RestoreLatestFeedbackAsync(
                "candidate-1",
                RecommendationFeedback.Interested);
            RecommendationFeedback? undoneAgain = await store.UndoLatestFeedbackAsync("candidate-1");
            RecommendationFeedback? absent = await store.UndoLatestFeedbackAsync("candidate-1");

            Assert.AreEqual(RecommendationFeedback.Interested, undone);
            Assert.IsTrue(restored);
            Assert.AreEqual(RecommendationFeedback.Interested, undoneAgain);
            Assert.IsNull(absent);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task UserSettingRoundTripsAndUpdates()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HorrorHarbor.Tests", Guid.NewGuid().ToString("N"));
        try
        {
            using var store = new SqliteUserDataStore(Path.Combine(directory, "user-data.db"));
            await store.InitializeAsync();

            Assert.IsNull(await store.LoadSettingAsync("discovery.mood.v1"));
            await store.SaveSettingAsync("discovery.mood.v1", "4|3|4|0.75|False|False|12");
            Assert.AreEqual(
                "4|3|4|0.75|False|False|12",
                await store.LoadSettingAsync("discovery.mood.v1"));

            await store.SaveSettingAsync("discovery.mood.v1", "5|1|5|0.9|True|False|18");
            Assert.AreEqual(
                "5|1|5|0.9|True|False|18",
                await store.LoadSettingAsync("discovery.mood.v1"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }
}
