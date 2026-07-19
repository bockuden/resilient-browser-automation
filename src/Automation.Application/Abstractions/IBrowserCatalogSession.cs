using Automation.Core.Jobs;
using Automation.Core.Results;

namespace Automation.Application.Abstractions;

public interface IBrowserCatalogSession : IAsyncDisposable
{
    Task<CatalogPageExtraction?> ExtractPageAsync(
        int pageNumber,
        CancellationToken cancellationToken);
}

public interface IBrowserCatalogSessionFactory
{
    Task<IBrowserCatalogSession> OpenAsync(
        AutomationJob job,
        CancellationToken cancellationToken);
}
