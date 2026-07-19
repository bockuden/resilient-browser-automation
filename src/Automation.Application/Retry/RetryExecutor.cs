namespace Automation.Application.Retry;

public sealed class RetryExecutor(
    RetrySettings settings,
    TransientFailureClassifier classifier,
    IJobClock clock,
    IRetryRandom random,
    IRetryObserver observer)
{
    public async Task<T> ExecuteAsync<T>(
        Func<int, CancellationToken, Task<T>> operation,
        DateTimeOffset deadline,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await operation(attempt, cancellationToken);
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested && classifier.IsTransient(error))
            {
                if (attempt >= settings.MaxAttempts)
                {
                    throw;
                }

                var remaining = deadline - clock.UtcNow;
                var delay = classifier.GetRetryAfter(error) ?? CalculateBackoff(attempt);
                if (delay > remaining)
                {
                    throw new TimeoutException("The remaining job budget cannot accommodate another retry.", error);
                }

                observer.OnRetry(new RetryDecision(attempt + 1, error.Message, delay, remaining));
                AutomationMetrics.Retries.Add(1);
                await clock.DelayAsync(delay, cancellationToken);
            }
        }
    }

    private TimeSpan CalculateBackoff(int failedAttempt)
    {
        var exponentialMilliseconds = settings.BaseDelay.TotalMilliseconds * Math.Pow(2, failedAttempt - 1);
        var boundedMilliseconds = Math.Min(exponentialMilliseconds, settings.MaxDelay.TotalMilliseconds);
        var jitterMultiplier = 0.5 + random.NextDouble();
        return TimeSpan.FromMilliseconds(Math.Min(boundedMilliseconds * jitterMultiplier, settings.MaxDelay.TotalMilliseconds));
    }
}
