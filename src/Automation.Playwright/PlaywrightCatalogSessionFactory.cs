using System.Globalization;
using System.Text.RegularExpressions;
using Automation.Application;
using Automation.Application.Abstractions;
using Automation.Core.Jobs;
using Automation.Core.Results;
using Microsoft.Playwright;

namespace Automation.Playwright;

public sealed class PlaywrightCatalogSessionFactory(
    PlaywrightBrowserOptions options) : IBrowserCatalogSessionFactory, IAsyncDisposable
{
    private readonly SemaphoreSlim initializationLock = new(1, 1);
    private IPlaywright? playwright;
    private IBrowser? browser;
    private int disposed;

    public async Task<IBrowserCatalogSession> OpenAsync(AutomationJob job, CancellationToken cancellationToken)
    {
        var activeBrowser = await GetBrowserAsync(cancellationToken);
        var context = await activeBrowser.NewContextAsync().WaitAsync(cancellationToken);
        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        })
            .WaitAsync(cancellationToken);
        var page = await context.NewPageAsync().WaitAsync(cancellationToken);
        page.SetDefaultTimeout(options.OperationTimeoutMilliseconds);
        page.SetDefaultNavigationTimeout(options.NavigationTimeoutMilliseconds);

        try
        {
            await page.GotoAsync(job.StartUrl.AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = options.NavigationTimeoutMilliseconds,
            })
                .WaitAsync(cancellationToken);
            await AuthenticateDemoSiteIfRequiredAsync(page, cancellationToken);
            return new PlaywrightCatalogSession(context, page);
        }
        catch
        {
            await context.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        if (browser is not null)
        {
            await browser.DisposeAsync();
        }

        playwright?.Dispose();
        initializationLock.Dispose();
    }

    private async Task<IBrowser> GetBrowserAsync(CancellationToken cancellationToken)
    {
        if (browser is not null)
        {
            return browser;
        }

        await initializationLock.WaitAsync(cancellationToken);
        try
        {
            if (browser is not null)
            {
                return browser;
            }

            playwright = await Microsoft.Playwright.Playwright.CreateAsync().WaitAsync(cancellationToken);
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = options.Headless,
            })
                .WaitAsync(cancellationToken);
            return browser;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task AuthenticateDemoSiteIfRequiredAsync(IPage page, CancellationToken cancellationToken)
    {
        if (!string.Equals(new Uri(page.Url).AbsolutePath, "/login", StringComparison.Ordinal))
        {
            return;
        }

        await page.GetByLabel("Username").FillAsync(options.DemoUsername).WaitAsync(cancellationToken);
        await page.GetByLabel("Password").FillAsync(options.DemoPassword).WaitAsync(cancellationToken);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" })
            .ClickAsync()
            .WaitAsync(cancellationToken);
    }
}

public sealed class PlaywrightBrowserOptions
{
    public required bool Headless { get; init; }

    public required float NavigationTimeoutMilliseconds { get; init; }

    public required float OperationTimeoutMilliseconds { get; init; }

    public required string DemoUsername { get; init; }

    public required string DemoPassword { get; init; }
}

internal sealed class PlaywrightCatalogSession(IBrowserContext context, IPage page) : IBrowserCatalogSession, IFailureEvidenceCollector
{
    private int loadedPage = 1;
    private bool initialPageReady;

    public async Task<CatalogPageExtraction?> ExtractPageAsync(int pageNumber, CancellationToken cancellationToken)
    {
        if (!initialPageReady)
        {
            await WaitForCatalogPageAsync(page, 1, cancellationToken);
            initialPageReady = true;
        }

        if (!await MoveToPageAsync(pageNumber, cancellationToken))
        {
            return null;
        }

        var cards = page.GetByTestId("catalog-item");
        var count = await cards.CountAsync().WaitAsync(cancellationToken);
        var items = new List<CatalogItem>(count);
        for (var index = 0; index < count; index++)
        {
            var card = cards.Nth(index);
            var externalId = await card.GetAttributeAsync("data-item-id").WaitAsync(cancellationToken)
                ?? throw new InvalidOperationException("Catalog item is missing data-item-id.");
            var name = await card.GetByTestId("item-name").InnerTextAsync().WaitAsync(cancellationToken);
            var priceText = await card.GetByTestId("item-price").InnerTextAsync().WaitAsync(cancellationToken);
            if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                throw new InvalidOperationException($"Catalog item '{externalId}' has an invalid price.");
            }

            items.Add(new CatalogItem(externalId, name, price, pageNumber, new Uri(page.Url)));
        }

        var hasNextPage = await page.GetByTestId("next-page").CountAsync().WaitAsync(cancellationToken) > 0;
        return new CatalogPageExtraction(items, hasNextPage);
    }

    public ValueTask DisposeAsync() => context.DisposeAsync();

    public async Task CaptureFailureEvidenceAsync(string directoryPath, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directoryPath);

        try
        {
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(directoryPath, "screenshot.png"),
                FullPage = true,
            });
        }
        catch
        {
        }

        try
        {
            var html = await page.ContentAsync();
            await File.WriteAllTextAsync(Path.Combine(directoryPath, "page.html"), RedactHtml(html), cancellationToken);
        }
        catch
        {
        }

        try
        {
            await context.Tracing.StopAsync(new TracingStopOptions
            {
                Path = Path.Combine(directoryPath, "trace.zip"),
            });
        }
        catch
        {
        }
    }

    internal static async Task WaitForCatalogPageAsync(
        IPage page,
        int expectedPage,
        CancellationToken cancellationToken)
    {
        await page.WaitForFunctionAsync(
            """
            (expected) => {
              const status = document.querySelector('[role="status"]');
              return status?.textContent?.includes(`Page ${expected} loaded`) ||
                status?.dataset.testid === 'catalog-error';
            }
            """,
            expectedPage)
            .WaitAsync(cancellationToken);

        var status = await page.GetByRole(AriaRole.Status).InnerTextAsync().WaitAsync(cancellationToken);
        if (status.StartsWith("Catalog error:", StringComparison.Ordinal))
        {
            var statusMatch = Regex.Match(status, "HTTP\\s+(\\d{3})");
            int? statusCode = int.TryParse(statusMatch.Groups[1].Value, out var statusValue) ? statusValue : null;
            var retryAfterMatch = Regex.Match(status, "retry-after=([0-9]+(?:\\.[0-9]+)?)", RegexOptions.IgnoreCase);
            TimeSpan? retryAfter = double.TryParse(
                retryAfterMatch.Groups[1].Value,
                CultureInfo.InvariantCulture,
                out var retryAfterSeconds)
                ? TimeSpan.FromSeconds(retryAfterSeconds)
                : null;
            throw new BrowserOperationException(status, statusCode, retryAfter);
        }
    }

    private async Task<bool> MoveToPageAsync(int requestedPage, CancellationToken cancellationToken)
    {
        if (requestedPage < loadedPage)
        {
            throw new InvalidOperationException("Catalog pages must be extracted in ascending order.");
        }

        while (loadedPage < requestedPage)
        {
            var next = page.GetByTestId("next-page");
            if (await next.CountAsync().WaitAsync(cancellationToken) == 0)
            {
                return false;
            }

            await next.ClickAsync().WaitAsync(cancellationToken);
            await WaitForCatalogPageAsync(page, loadedPage + 1, cancellationToken);
            loadedPage++;
        }

        return true;
    }

    private static string RedactHtml(string value) =>
        Regex.Replace(
            value,
            "(?i)(password|token|authorization|cookie)(\\s*[=:]\\s*[\\\"'])[^\\\"']*",
            "$1$2[REDACTED]");
}
