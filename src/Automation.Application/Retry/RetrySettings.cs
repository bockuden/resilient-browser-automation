namespace Automation.Application.Retry;

public sealed record RetrySettings(
    int MaxAttempts,
    TimeSpan BaseDelay,
    TimeSpan MaxDelay);

public sealed record JobExecutionSettings(TimeSpan WholeJobTimeout);

public sealed record RetryDecision(
    int NextAttempt,
    string Reason,
    TimeSpan Delay,
    TimeSpan RemainingBudget);
