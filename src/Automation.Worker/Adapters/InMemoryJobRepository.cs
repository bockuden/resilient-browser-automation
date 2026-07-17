using System.Collections.Concurrent;
using Automation.Application.Abstractions;
using Automation.Core.Results;

namespace Automation.Worker.Adapters;

public sealed class InMemoryJobRepository : IJobRepository
{
    private readonly ConcurrentDictionary<string, JobExecution> executions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<(string JobId, string ExternalId), CatalogItem> items = new();

    public Task<JobExecution?> FindAsync(string jobId, CancellationToken cancellationToken)
    {
        executions.TryGetValue(jobId, out var execution);
        return Task.FromResult(execution);
    }

    public Task MarkRunningAsync(string jobId, CancellationToken cancellationToken)
    {
        executions[jobId] = new JobExecution(jobId, JobStatus.Running, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task StoreItemsAsync(
        string jobId,
        IReadOnlyCollection<CatalogItem> values,
        CancellationToken cancellationToken)
    {
        foreach (var item in values)
        {
            items.TryAdd((jobId, item.ExternalId), item);
        }

        return Task.CompletedTask;
    }

    public Task MarkCompletedAsync(string jobId, CancellationToken cancellationToken)
    {
        executions[jobId] = new JobExecution(jobId, JobStatus.Completed, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }

    public Task MarkFailedAsync(
        string jobId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken)
    {
        executions[jobId] = new JobExecution(jobId, JobStatus.Failed, DateTimeOffset.UtcNow, errorCode, errorMessage);
        return Task.CompletedTask;
    }

    public Task MarkCancelledAsync(string jobId, CancellationToken cancellationToken)
    {
        executions[jobId] = new JobExecution(jobId, JobStatus.Cancelled, DateTimeOffset.UtcNow);
        return Task.CompletedTask;
    }
}

