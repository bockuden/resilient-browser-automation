using Automation.Core.Checkpoints;
using Automation.Core.Results;

namespace Automation.Application.Abstractions;

public interface IJobPageCommitter
{
    Task CommitPageAsync(
        string jobId,
        IReadOnlyCollection<CatalogItem> items,
        JobCheckpoint checkpoint,
        CancellationToken cancellationToken);
}
