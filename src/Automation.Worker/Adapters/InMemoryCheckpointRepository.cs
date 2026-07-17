using System.Collections.Concurrent;
using Automation.Application.Abstractions;
using Automation.Core.Checkpoints;

namespace Automation.Worker.Adapters;

public sealed class InMemoryCheckpointRepository : ICheckpointRepository
{
    private readonly ConcurrentDictionary<string, JobCheckpoint> checkpoints = new(StringComparer.Ordinal);

    public Task<JobCheckpoint?> FindAsync(string jobId, CancellationToken cancellationToken)
    {
        checkpoints.TryGetValue(jobId, out var checkpoint);
        return Task.FromResult(checkpoint);
    }

    public Task SaveAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        checkpoints[checkpoint.JobId] = checkpoint;
        return Task.CompletedTask;
    }
}

