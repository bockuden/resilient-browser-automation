using Automation.Application.Abstractions;

namespace Automation.Worker.Adapters;

public sealed class NoOpFailureArtifactWriter : IFailureArtifactWriter
{
    public Task CaptureAsync(string jobId, Exception error, CancellationToken cancellationToken) => Task.CompletedTask;
}

