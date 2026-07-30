using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HorrorHarbor.App.Infrastructure;

public sealed partial class SecureLocalErrorReporter : ISecureErrorReporter, IDisposable
{
    private const int MaximumMessageLength = 500;
    private const int DefaultMaximumFileBytes = 256 * 1024;
    private const int DefaultRetainedFileCount = 3;
    private readonly string _logPath;
    private readonly int _maximumFileBytes;
    private readonly int _retainedFileCount;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SecureLocalErrorReporter()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HorrorHarbor",
            "logs",
            "errors.jsonl"))
    {
    }

    public SecureLocalErrorReporter(
        string logPath,
        int maximumFileBytes = DefaultMaximumFileBytes,
        int retainedFileCount = DefaultRetainedFileCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logPath);
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumFileBytes, 4096);
        ArgumentOutOfRangeException.ThrowIfLessThan(retainedFileCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(retainedFileCount, 10);

        _logPath = Path.GetFullPath(logPath);
        _maximumFileBytes = maximumFileBytes;
        _retainedFileCount = retainedFileCount;
        string? directory = Path.GetDirectoryName(_logPath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("The log path must include a directory.", nameof(logPath));
        }

        Directory.CreateDirectory(directory);
    }

    public async Task<string> ReportAsync(
        Exception exception,
        string operation,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        string reference = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        var entry = new
        {
            timestampUtc = DateTime.UtcNow,
            reference,
            operation = Sanitize(operation),
            exceptionType = exception.GetType().FullName,
            message = Sanitize(exception.Message),
            hResult = exception.HResult,
        };

        string line = JsonSerializer.Serialize(entry) + Environment.NewLine;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RotateIfRequired(Encoding.UTF8.GetByteCount(line));
            await File.AppendAllTextAsync(_logPath, line, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        return reference;
    }

    private static string Sanitize(string value)
    {
        string sanitized = ControlCharacterRegex().Replace(value, " ");
        sanitized = AuthorizationRegex().Replace(sanitized, "$1: [redacted]");
        sanitized = NamedSecretRegex().Replace(sanitized, "$1=[redacted]");
        sanitized = BearerTokenRegex().Replace(sanitized, "Bearer [redacted]");
        sanitized = UrlRegex().Replace(sanitized, "[url]");
        sanitized = WindowsPathRegex().Replace(sanitized, "[path]");
        sanitized = UnixPathRegex().Replace(sanitized, "[path]");
        sanitized = WhitespaceRegex().Replace(sanitized, " ").Trim();
        return sanitized.Length <= MaximumMessageLength
            ? sanitized
            : sanitized[..MaximumMessageLength];
    }

    private void RotateIfRequired(int pendingBytes)
    {
        if (!File.Exists(_logPath) || new FileInfo(_logPath).Length + pendingBytes <= _maximumFileBytes)
        {
            return;
        }

        string oldestPath = GetRotatedPath(_retainedFileCount);
        File.Delete(oldestPath);
        for (int index = _retainedFileCount - 1; index >= 1; index--)
        {
            string source = GetRotatedPath(index);
            if (File.Exists(source))
            {
                File.Move(source, GetRotatedPath(index + 1));
            }
        }

        File.Move(_logPath, GetRotatedPath(1));
    }

    private string GetRotatedPath(int index) => $"{_logPath}.{index}";

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Dispose();
    }

    [GeneratedRegex("""[A-Za-z]:\\[^\s"']+""")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex("""(?<!:)\/(?:[^\s"']+\/)*[^\s"']+""")]
    private static partial Regex UnixPathRegex();

    [GeneratedRegex("""(?i)\b(Authorization|X-Plex-Token)\s*:\s*(?:(?:Bearer|Basic)\s+)?[^\s,;]+""")]
    private static partial Regex AuthorizationRegex();

    [GeneratedRegex("""(?i)\b(api[_-]?key|access[_-]?token|refresh[_-]?token|client[_-]?secret|password|passwd|token)\b\s*[:=]\s*[^\s,;]+""")]
    private static partial Regex NamedSecretRegex();

    [GeneratedRegex("""(?i)\bBearer\s+[A-Za-z0-9._~+\/-]+=*""")]
    private static partial Regex BearerTokenRegex();

    [GeneratedRegex("""(?i)\b(?:https?|ftp):\/\/[^\s"'<>]+""")]
    private static partial Regex UrlRegex();

    [GeneratedRegex("""[\u0000-\u001F\u007F]""")]
    private static partial Regex ControlCharacterRegex();

    [GeneratedRegex("""\s+""")]
    private static partial Regex WhitespaceRegex();
}
