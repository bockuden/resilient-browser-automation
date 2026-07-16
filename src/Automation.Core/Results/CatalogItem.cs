namespace Automation.Core.Results;

public sealed record CatalogItem(
    string ExternalId,
    string Name,
    decimal Price,
    Uri SourceUrl);

