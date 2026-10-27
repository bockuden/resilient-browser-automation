using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using Automation.Application;
using Automation.Application.Abstractions;
using Automation.Core.Jobs;
using Automation.Core.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;

namespace Automation.Playwright;

public sealed class PlaywrightCatalogSessionFactory(
    PlaywrightBrowserOptions options,
    ILogger<PlaywrightCatalogSessionFactory> logger) : IBrowserCatalogSessionFactory, IAsyncDisposable
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
            var session = new PlaywrightCatalogSession(
                context,
                page,
                AuthenticateDemoSiteIfRequiredAsync,
                options.OperationTimeoutMilliseconds);
            await page.RouteAsync("**/api/catalog**", session.ValidateApiResponseAsync)
                .WaitAsync(cancellationToken);
            await page.GotoAsync(job.StartUrl.AbsoluteUri, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = options.NavigationTimeoutMilliseconds,
            })
                .WaitAsync(cancellationToken);
            return session;
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
            new PageGetByRoleOptions { Name = "Sign in", Exact = true, IncludeHidden = true });
        await WaitForVisibleAsync(loginButton, cancellationToken);
        var loginButtonCount = await loginButton.CountAsync().WaitAsync(cancellationToken);
        if (loginButtonCount != 1)
        {
            throw new InvalidOperationException(
                $"The login button semantic fallback matched {loginButtonCount} elements; exactly one is required.");
        }

        if (!await loginButton.IsVisibleAsync().WaitAsync(cancellationToken))
        {
            throw new InvalidOperationException("The login button selector matched a hidden element.");
        }

        var loginTestId = await loginButton.GetAttributeAsync("data-testid").WaitAsync(cancellationToken);
        var configuredLoginButton = string.IsNullOrWhiteSpace(loginTestId) ||
            loginTestId.EndsWith("-changed", StringComparison.Ordinal)
                ? loginButton
                : page.GetByTestId(loginTestId);
        await EnsureSingleEnabledControlAsync(configuredLoginButton, "login button", cancellationToken);
        await configuredLoginButton.ClickAsync().WaitAsync(cancellationToken);
        await page.WaitForFunctionAsync("() => window.location.pathname !== '/login'")
            .WaitAsync(cancellationToken);
        logger.LogInformation(
            new EventId(3010, "ProtectedCatalogAuthenticationSucceeded"),
            "Authenticated protected catalog session.");
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
    private static readonly Dictionary<string, string> DefaultSelectors = new(StringComparer.Ordinal)
    {
        ["username"] = "username",
        ["password"] = "password",
        ["login_button"] = "login-submit",
        ["catalog"] = "catalog",
        ["item"] = "catalog-item",
        ["item_name"] = "item-name",
        ["item_price"] = "item-price",
        ["next_page"] = "next-page",
    };
    private Dictionary<string, string>? selectors;
    private string? apiContractFailure;
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
        ILocator catalog = page.GetByTestId(Selector("catalog"));
        var catalogCount = await catalog.CountAsync().WaitAsync(cancellationToken);
        if (catalogCount == 0)
        {
            catalog = page.Locator("#catalog");
            catalogCount = await catalog.CountAsync().WaitAsync(cancellationToken);
        }

        if (catalogCount != 1)
        {
            throw new InvalidOperationException(
                $"Configured 'catalog' selector matched {catalogCount} containers; exactly one is required.");
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

    public async Task ValidateApiResponseAsync(IRoute route)
    {
        var response = await route.FetchAsync();
        if (!response.Ok)
        {
            await route.FulfillAsync(new RouteFulfillOptions { Response = response });
            return;
        }

        var contentType = response.Headers.TryGetValue("content-type", out var headerValue)
            ? headerValue
            : null;
        if (!MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
            !string.Equals(mediaType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
        {
            await FailApiResponseAsync(route, "expected Content-Type application/json");
            return;
        }

        var body = await response.BodyAsync();
        if (!TryValidateCatalogResponse(body, out var failureReason))
        {
            await FailApiResponseAsync(route, failureReason);
            return;
        }

        await route.FulfillAsync(new RouteFulfillOptions { Response = response });
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
            if (apiContractFailure is not null)
            {
                throw new InvalidDataException($"Catalog API response contract validation failed: {apiContractFailure}");
            }

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
        var root = document.RootElement;
        totalPages = root.GetProperty("totalPages").GetInt32();
        var resolvedSelectors = new Dictionary<string, string>(DefaultSelectors, StringComparer.Ordinal);
        if (!root.TryGetProperty("selectors", out var selectorElement) ||
            selectorElement.ValueKind != JsonValueKind.Object)
        {
            return resolvedSelectors;
        }

        foreach (var property in selectorElement.EnumerateObject())
        {
            resolvedSelectors[property.Name] = property.Value.GetString()
                ?? throw new InvalidOperationException($"Configured selector '{property.Name}' is empty.");
        }

        return resolvedSelectors;
    }

    private string Selector(string name) =>
        selectors is not null && selectors.TryGetValue(name, out var configured)
            ? configured
            : DefaultSelectors.TryGetValue(name, out var fallback)
                ? fallback
                : throw new InvalidOperationException($"The page did not provide the '{name}' selector token.");

    private async Task FailApiResponseAsync(IRoute route, string reason)
    {
        apiContractFailure = reason;
        await route.FulfillAsync(new RouteFulfillOptions
        {
            Status = 502,
            ContentType = "application/json",
            Body = "{}",
        });
    }

    private static bool TryValidateCatalogResponse(byte[] body, out string failureReason)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            failureReason = "response body is not valid JSON";
            return false;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failureReason = "response JSON must be an object";
                return false;
            }

            if (!HasPositiveInteger(root, "page") ||
                !HasPositiveInteger(root, "total_pages") ||
                !HasPositiveInteger(root, "attempt"))
            {
                failureReason = "response must contain positive integer page, total_pages, and attempt fields";
                return false;
            }

            if (!root.TryGetProperty("scenario", out var scenario) ||
                scenario.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(scenario.GetString()))
            {
                failureReason = "response must contain a non-empty string scenario field";
                return false;
            }

            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                failureReason = "response must contain an items array";
                return false;
            }

            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object ||
                    !HasNonEmptyString(item, "id") ||
                    !HasNonEmptyString(item, "name") ||
                    !item.TryGetProperty("price", out var price) ||
                    price.ValueKind != JsonValueKind.Number ||
                    !price.TryGetDecimal(out _))
                {
                    failureReason = $"items[{index}] must contain non-empty string id and name fields and a numeric price";
                    return false;
                }

                index++;
            }
        }

        failureReason = string.Empty;
        return true;
    }

    private static bool HasPositiveInteger(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.Number &&
        property.TryGetInt32(out var value) &&
        value > 0;

    private static bool HasNonEmptyString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(property.GetString());

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
