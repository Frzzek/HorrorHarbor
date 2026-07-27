using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HorrorHarbor.App.Infrastructure;

public sealed partial class SecureLocalErrorReporter : ISecureErrorReporter, IDisposable
{
    private const int MaximumMessageLength = 500;
    private readonly string _logPath;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public SecureLocalErrorReporter()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "HorrorHarbor",
            "logs");
        Directory.CreateDirectory(directory);
        _logPath = Path.Combine(directory, "errors.jsonl");
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
        string sanitized = WindowsPathRegex().Replace(value, "[path]");
        sanitized = UnixPathRegex().Replace(sanitized, "[path]");
        sanitized = sanitized.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return sanitized.Length <= MaximumMessageLength
            ? sanitized
            : sanitized[..MaximumMessageLength];
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

    [GeneratedRegex("""[A-Za-z]:\\[^\s"']+""")]
    private static partial Regex WindowsPathRegex();

    [GeneratedRegex("""(?<!:)\/(?:[^\s"']+\/)*[^\s"']+""")]
    private static partial Regex UnixPathRegex();
}
