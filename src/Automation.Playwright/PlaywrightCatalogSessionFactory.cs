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
        var context = await activeBrowser.NewContextAsync();
        await context.Tracing.StartAsync(new TracingStartOptions
        {
            Screenshots = true,
            Snapshots = true,
            Sources = true,
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(options.OperationTimeoutMilliseconds);
        page.SetDefaultNavigationTimeout(options.NavigationTimeoutMilliseconds);

        try
        {
            await page.GotoAsync(job.StartUrl.AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = options.NavigationTimeoutMilliseconds,
            });
            await AuthenticateDemoSiteIfRequiredAsync(page);
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

            playwright = await Microsoft.Playwright.Playwright.CreateAsync();
            browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = options.Headless,
            });
            return browser;
        }
        finally
        {
            initializationLock.Release();
        }
    }

    private async Task AuthenticateDemoSiteIfRequiredAsync(IPage page)
    {
        if (!string.Equals(new Uri(page.Url).AbsolutePath, "/login", StringComparison.Ordinal))
        {
            return;
        }

        await page.GetByLabel("Username").FillAsync(options.DemoUsername);
        await page.GetByLabel("Password").FillAsync(options.DemoPassword);
        await page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Sign in" }).ClickAsync();
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

    public async Task<IReadOnlyCollection<CatalogItem>> ExtractPageAsync(int pageNumber, CancellationToken cancellationToken)
    {
        if (!initialPageReady)
        {
            await WaitForCatalogPageAsync(page, 1);
            initialPageReady = true;
        }

        if (!await MoveToPageAsync(pageNumber))
        {
            return [];
        }

        var cards = page.GetByTestId("catalog-item");
        var count = await cards.CountAsync();
        var items = new List<CatalogItem>(count);
        for (var index = 0; index < count; index++)
        {
            var card = cards.Nth(index);
            var externalId = await card.GetAttributeAsync("data-item-id")
                ?? throw new InvalidOperationException("Catalog item is missing data-item-id.");
            var name = await card.GetByTestId("item-name").InnerTextAsync();
            var priceText = await card.GetByTestId("item-price").InnerTextAsync();
            if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                throw new InvalidOperationException($"Catalog item '{externalId}' has an invalid price.");
            }

            items.Add(new CatalogItem(externalId, name, price, new Uri(page.Url)));
        }

        return items;
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

    internal static async Task WaitForCatalogPageAsync(IPage page, int expectedPage)
    {
        await page.WaitForFunctionAsync(
            """
            (expected) => {
              const status = document.querySelector('[role="status"]');
              return status?.textContent?.includes(`Page ${expected} loaded`) ||
                status?.dataset.testid === 'catalog-error';
            }
            """,
            expectedPage);

        var status = await page.GetByRole(AriaRole.Status).InnerTextAsync();
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

    private async Task<bool> MoveToPageAsync(int requestedPage)
    {
        if (requestedPage < loadedPage)
        {
            throw new InvalidOperationException("Catalog pages must be extracted in ascending order.");
        }

        while (loadedPage < requestedPage)
        {
            var next = page.GetByTestId("next-page");
            if (await next.CountAsync() == 0)
            {
                return false;
            }

            await next.ClickAsync();
            await WaitForCatalogPageAsync(page, loadedPage + 1);
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
