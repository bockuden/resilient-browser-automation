using System.Globalization;
using System.Text.Json;
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
            return new PlaywrightCatalogSession(
                context,
                page,
                AuthenticateDemoSiteIfRequiredAsync,
                options.OperationTimeoutMilliseconds);
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

        var username = await ResolveConfiguredFieldAsync(page, "Username", cancellationToken);
        var password = await ResolveConfiguredFieldAsync(page, "Password", cancellationToken);
        await username.FillAsync(options.DemoUsername).WaitAsync(cancellationToken);
        await password.FillAsync(options.DemoPassword).WaitAsync(cancellationToken);

        var loginButton = page.GetByRole(
            AriaRole.Button,
            new PageGetByRoleOptions { Name = "Sign in", Exact = true });
        await WaitForVisibleAsync(loginButton, cancellationToken);
        var loginTestId = await loginButton.GetAttributeAsync("data-testid").WaitAsync(cancellationToken);
        var configuredLoginButton = string.IsNullOrWhiteSpace(loginTestId) ||
            loginTestId.EndsWith("-changed", StringComparison.Ordinal)
                ? loginButton
                : page.GetByTestId(loginTestId);
        await EnsureSingleEnabledControlAsync(configuredLoginButton, "login button", cancellationToken);
        await configuredLoginButton.ClickAsync().WaitAsync(cancellationToken);
        await page.WaitForFunctionAsync("() => window.location.pathname !== '/login'")
            .WaitAsync(cancellationToken);
    }

    private async Task WaitForVisibleAsync(ILocator locator, CancellationToken cancellationToken)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = Math.Min(options.OperationTimeoutMilliseconds, 6000),
            }).WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            // The validation below reports missing and hidden controls precisely.
        }
    }

    private static async Task EnsureSingleEnabledControlAsync(
        ILocator locator,
        string description,
        CancellationToken cancellationToken)
    {
        var count = await locator.CountAsync().WaitAsync(cancellationToken);
        if (count != 1)
        {
            throw new InvalidOperationException(
                $"The {description} selector matched {count} elements; exactly one is required.");
        }

        if (!await locator.IsVisibleAsync().WaitAsync(cancellationToken))
        {
            throw new InvalidOperationException($"The {description} selector matched a hidden element.");
        }

        if (!await locator.IsEnabledAsync().WaitAsync(cancellationToken))
        {
            throw new InvalidOperationException($"The {description} selector matched a disabled element.");
        }
    }

    private static async Task<ILocator> ResolveConfiguredFieldAsync(
        IPage page,
        string label,
        CancellationToken cancellationToken)
    {
        var semantic = page.GetByLabel(label, new PageGetByLabelOptions { Exact = true });
        var token = await semantic.GetAttributeAsync("data-testid").WaitAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(token))
        {
            await EnsureSingleEnabledControlAsync(semantic, $"{label} field", cancellationToken);
            return semantic;
        }

        var configured = page.GetByTestId(token);
        var count = await configured.CountAsync().WaitAsync(cancellationToken);
        if (count == 0)
        {
            await EnsureSingleEnabledControlAsync(semantic, $"{label} field", cancellationToken);
            return semantic;
        }

        await EnsureSingleEnabledControlAsync(configured, $"{label} field", cancellationToken);
        return configured;
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

