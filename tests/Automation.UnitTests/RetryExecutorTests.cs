using Automation.Application;
using Automation.Application.Retry;

namespace Automation.UnitTests;

[TestFixture]
public sealed class RetryExecutorTests
{
    [Test]
    public async Task ExecuteAsync_RetriesTwoTransient503FailuresWithoutWaitingInRealTime()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-01-01T00:00:00Z"));
        var observer = new RecordingObserver();
        var executor = CreateExecutor(clock, observer, maxAttempts: 3);
        var attempts = 0;

        var result = await executor.ExecuteAsync(
            (_, _) =>
            {
                attempts++;
                return attempts < 3
                    ? Task.FromException<string>(new BrowserOperationException("HTTP 503", 503))
                    : Task.FromResult("recovered");
            },
            clock.UtcNow + TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo("recovered"));
            Assert.That(attempts, Is.EqualTo(3));
            Assert.That(clock.Delays, Is.EqualTo([TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)]));
            Assert.That(observer.Decisions.Select(decision => decision.NextAttempt), Is.EqualTo([2, 3]));
        });
    }

    [Test]
    public void ExecuteAsync_DoesNotRetryPermanentFailure()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var executor = CreateExecutor(clock, new RecordingObserver(), maxAttempts: 3);
        var attempts = 0;

        Assert.ThrowsAsync<BrowserOperationException>(async () => await executor.ExecuteAsync<string>(
            (_, _) =>
            {
                attempts++;
                return Task.FromException<string>(new BrowserOperationException("HTTP 500", 500));
            },
            clock.UtcNow + TimeSpan.FromSeconds(10),
            CancellationToken.None));
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public void ExecuteAsync_StopsWhenRetryAfterExceedsRemainingBudget()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var executor = CreateExecutor(clock, new RecordingObserver(), maxAttempts: 3);

        Assert.ThrowsAsync<TimeoutException>(async () => await executor.ExecuteAsync<string>(
            (_, _) => Task.FromException<string>(new BrowserOperationException("HTTP 429", 429, TimeSpan.FromSeconds(5))),
            clock.UtcNow + TimeSpan.FromSeconds(1),
            CancellationToken.None));
        Assert.That(clock.Delays, Is.Empty);
    }

    [Test]
    public async Task ExecuteAsync_UsesRetryAfterWhenItFitsTheRemainingBudget()
    {
        var clock = new FakeClock(DateTimeOffset.UtcNow);
        var executor = CreateExecutor(clock, new RecordingObserver(), maxAttempts: 2);
        var attempts = 0;

        await executor.ExecuteAsync(
            (_, _) =>
            {
                attempts++;
                return attempts == 1
                    ? Task.FromException<string>(new BrowserOperationException("HTTP 429", 429, TimeSpan.FromMilliseconds(750)))
                    : Task.FromResult("recovered");
            },
            clock.UtcNow + TimeSpan.FromSeconds(2),
            CancellationToken.None);

        Assert.That(clock.Delays, Is.EqualTo([TimeSpan.FromMilliseconds(750)]));
    }

    [Test]
    public void ExecuteAsync_CancellationInterruptsBackoff()
    {
        var clock = new CancellingClock(DateTimeOffset.UtcNow);
        var executor = CreateExecutor(clock, new RecordingObserver(), maxAttempts: 3);
        using var cancellation = new CancellationTokenSource();

        Assert.ThrowsAsync<TaskCanceledException>(async () => await executor.ExecuteAsync<string>(
            (_, token) => Task.FromException<string>(new BrowserOperationException("HTTP 503", 503)),
            clock.UtcNow + TimeSpan.FromSeconds(10),
            cancellation.Token));
    }

    private static RetryExecutor CreateExecutor(IJobClock clock, IRetryObserver observer, int maxAttempts) =>
        new(
            new RetrySettings(maxAttempts, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)),
            new TransientFailureClassifier(),
            clock,
            new FixedRetryRandom(),
            observer);

    private sealed class FakeClock(DateTimeOffset initial) : IJobClock
    {
        public List<TimeSpan> Delays { get; } = [];

        public DateTimeOffset UtcNow { get; private set; } = initial;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            Delays.Add(delay);
            UtcNow += delay;
            return Task.CompletedTask;
        }
    }

    private sealed class CancellingClock(DateTimeOffset initial) : IJobClock
    {
        public DateTimeOffset UtcNow { get; } = initial;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
            Task.FromCanceled(new CancellationToken(canceled: true));
    }

    private sealed class FixedRetryRandom : IRetryRandom
    {
        public double NextDouble() => 0.5;
    }

    private sealed class RecordingObserver : IRetryObserver
    {
        public List<RetryDecision> Decisions { get; } = [];

        public void OnRetry(RetryDecision decision) => Decisions.Add(decision);
    }
}
