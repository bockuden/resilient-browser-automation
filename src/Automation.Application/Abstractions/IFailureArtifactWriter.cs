namespace Automation.Application.Abstractions;

public interface IFailureArtifactWriter
{
    Task CaptureAsync(string jobId, Exception error, CancellationToken cancellationToken);
}

