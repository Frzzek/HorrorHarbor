using System.Diagnostics;

namespace HorrorHarbor.Data.UserData;

internal sealed class UserDataInitializationLock : IAsyncDisposable
{
    private static readonly TimeSpan AcquisitionTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromMilliseconds(50);

    private readonly FileStream _stream;

    private UserDataInitializationLock(FileStream stream)
    {
        _stream = stream;
    }

    public static async Task<UserDataInitializationLock> AcquireAsync(
        string databasePath,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        string lockPath = Path.GetFullPath(databasePath) + ".initialization.lock";
        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var stream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None,
                    bufferSize: 1,
                    FileOptions.Asynchronous |
                    FileOptions.DeleteOnClose |
                    FileOptions.WriteThrough);
                return new UserDataInitializationLock(stream);
            }
            catch (IOException) when (stopwatch.Elapsed < AcquisitionTimeout)
            {
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException exception)
            {
                throw new IOException(
                    "The user-data initialization lock could not be acquired safely.",
                    exception);
            }
        }
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
