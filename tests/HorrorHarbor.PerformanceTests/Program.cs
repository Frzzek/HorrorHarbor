using System.Diagnostics;
using System.Text.Json;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Data.Catalog;
using HorrorHarbor.Recommendations.Engine;
using HorrorHarbor.Search;
using HorrorHarbor.Search.Contracts;
using HorrorHarbor.Search.Indexing;
using HorrorHarbor.Search.Normalization;
using HorrorHarbor.Search.Querying;

string catalogPath = args.FirstOrDefault() ?? Path.GetFullPath("data/catalog/recommendation_catalog.json");
string reportPath = args.Skip(1).FirstOrDefault() ?? Path.GetFullPath("artifacts/performance/performance-report.json");
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);

var catalog = await new JsonCandidateCatalog(catalogPath).LoadAsync();
RecommendationCandidate[] synthetic = Enumerable.Range(0, 50_000)
    .Select(index => catalog[index % catalog.Count] with
    {
        CandidateId = $"stress-{index:D6}",
        Title = $"Stress Candidate {index:D6}",
        ReleaseYear = 1900 + (index % 126),
    })
    .ToArray();
var profile = new TasteProfile(
    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Slasher / Killer"] = 1, ["Body Horror"] = 0.8 },
    new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase) { ["Saw"] = 1 },
    4, 4, 4, true, true, 0.85);
var engine = new DeterministicRecommendationEngine();
var exclusions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

_ = engine.Rank(synthetic.Take(500), profile, exclusions, 30);
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
long beforeBytes = GC.GetTotalAllocatedBytes(true);
var stopwatch = Stopwatch.StartNew();
IReadOnlyList<RecommendationResult> results = engine.Rank(synthetic, profile, exclusions, 30);
stopwatch.Stop();
long allocatedBytes = GC.GetTotalAllocatedBytes(true) - beforeBytes;

const double maximumMilliseconds = 8_000;
const long maximumAllocatedBytes = 512L * 1024 * 1024;
bool recommendationPassed = stopwatch.Elapsed.TotalMilliseconds <= maximumMilliseconds &&
    allocatedBytes <= maximumAllocatedBytes &&
    results.Count == 30;

string[] normalizerInputs =
[
    "You're Next",
    "Frontier(s)",
    "séance",
    "Noroi: The Curse",
    "  THE   WAILING  ",
    "ＳＡＷ Ⅵ",
    "𐐀 Horror",
    "l'esprit de l'escalier",
];
const int normalizerOperationCount = 500_000;
const double maximumNormalizerMilliseconds = 500;
const double maximumNormalizerBytesPerOperation = 400;
var normalizerMeasurement = MeasureNormalizer(normalizerInputs, normalizerOperationCount);
bool normalizerPassed = normalizerMeasurement.ElapsedMilliseconds <= maximumNormalizerMilliseconds &&
    normalizerMeasurement.BytesPerOperation <= maximumNormalizerBytesPerOperation;
HorrorSearchDocument[] searchDocuments = synthetic
    .Select(static candidate => new HorrorSearchDocument(
        candidate,
        "synthetic-50k-v1",
        SearchAvailabilityState.Unknown))
    .ToArray();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();
long searchBeforeBytes = GC.GetTotalAllocatedBytes(true);
var searchBuildWatch = Stopwatch.StartNew();
SearchIndexBuildResult searchBuild = new SearchIndexBuilder().Build(searchDocuments);
searchBuildWatch.Stop();
long searchBuildBytes = GC.GetTotalAllocatedBytes(true) - searchBeforeBytes;
double searchQueryMilliseconds = double.PositiveInfinity;
int searchResultCount = 0;
if (searchBuild is SearchIndexBuildSucceeded searchSucceeded)
{
    var searchStore = new SearchIndexStore();
    searchStore.Publish(searchSucceeded.Snapshot);
    var searchEngine = new HorrorSearchEngine(searchStore, new SearchQueryParser());
    var searchWatch = Stopwatch.StartNew();
    SearchResponse searchResponse = searchEngine.Execute(
        new SearchRequest(
            "stress year:>=2000 dread:>=3 -tag:comedy",
            30,
            new SearchUserContext()));
    searchWatch.Stop();
    searchQueryMilliseconds = searchWatch.Elapsed.TotalMilliseconds;
    searchResultCount = searchResponse is SearchSuccessResponse searchSuccess
        ? searchSuccess.Results.Length
        : 0;
}

