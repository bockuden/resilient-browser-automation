namespace Automation.Core.Results;

public sealed record CatalogItem(
    string ExternalId,
    string Name,
    decimal Price,
    int PageNumber,
    Uri SourceUrl);

public sealed record CatalogPageExtraction(
    IReadOnlyCollection<CatalogItem> Items,
    bool HasNextPage);
