using System.Text.Json;
using HorrorHarbor.Core.Abstractions;
using HorrorHarbor.Core.Models;
using HorrorHarbor.Core.Validation;

namespace HorrorHarbor.Data.Feedback;

public sealed class JsonFeedbackStore : IFeedbackStore, IDisposable
{
    private const long MaximumFeedbackFileBytes = 2 * 1024 * 1024;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = false,
    };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public JsonFeedbackStore()
        : this(ResolveDefaultPath())
    {
    }

    public JsonFeedbackStore(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        string? directory = Path.GetDirectoryName(_path);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Feedback path must include a directory.", nameof(path));
        }

        Directory.CreateDirectory(directory);
    }

    public async Task<IReadOnlyDictionary<string, RecommendationFeedback>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        string candidateId,
        RecommendationFeedback feedback,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        CandidateIdValidator.Validate(candidateId, nameof(candidateId));
        if (!Enum.IsDefined(feedback))
        {
            throw new ArgumentOutOfRangeException(nameof(feedback), feedback, "Feedback value is not supported.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        string? temporaryPath = null;
        try
        {
            var values = new Dictionary<string, RecommendationFeedback>(
                await LoadUnsafeAsync(cancellationToken).ConfigureAwait(false),
                StringComparer.OrdinalIgnoreCase);

            if (feedback == RecommendationFeedback.None)
            {
                values.Remove(candidateId);
            }
            else
            {
                values[candidateId] = feedback;
            }

            temporaryPath = _path + $".tmp-{Guid.NewGuid():N}";
            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    values,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _path, overwrite: true);
            temporaryPath = null;
        }
        finally
        {
            if (temporaryPath is not null)
            {
                File.Delete(temporaryPath);
            }

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

    private async Task<IReadOnlyDictionary<string, RecommendationFeedback>> LoadUnsafeAsync(
        CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return Empty();
        }

        var file = new FileInfo(_path);
        if (file.Length is < 0 or > MaximumFeedbackFileBytes)
        {
            Quarantine();
            return Empty();
        }

        try
        {
            await using FileStream stream = new(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                16 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            Dictionary<string, RecommendationFeedback>? values =
                await JsonSerializer.DeserializeAsync<Dictionary<string, RecommendationFeedback>>(
                    stream,
                    SerializerOptions,
                    cancellationToken).ConfigureAwait(false);

            if (values is null)
            {
                return Empty();
            }

            foreach ((string candidateId, RecommendationFeedback feedback) in values)
            {
                CandidateIdValidator.Validate(candidateId, nameof(candidateId));
                if (!Enum.IsDefined(feedback))
                {
                    throw new JsonException("Feedback file contains an unsupported feedback value.");
                }
            }

            return new Dictionary<string, RecommendationFeedback>(values, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        {
            Quarantine();
            return Empty();
        }
    }

    private void Quarantine()
    {
        if (!File.Exists(_path))
        {
            return;
        }

        string quarantinePath = _path + $".corrupt-{DateTime.UtcNow:yyyyMMddHHmmssfff}-{Guid.NewGuid():N}";
        File.Move(_path, quarantinePath, overwrite: false);
    }

    private static Dictionary<string, RecommendationFeedback> Empty() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static string ResolveDefaultPath()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HorrorHarbor");
        return Path.Combine(directory, "recommendation_feedback.json");
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
