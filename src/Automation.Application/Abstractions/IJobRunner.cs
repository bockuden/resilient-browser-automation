using Automation.Core.Jobs;

namespace Automation.Application.Abstractions;

public interface IJobRunner
{
    Task<JobRunResult> RunAsync(
        AutomationJob job,
        CancellationToken cancellationToken);
}
