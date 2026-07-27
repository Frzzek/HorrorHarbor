namespace HorrorHarbor.App.Infrastructure;

public interface ISecureErrorReporter
{
    Task<string> ReportAsync(
        Exception exception,
        string operation,
        CancellationToken cancellationToken = default);
}
