using Automation.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace Automation.Worker.Services;

public sealed class TargetRateLimiter(IOptions<AutomationWorkerOptions> options) : ITargetRateLimiter
{
    private readonly TimeSpan period = TimeSpan.FromMilliseconds(options.Value.Concurrency.PerTargetRatePeriodMilliseconds);
    private readonly int limit = options.Value.Concurrency.PerTargetRateLimit;
    private readonly int burstSize = options.Value.Concurrency.PerTargetBurstSize;
    private readonly Dictionary<string, Bucket> buckets = new(StringComparer.OrdinalIgnoreCase);
    private readonly object bucketsLock = new();

    public async Task WaitAsync(string target, CancellationToken cancellationToken)
    {
        var bucket = GetBucket(target);

        while (true)
        {
            TimeSpan delay;
            await bucket.Lock.WaitAsync(cancellationToken);
            try
            {
                Refill(bucket, DateTimeOffset.UtcNow);
                if (bucket.Tokens >= 1)
                {
                    bucket.Tokens -= 1;
                    return;
                }

                delay = TimeSpan.FromTicks((long)(period.Ticks * ((1 - bucket.Tokens) / limit)));
            }
            finally
            {
                bucket.Lock.Release();
            }

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    private Bucket GetBucket(string target)
    {
        var key = string.IsNullOrWhiteSpace(target) ? "<unknown>" : target;
        lock (bucketsLock)
        {
            if (!buckets.TryGetValue(key, out var bucket))
            {
                bucket = new Bucket(burstSize, DateTimeOffset.UtcNow);
                buckets.Add(key, bucket);
            }

            return bucket;
        }
    }

    private void Refill(Bucket bucket, DateTimeOffset now)
    {
        var elapsed = now - bucket.UpdatedAt;
        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        var refill = elapsed.TotalMilliseconds / period.TotalMilliseconds * limit;
        bucket.Tokens = Math.Min(burstSize, bucket.Tokens + refill);
        bucket.UpdatedAt = now;
    }

    private sealed class Bucket(double tokens, DateTimeOffset updatedAt)
    {
        public SemaphoreSlim Lock { get; } = new(1, 1);

        public double Tokens { get; set; } = tokens;

        public DateTimeOffset UpdatedAt { get; set; } = updatedAt;
    }
}
