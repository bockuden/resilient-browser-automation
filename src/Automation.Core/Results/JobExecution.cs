namespace Automation.Core.Results;

public sealed record JobExecution(
    string JobId,
    JobStatus Status,
    DateTimeOffset UpdatedAt,
    string? ErrorCode = null,
    string? ErrorMessage = null);

