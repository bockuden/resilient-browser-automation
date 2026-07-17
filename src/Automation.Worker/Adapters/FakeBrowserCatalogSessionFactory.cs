using Automation.Application.Abstractions;
using Automation.Core.Jobs;
using Automation.Core.Results;

namespace Automation.Worker.Adapters;

public sealed class FakeBrowserCatalogSessionFactory : IBrowserCatalogSessionFactory
{
    public Task<IBrowserCatalogSession> OpenAsync(AutomationJob job, CancellationToken cancellationToken) =>
        Task.FromResult<IBrowserCatalogSession>(new FakeBrowserCatalogSession(job));

    private sealed class FakeBrowserCatalogSession(AutomationJob job) : IBrowserCatalogSession
    {
        public Task<IReadOnlyCollection<CatalogItem>> ExtractPageAsync(
            int pageNumber,
            CancellationToken cancellationToken)
        {
            IReadOnlyCollection<CatalogItem> items =
            [
                new CatalogItem(
                    $"{job.Target}-{pageNumber:D3}",
                    $"Fake catalog item {pageNumber}",
                    pageNumber,
                    job.StartUrl),
            ];

            return Task.FromResult(items);
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