const double maximumSearchBuildMilliseconds = 12_000;
const double maximumSearchQueryMilliseconds = 500;
const long maximumSearchBuildAllocatedBytes = 1_024L * 1024 * 1024;
bool searchPassed = searchBuild is SearchIndexBuildSucceeded &&
    searchBuildWatch.Elapsed.TotalMilliseconds <= maximumSearchBuildMilliseconds &&
    searchQueryMilliseconds <= maximumSearchQueryMilliseconds &&
    searchBuildBytes <= maximumSearchBuildAllocatedBytes &&
    searchResultCount == 30;
bool passed = recommendationPassed && normalizerPassed && searchPassed;
var report = new
{
    status = passed ? "PASS" : "FAIL",
    candidateCount = synthetic.Length,
    resultCount = results.Count,
    elapsedMilliseconds = Math.Round(stopwatch.Elapsed.TotalMilliseconds, 3),
    allocatedBytes,
    budgets = new { maximumMilliseconds, maximumAllocatedBytes },
    searchNormalizer = new
    {
        status = normalizerPassed ? "PASS" : "FAIL",
        inputCount = normalizerInputs.Length,
        operationCount = normalizerOperationCount,
        elapsedMilliseconds = Math.Round(normalizerMeasurement.ElapsedMilliseconds, 3),
        allocatedBytes = normalizerMeasurement.AllocatedBytes,
        bytesPerOperation = Math.Round(normalizerMeasurement.BytesPerOperation, 3),
        normalizerMeasurement.Checksum,
        budgets = new
        {
            maximumMilliseconds = maximumNormalizerMilliseconds,
            maximumBytesPerOperation = maximumNormalizerBytesPerOperation,
        },
    },
    horrorSearch = new
    {
        status = searchPassed ? "PASS" : "FAIL",
        documentCount = searchDocuments.Length,
        indexBuildMilliseconds = Math.Round(searchBuildWatch.Elapsed.TotalMilliseconds, 3),
        indexBuildAllocatedBytes = searchBuildBytes,
        queryMilliseconds = Math.Round(searchQueryMilliseconds, 3),
        resultCount = searchResultCount,
        budgets = new
        {
            maximumSearchBuildMilliseconds,
            maximumSearchQueryMilliseconds,
            maximumSearchBuildAllocatedBytes,
        },
    },
    timestampUtc = DateTime.UtcNow,
};
await File.WriteAllTextAsync(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(JsonSerializer.Serialize(report));
return passed ? 0 : 1;

static (double ElapsedMilliseconds, long AllocatedBytes, double BytesPerOperation, long Checksum) MeasureNormalizer(
    string[] inputs,
    int operationCount)
{
    const int WarmupOperationCount = 1_000_000;
    long checksum = 0;

    for (int index = 0; index < WarmupOperationCount; index++)
    {
        NormalizedSearchText result = SearchTextNormalizerV1.Normalize(inputs[index % inputs.Length]);
        checksum += result.Value.Length + result.Tokens.Length + (int)result.Changes;
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
    var stopwatch = Stopwatch.StartNew();
    for (int index = 0; index < operationCount; index++)
    {
        NormalizedSearchText result = SearchTextNormalizerV1.Normalize(inputs[index % inputs.Length]);
        checksum += result.Value.Length + result.Tokens.Length + (int)result.Changes;
    }

    stopwatch.Stop();
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
    return (
        stopwatch.Elapsed.TotalMilliseconds,
        allocatedBytes,
        (double)allocatedBytes / operationCount,
        checksum);
}
