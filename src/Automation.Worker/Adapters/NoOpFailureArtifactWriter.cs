using Automation.Application.Abstractions;

namespace Automation.Worker.Adapters;

public sealed class NoOpFailureArtifactWriter : IFailureArtifactWriter
{
    public Task<string?> CaptureAsync(string jobId, Exception error, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
}
