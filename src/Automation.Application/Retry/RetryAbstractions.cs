namespace Automation.Application.Retry;

public interface IJobClock
{
    DateTimeOffset UtcNow { get; }

    Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken);
}

public interface IRetryRandom
{
    double NextDouble();
}

public interface IRetryObserver
{
    void OnRetry(RetryDecision decision);
}