internal sealed class PlaywrightCatalogSession(
    IBrowserContext context,
    IPage page,
    Func<IPage, CancellationToken, Task> authenticateIfRequired,
    float operationTimeoutMilliseconds) : IBrowserCatalogSession, IFailureEvidenceCollector
{
    private const int DelayedSelectorTimeoutMilliseconds = 6000;
    private Dictionary<string, string>? selectors;
    private int totalPages;
    private int loadedPage = 1;
    private bool initialPageReady;

    public async Task<CatalogPageExtraction?> ExtractPageAsync(int pageNumber, CancellationToken cancellationToken)
    {
        if (!initialPageReady)
        {
            await WaitForCatalogPageAsync(1, cancellationToken);
            initialPageReady = true;
        }

        if (!await MoveToPageAsync(pageNumber, cancellationToken))
        {
            return null;
        }

        selectors ??= await ReadSelectorsAsync(cancellationToken);
        var catalog = page.GetByTestId(Selector("catalog"));
        var catalogCount = await catalog.CountAsync().WaitAsync(cancellationToken);
        if (catalogCount != 1 || !await catalog.IsVisibleAsync().WaitAsync(cancellationToken))
        {
            throw new InvalidOperationException(
                $"Configured 'catalog' selector matched {catalogCount} visible containers; exactly one is required.");
        }

        var cards = await ResolveCollectionAsync("item", catalog.Locator("[data-item-id]"), cancellationToken);
        var count = await cards.CountAsync().WaitAsync(cancellationToken);
        var items = new List<CatalogItem>(count);
        var externalIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < count; index++)
        {
            var card = cards.Nth(index);
            var externalId = await card.GetAttributeAsync("data-item-id").WaitAsync(cancellationToken)
                ?? throw new InvalidOperationException("Catalog item is missing data-item-id.");
            if (!externalIds.Add(externalId))
            {
                throw new InvalidOperationException(
                    $"Catalog item selector matched duplicate item id '{externalId}' on page {pageNumber}.");
            }

            var name = await card.GetByTestId(Selector("item_name")).InnerTextAsync().WaitAsync(cancellationToken);
            var priceText = await card.GetByTestId(Selector("item_price")).InnerTextAsync().WaitAsync(cancellationToken);
            if (!decimal.TryParse(priceText, NumberStyles.Number, CultureInfo.InvariantCulture, out var price))
            {
                throw new InvalidOperationException($"Catalog item '{externalId}' has an invalid price.");
            }

            items.Add(new CatalogItem(externalId, name, price, pageNumber, new Uri(page.Url)));
        }

        if (pageNumber >= totalPages)
        {
            return new CatalogPageExtraction(items, HasNextPage: false);
        }

        var hasNextPage = await ResolveOptionalControlAsync(
            "next_page",
            page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next page", Exact = true }),
            cancellationToken) is not null;
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

    private async Task WaitForCatalogPageAsync(
        int expectedPage,
        CancellationToken cancellationToken)
    {
        await page.WaitForFunctionAsync(
            """
            (expected) => {
              if (window.location.pathname === '/login') return true;
              const status = document.querySelector('[role="status"]');
              return status?.textContent?.includes(`Page ${expected} loaded`) ||
                status?.dataset.testid === 'catalog-error';
            }
            """,
            expectedPage)
            .WaitAsync(cancellationToken);

        if (string.Equals(new Uri(page.Url).AbsolutePath, "/login", StringComparison.Ordinal))
        {
            await authenticateIfRequired(page, cancellationToken);
            if (string.Equals(new Uri(page.Url).AbsolutePath, "/login", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Demo-site authentication did not return to the catalog.");
            }

            await WaitForCatalogPageAsync(expectedPage, cancellationToken);
            return;
        }

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
            var next = await ResolveOptionalControlAsync(
                "next_page",
                page.GetByRole(AriaRole.Button, new PageGetByRoleOptions { Name = "Next page", Exact = true }),
                cancellationToken);
            if (next is null)
            {
                return false;
            }

            await next.ClickAsync().WaitAsync(cancellationToken);
            await WaitForCatalogPageAsync(loadedPage + 1, cancellationToken);
            loadedPage++;
        }

        return true;
    }

    private async Task<ILocator> ResolveCollectionAsync(
        string selectorName,
        ILocator fallback,
        CancellationToken cancellationToken)
    {
        var configured = page.GetByTestId(Selector(selectorName));
        var count = await configured.CountAsync().WaitAsync(cancellationToken);
        if (count == 0)
        {
            await WaitForVisibleAsync(configured, cancellationToken);
            count = await configured.CountAsync().WaitAsync(cancellationToken);
        }

        if (count > 0)
        {
            for (var index = 0; index < count; index++)
            {
                if (!await configured.Nth(index).IsVisibleAsync().WaitAsync(cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"Configured '{selectorName}' selector matched a hidden element.");
                }

                var disabled = await configured.Nth(index).GetAttributeAsync("aria-disabled")
                    .WaitAsync(cancellationToken);
                if (string.Equals(disabled, "true", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Configured '{selectorName}' selector matched a disabled element.");
                }
            }

            return configured;
        }

        if (await fallback.CountAsync().WaitAsync(cancellationToken) == 0)
        {
            throw new InvalidOperationException(
                $"Configured '{selectorName}' selector '{Selector(selectorName)}' was unavailable, " +
                "and its semantic fallback matched no elements.");
        }

        return fallback;
    }

    private async Task<ILocator?> ResolveOptionalControlAsync(
        string selectorName,
        ILocator fallback,
        CancellationToken cancellationToken)
    {
        var configured = page.GetByTestId(Selector(selectorName));
        var count = await configured.CountAsync().WaitAsync(cancellationToken);
        if (count == 0)
        {
            await WaitForVisibleAsync(configured, cancellationToken);
            count = await configured.CountAsync().WaitAsync(cancellationToken);
        }

        if (count == 0)
        {
            var fallbackCount = await fallback.CountAsync().WaitAsync(cancellationToken);
            if (fallbackCount == 0)
            {
                return null;
            }

            if (fallbackCount > 1)
            {
                throw new InvalidOperationException(
                    $"Semantic fallback for '{selectorName}' matched {fallbackCount} elements.");
            }

            await EnsureSingleEnabledControlAsync(fallback, selectorName, cancellationToken);
            return fallback;
        }

        await EnsureSingleEnabledControlAsync(configured, selectorName, cancellationToken);
        return configured;
    }

    private async Task<Dictionary<string, string>> ReadSelectorsAsync(CancellationToken cancellationToken)
    {
        var json = await page.Locator("#catalog-config").InnerTextAsync().WaitAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        totalPages = document.RootElement.GetProperty("totalPages").GetInt32();
        var selectorElement = document.RootElement.GetProperty("selectors");
        return selectorElement.EnumerateObject().ToDictionary(
            property => property.Name,
            property => property.Value.GetString()
                ?? throw new InvalidOperationException($"Configured selector '{property.Name}' is empty."),
            StringComparer.Ordinal);
    }

    private string Selector(string name) =>
        selectors is not null && selectors.TryGetValue(name, out var configured)
            ? configured
            : throw new InvalidOperationException($"The page did not provide the '{name}' selector token.");

    private async Task WaitForVisibleAsync(ILocator locator, CancellationToken cancellationToken)
    {
        try
        {
            await locator.WaitForAsync(new LocatorWaitForOptions
            {
                State = WaitForSelectorState.Visible,
                Timeout = Math.Min(operationTimeoutMilliseconds, DelayedSelectorTimeoutMilliseconds),
            }).WaitAsync(cancellationToken);
        }
        catch (TimeoutException)
        {
            // Count and state checks below distinguish a genuinely missing selector from
            // a present but hidden target after allowing bounded delayed selectors to appear.
        }
    }

    private static async Task EnsureSingleEnabledControlAsync(
        ILocator locator,
        string description,
        CancellationToken cancellationToken)
    {
        var count = await locator.CountAsync().WaitAsync(cancellationToken);
        if (count != 1)
        {
            throw new InvalidOperationException(
                $"The {description} selector matched {count} elements; exactly one is required.");
        }

        if (!await locator.IsVisibleAsync().WaitAsync(cancellationToken))
        {
            throw new InvalidOperationException($"The {description} selector matched a hidden element.");
        }

        if (!await locator.IsEnabledAsync().WaitAsync(cancellationToken) ||
            string.Equals(
                await locator.GetAttributeAsync("aria-disabled").WaitAsync(cancellationToken),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"The {description} selector matched a disabled element.");
        }
    }

    private static string RedactHtml(string value) =>
        Regex.Replace(
            value,
            "(?i)(password|token|authorization|cookie)(\\s*[=:]\\s*[\\\"'])[^\\\"']*",
            "$1$2[REDACTED]");
}
