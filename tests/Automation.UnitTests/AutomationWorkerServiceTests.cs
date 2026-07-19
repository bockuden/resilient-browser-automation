using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Automation.Application;
using Automation.Application.Abstractions;
using Automation.Core.Jobs;
using Automation.Worker.Configuration;
using Automation.Worker.Jobs;
using Automation.Worker.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Automation.UnitTests;

[TestFixture]
public sealed class AutomationWorkerServiceTests
{
    [Test]
    public async Task ExecuteAsync_DoesNotExceedConfiguredConcurrency()
    {
        var jobs = Enumerable.Range(1, 20)
            .Select(index => JobInputResult.Valid(index, CreateJob($"job-{index}", $"target-{index % 4}")))
            .ToArray();
        var runner = new MeasuringJobRunner(TimeSpan.FromMilliseconds(30));
        var service = CreateService(
            jobs,
            runner,
            new NoOpTargetRateLimiter(),
            new ConcurrencyOptions
            {
                MaxConcurrentJobs = 3,
                QueueCapacity = 4,
                PerTargetRateLimit = 10,
                PerTargetRatePeriodMilliseconds = 1,
                PerTargetBurstSize = 10,
                ShutdownGracePeriodSeconds = 1,
            });

        await service.StartAsync(CancellationToken.None);
        var summary = await service.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(summary.CompletedJobs, Is.EqualTo(20));
            Assert.That(summary.ExitCode, Is.EqualTo(WorkerExitCode.Success));
            Assert.That(runner.MaxActiveJobs, Is.LessThanOrEqualTo(3));
        });
    }

    [Test]
    public async Task ExecuteAsync_AppliesPerTargetRateLimitBeforeStartingJobs()
    {
        var jobs = Enumerable.Range(1, 3)
            .Select(index => JobInputResult.Valid(index, CreateJob($"same-target-{index}", "shared-target")))
            .ToArray();
        var runner = new MeasuringJobRunner(TimeSpan.Zero);
        var service = CreateService(
            jobs,
            runner,
            new TargetRateLimiter(Options.Create(new AutomationWorkerOptions
            {
                Concurrency = new ConcurrencyOptions
                {
                    MaxConcurrentJobs = 3,
                    QueueCapacity = 3,
                    PerTargetRateLimit = 1,
                    PerTargetRatePeriodMilliseconds = 40,
                    PerTargetBurstSize = 1,
                    ShutdownGracePeriodSeconds = 1,
                },
            })),
            new ConcurrencyOptions
            {
                MaxConcurrentJobs = 3,
                QueueCapacity = 3,
                PerTargetRateLimit = 1,
                PerTargetRatePeriodMilliseconds = 40,
                PerTargetBurstSize = 1,
                ShutdownGracePeriodSeconds = 1,
            });

        await service.StartAsync(CancellationToken.None);
        var summary = await service.Completion.WaitAsync(TimeSpan.FromSeconds(5));
        var starts = runner.StartTimes
            .OrderBy(timestamp => timestamp)
            .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(summary.CompletedJobs, Is.EqualTo(3));
            Assert.That(starts, Has.Length.EqualTo(3));
            Assert.That(starts[2] - starts[0], Is.GreaterThanOrEqualTo(70));
        });
    }

    [Test]
    public async Task StopAsync_CancelsActiveWorkAfterShutdownGracePeriod()
    {
        var runner = new MeasuringJobRunner(TimeSpan.FromSeconds(30));
        var service = CreateService(
            [JobInputResult.Valid(1, CreateJob("shutdown-job", "target"))],
            runner,
            new NoOpTargetRateLimiter(),
            new ConcurrencyOptions
            {
                MaxConcurrentJobs = 1,
                QueueCapacity = 1,
                PerTargetRateLimit = 10,
                PerTargetRatePeriodMilliseconds = 1,
                PerTargetBurstSize = 10,
                ShutdownGracePeriodSeconds = 1,
            });

        await service.StartAsync(CancellationToken.None);
        await runner.WaitForStartAsync(TimeSpan.FromSeconds(2));
        var elapsed = Stopwatch.StartNew();
        using var stopBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await service.StopAsync(stopBudget.Token);
        var summary = await service.Completion.WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Multiple(() =>
        {
            Assert.That(elapsed.Elapsed, Is.LessThan(TimeSpan.FromSeconds(4)));
            Assert.That(summary.CancelledJobs, Is.GreaterThanOrEqualTo(1));
            Assert.That(summary.ExitCode, Is.EqualTo(WorkerExitCode.Cancelled));
        });
    }


    private static AutomationWorkerService CreateService(
        IReadOnlyCollection<JobInputResult> inputs,
        IJobRunner runner,
        ITargetRateLimiter targetRateLimiter,
        ConcurrencyOptions concurrency)
    {
        var options = Options.Create(new AutomationWorkerOptions { Concurrency = concurrency });
        return new AutomationWorkerService(
            new StaticJobSource(inputs),
            runner,
            targetRateLimiter,
            options,
            new TestApplicationLifetime(),
            NullLogger<AutomationWorkerService>.Instance);
    }

    private static AutomationJob CreateJob(string jobId, string target) =>
        new(jobId, target, new Uri("http://localhost/catalog"), MaxPages: 1);

    private sealed class StaticJobSource(IReadOnlyCollection<JobInputResult> inputs) : IJobSource
    {
        public async IAsyncEnumerable<JobInputResult> ReadAllAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            foreach (var input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return input;
                await Task.Yield();
            }
        }
    }

    private sealed class MeasuringJobRunner(TimeSpan delay) : IJobRunner
    {
        private int activeJobs;
        private int maxActiveJobs;
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ConcurrentQueue<long> StartTimes { get; } = new();

        public int MaxActiveJobs => Volatile.Read(ref maxActiveJobs);

        public async Task WaitForStartAsync(TimeSpan timeout) => await started.Task.WaitAsync(timeout);

        public async Task<JobRunResult> RunAsync(
            AutomationJob job,
            CancellationToken cancellationToken)
        {
            StartTimes.Enqueue(Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency);
            started.TrySetResult();
            var active = Interlocked.Increment(ref activeJobs);
            UpdateMax(active);
            try
            {
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken);
                }

                return new JobRunResult(job.JobId, WasAlreadyCompleted: false, LastCompletedPage: job.MaxPages);
            }
            finally
            {
                Interlocked.Decrement(ref activeJobs);
            }
        }

        private void UpdateMax(int active)
        {
            while (true)
            {
                var current = Volatile.Read(ref maxActiveJobs);
                if (active <= current || Interlocked.CompareExchange(ref maxActiveJobs, active, current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class NoOpTargetRateLimiter : ITargetRateLimiter
    {
        public Task WaitAsync(string target, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TestApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => CancellationToken.None;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication()
        {
        }
    }
}
