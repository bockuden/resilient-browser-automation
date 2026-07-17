namespace Automation.Worker.Jobs;

public interface IJobSource
{
    IAsyncEnumerable<JobInputResult> ReadAllAsync(CancellationToken cancellationToken);
}

