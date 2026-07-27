using Microsoft.VisualStudio.TestTools.UnitTesting;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Data.Catalog;
using HorrorHarbor.Data.Feedback;

namespace HorrorHarbor.SecurityTests;

[TestClass]
public sealed class MaliciousInputTests
{
    [TestMethod]
    [DataRow("catalog-duplicate-id.json")]
    [DataRow("catalog-invalid-intensity.json")]
    [DataRow("catalog-unknown-property.json")]
    [DataRow("catalog-malformed.json")]
    public async Task Catalog_RejectsMaliciousFixtures(string fixture)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "fixtures", "malicious", fixture);
        var catalog = new JsonCandidateCatalog(path);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            await catalog.LoadAsync();
        });
    }

    [TestMethod]
    public async Task FeedbackStore_RejectsTraversalLikeCandidateId()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HorrorHarborSecurityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var store = new JsonFeedbackStore(Path.Combine(directory, "feedback.json"));
        await Assert.ThrowsAsync<ArgumentException>(async () =>
        {
            await store.SaveAsync("../escape", RecommendationFeedback.Watched);
        });
    }

    [TestMethod]
    public async Task FeedbackStore_QuarantinesCorruptDataAndContinuesSafely()
    {
        string directory = Path.Combine(Path.GetTempPath(), "HorrorHarborSecurityTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "feedback.json");
        await File.WriteAllTextAsync(path, await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "malicious", "feedback-corrupt.json")));

        using var store = new JsonFeedbackStore(path);
        IReadOnlyDictionary<string, RecommendationFeedback> values = await store.LoadAsync();

        Assert.AreEqual(0, values.Count);
        Assert.IsFalse(File.Exists(path));
        Assert.AreEqual(1, Directory.GetFiles(directory, "feedback.json.corrupt-*").Length);
    }
}
