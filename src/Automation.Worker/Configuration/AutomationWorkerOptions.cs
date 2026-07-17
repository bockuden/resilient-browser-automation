namespace Automation.Worker.Configuration;

public sealed class AutomationWorkerOptions
{
    public const string SectionName = "Automation";

    public BrowserOptions Browser { get; init; } = new();

    public RetryOptions Retry { get; init; } = new();

    public TimeoutOptions Timeouts { get; init; } = new();

    public ConcurrencyOptions Concurrency { get; init; } = new();

    public StorageOptions Storage { get; init; } = new();

    public ArtifactOptions Artifacts { get; init; } = new();
}

public sealed class BrowserOptions
{
    public int NavigationTimeoutSeconds { get; init; } = 30;

    public int OperationTimeoutSeconds { get; init; } = 30;
}

public sealed class RetryOptions
{
    public int MaxAttempts { get; init; } = 3;

    public int BaseDelayMilliseconds { get; init; } = 250;

    public int MaxDelayMilliseconds { get; init; } = 5000;
}

public sealed class TimeoutOptions
{
    public int WholeJobTimeoutSeconds { get; init; } = 300;
}

public sealed class ConcurrencyOptions
{
    public int MaxConcurrentJobs { get; init; } = 1;
}

public sealed class StorageOptions
{
    public string ConnectionString { get; init; } = "Data Source=artifacts/automation.db";

    public int StaleRunningJobSeconds { get; init; } = 300;
}

public sealed class ArtifactOptions
{
    public string RootPath { get; init; } = "artifacts";
}
