using System.Text.Json;
using HorrorHarbor.Search.Querying;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.UnitTests;

[TestClass]
public sealed class SearchAcceptanceCorpusParserTests
{
    private static readonly SearchQueryParser Parser = new();

    [TestMethod]
    public void Parse_MatchesEveryDeclaredAcceptanceCorpusOutcome()
    {
        string fixturePath = Path.Combine(
            AppContext.BaseDirectory,
            "fixtures",
            "search",
            "horror-search-acceptance-v1.json");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(fixturePath));
        JsonElement root = document.RootElement;

        Assert.AreEqual(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(5, root.GetProperty("corpusRevision").GetInt32());

        JsonElement cases = root.GetProperty("cases");
        Assert.AreEqual(48, cases.GetArrayLength());
        foreach (JsonElement acceptanceCase in cases.EnumerateArray())
        {
            string caseId = acceptanceCase.GetProperty("id").GetString()!;
            string queryText = acceptanceCase.GetProperty("query").GetString()!;
            string expectedOutcome = acceptanceCase.GetProperty("expectedOutcome").GetString()!;
            SearchQueryParseResult result = Parser.Parse(queryText);

            switch (expectedOutcome)
            {
                case "success":
                    AssertSuccessfulCase(acceptanceCase, result, caseId);
                    break;
                case "invalid-query":
                    AssertRejectedCase(acceptanceCase, result, caseId);
                    break;
                default:
                    Assert.Fail($"Acceptance case '{caseId}' has unknown outcome '{expectedOutcome}'.");
                    break;
            }
        }
    }

    private static void AssertSuccessfulCase(
        JsonElement acceptanceCase,
        SearchQueryParseResult result,
        string caseId)
    {
        SearchQueryParsed parsed = Assert.IsInstanceOfType<SearchQueryParsed>(
            result,
            $"Acceptance case '{caseId}' should parse successfully.");
        string expectedIntent = acceptanceCase.GetProperty("expectedIntent").GetString()!;
        SearchIntent intent = expectedIntent switch
        {
            "local-search" => SearchIntent.LocalSearch,
            "filtered-discovery" => SearchIntent.FilteredDiscovery,
            _ => throw new AssertFailedException(
                $"Acceptance case '{caseId}' has unknown intent '{expectedIntent}'."),
        };

        Assert.AreEqual(intent, parsed.Query.Intent, $"Acceptance case '{caseId}' has the wrong intent.");
    }

    private static void AssertRejectedCase(
        JsonElement acceptanceCase,
        SearchQueryParseResult result,
        string caseId)
    {
        SearchQueryRejected rejected = Assert.IsInstanceOfType<SearchQueryRejected>(
            result,
            $"Acceptance case '{caseId}' should be rejected.");
        string expectedCode = acceptanceCase.GetProperty("expectedDiagnosticCode").GetString()!;

        Assert.AreEqual(
            expectedCode,
            rejected.Diagnostic.StableCode,
            $"Acceptance case '{caseId}' returned the wrong diagnostic.");
    }
}
