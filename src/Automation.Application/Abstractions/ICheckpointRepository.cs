using Automation.Core.Checkpoints;

namespace Automation.Application.Abstractions;

public interface ICheckpointRepository
{
    Task<JobCheckpoint?> FindAsync(string jobId, CancellationToken cancellationToken);
    Task SaveAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken);
}

