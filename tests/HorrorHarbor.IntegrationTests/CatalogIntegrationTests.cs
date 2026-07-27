using Microsoft.VisualStudio.TestTools.UnitTesting;
using HorrorHarbor.Data.Catalog;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class CatalogIntegrationTests
{
    [TestMethod]
    public async Task CanonicalCatalog_LoadsAndHasUniqueIdentities()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "data", "catalog", "recommendation_catalog.json");
        var catalog = new JsonCandidateCatalog(path);
        var candidates = await catalog.LoadAsync();
        Assert.AreEqual(116, candidates.Count);
        Assert.AreEqual(116, candidates.Select(static candidate => candidate.CandidateId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
