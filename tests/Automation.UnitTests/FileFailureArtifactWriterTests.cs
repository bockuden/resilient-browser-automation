using Automation.Worker.Artifacts;
using Automation.Worker.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automation.UnitTests;

[TestFixture]
public sealed class FileFailureArtifactWriterTests
{
    [Test]
    public async Task CaptureAsync_WritesAtomicRedactedMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), $"automation-artifacts-{Guid.NewGuid():N}");
        try
        {
            var writer = new FileFailureArtifactWriter(
                new ArtifactOptions { RootPath = root, RetentionDays = 1, MaximumTotalSizeMegabytes = 1 },
                NullLogger<FileFailureArtifactWriter>.Instance);

            var directory = await writer.CaptureAsync(
                "job/with unsafe chars",
                new InvalidOperationException("password=super-secret token=another-secret"),
                CancellationToken.None);

            var errorJson = await File.ReadAllTextAsync(Path.Combine(directory!, "error.json"));
            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(Path.Combine(directory!, "error.json")), Is.True);
                Assert.That(File.Exists(Path.Combine(directory!, "error.json.tmp")), Is.False);
                Assert.That(errorJson, Does.Not.Contain("super-secret"));
                Assert.That(errorJson, Does.Not.Contain("another-secret"));
                Assert.That(errorJson, Does.Contain("[REDACTED]"));
            });
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
