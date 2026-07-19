namespace Automation.Application.Abstractions;

public interface IFailureArtifactWriter
{
    Task<string?> CaptureAsync(string jobId, Exception error, CancellationToken cancellationToken);
}
