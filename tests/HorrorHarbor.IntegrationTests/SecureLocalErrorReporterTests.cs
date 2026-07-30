using System.Text.Json;
using HorrorHarbor.App.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace HorrorHarbor.IntegrationTests;

[TestClass]
public sealed class SecureLocalErrorReporterTests
{
    [TestMethod]
    public async Task ReportRedactsCredentialsUrlsPathsAndControlCharacters()
    {
        string directory = CreateTestDirectory();
        string logPath = Path.Combine(directory, "errors.jsonl");
        string bearerSecret = "SYNTHETIC" + "-BEARER-" + new string('A', 24);
        string plexSecret = "SYNTHETIC" + "-PLEX-" + new string('B', 24);
        string namedSecret = "SYNTHETIC" + "-TOKEN-" + new string('C', 24);
        string passwordSecret = "SYNTHETIC" + "-PASSWORD-" + new string('D', 24);
        string urlSecret = "SYNTHETIC" + "-QUERY-" + new string('E', 24);
        string windowsPath = Path.Combine(
            $"C:{Path.DirectorySeparatorChar}",
            "Users",
            "SyntheticUser",
            "private",
            "history.db");
        try
        {
            using var reporter = new SecureLocalErrorReporter(logPath);
            string message = string.Join(
                ' ',
                $"Authorization: Bearer {bearerSecret}",
                $"X-Plex-Token: {plexSecret}",
                $"token={namedSecret}",
                $"password:{passwordSecret}",
                $"https://example.invalid/private?access_token={urlSecret}",
                windowsPath,
                "forged\r\nrecord");

            string reference = await reporter.ReportAsync(
                new InvalidOperationException(message),
                "provider\noperation");

            Assert.AreEqual(16, reference.Length);
            string[] lines = await File.ReadAllLinesAsync(logPath);
            Assert.HasCount(1, lines);
            using JsonDocument document = JsonDocument.Parse(lines[0]);
            JsonElement root = document.RootElement;
            string loggedMessage = root.GetProperty("message").GetString()!;
            string loggedOperation = root.GetProperty("operation").GetString()!;

            foreach (string secret in new[]
            {
                bearerSecret,
                plexSecret,
                namedSecret,
                passwordSecret,
                urlSecret,
            })
            {
                Assert.DoesNotContain(secret, lines[0], StringComparison.Ordinal);
            }

            Assert.DoesNotContain("example.invalid", loggedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("SyntheticUser", loggedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("[redacted]", loggedMessage, StringComparison.Ordinal);
            Assert.Contains("[url]", loggedMessage, StringComparison.Ordinal);
            Assert.Contains("[path]", loggedMessage, StringComparison.Ordinal);
            Assert.DoesNotContain('\r', loggedMessage);
            Assert.DoesNotContain('\n', loggedMessage);
            Assert.DoesNotContain('\r', loggedOperation);
            Assert.DoesNotContain('\n', loggedOperation);
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    [TestMethod]
    public async Task ReportRotationBoundsFileCountAndFileSize()
    {
        const int maximumFileBytes = 4096;
        const int retainedRotations = 2;
        string directory = CreateTestDirectory();
        string logPath = Path.Combine(directory, "errors.jsonl");
        try
        {
            using var reporter = new SecureLocalErrorReporter(
                logPath,
                maximumFileBytes,
                retainedRotations);
            string message = new('X', 500);
            for (var index = 0; index < 30; index++)
            {
                await reporter.ReportAsync(
                    new InvalidOperationException($"{index:D2}-{message}"),
                    "rotation-test");
            }

            string[] files = Directory.GetFiles(directory, "errors.jsonl*");
            Assert.HasCount(retainedRotations + 1, files);
            Assert.IsTrue(File.Exists(logPath));
            Assert.IsTrue(File.Exists(logPath + ".1"));
            Assert.IsTrue(File.Exists(logPath + ".2"));
            Assert.IsFalse(File.Exists(logPath + ".3"));
            foreach (string file in files)
            {
                Assert.IsLessThanOrEqualTo(maximumFileBytes, new FileInfo(file).Length);
                foreach (string line in await File.ReadAllLinesAsync(file))
                {
                    using JsonDocument document = JsonDocument.Parse(line);
                    Assert.AreEqual("rotation-test", document.RootElement.GetProperty("operation").GetString());
                }
            }
        }
        finally
        {
            DeleteTestDirectory(directory);
        }
    }

    private static string CreateTestDirectory() => Path.Combine(
        Path.GetTempPath(),
        "HorrorHarbor.Tests",
        Guid.NewGuid().ToString("N"));

    private static void DeleteTestDirectory(string directory)
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
