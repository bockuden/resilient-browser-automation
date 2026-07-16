using Automation.Core.Jobs;
using Automation.Core.Results;

namespace Automation.Application.Abstractions;

public interface IBrowserCatalogSession : IAsyncDisposable
{
    Task<IReadOnlyCollection<CatalogItem>> ExtractPageAsync(
        int pageNumber,
        CancellationToken cancellationToken);
}

public interface IBrowserCatalogSessionFactory
{
    Task<IBrowserCatalogSession> OpenAsync(
        AutomationJob job,
        CancellationToken cancellationToken);
}

