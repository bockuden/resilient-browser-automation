namespace Automation.Application.Abstractions;

public interface IFailureEvidenceCollector
{
    Task CaptureFailureEvidenceAsync(string directoryPath, CancellationToken cancellationToken);
}
