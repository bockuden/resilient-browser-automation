using Automation.Core.Results;

namespace Automation.Application.Abstractions;

public interface IJobRepository
{
    Task<JobExecution?> FindAsync(string jobId, CancellationToken cancellationToken);

    Task MarkRunningAsync(string jobId, CancellationToken cancellationToken);

    Task StoreItemsAsync(
        string jobId,
        IReadOnlyCollection<CatalogItem> items,
        CancellationToken cancellationToken);

    Task MarkCompletedAsync(string jobId, CancellationToken cancellationToken);

    Task MarkFailedAsync(
        string jobId,
        string errorCode,
        string errorMessage,
        CancellationToken cancellationToken);

    Task MarkCancelledAsync(string jobId, CancellationToken cancellationToken);
}

