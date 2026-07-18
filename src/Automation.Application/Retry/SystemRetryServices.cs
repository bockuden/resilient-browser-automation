namespace Automation.Application.Retry;

public sealed class SystemJobClock : IJobClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);
}

public sealed class SystemRetryRandom : IRetryRandom
{
    public double NextDouble() => Random.Shared.NextDouble();
}

public sealed class NoOpRetryObserver : IRetryObserver
{
    public void OnRetry(RetryDecision decision)
    {
    }
}
