using System.Text.Json.Serialization;

namespace HorrorHarbor.Posters;

public sealed record TmdbSearchResponse(
    [property: JsonPropertyName("results")] TmdbMovieResult?[]? Results);

public sealed record TmdbMovieResult(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("original_title")] string? OriginalTitle,
    [property: JsonPropertyName("original_language")] string? OriginalLanguage,
    [property: JsonPropertyName("release_date")] string? ReleaseDate,
    [property: JsonPropertyName("poster_path")] string? PosterPath);

public sealed record TmdbMatch(
    TmdbMovieResult Movie,
    double Confidence,
    string Method);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip)]
[JsonSerializable(typeof(TmdbSearchResponse))]
[JsonSerializable(typeof(TmdbMovieResult))]
[JsonSerializable(typeof(PosterProvenance))]
internal sealed partial class PosterJsonContext : JsonSerializerContext;
