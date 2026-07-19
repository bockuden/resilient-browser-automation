using Automation.Application;
using Automation.Application.Abstractions;
using Automation.Worker.Configuration;
using Automation.Worker.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Threading.Channels;

namespace Automation.Worker.Services;

public sealed class AutomationWorkerService(
    IJobSource jobSource,
    IJobRunner jobRunner,
    ITargetRateLimiter targetRateLimiter,
    IOptions<AutomationWorkerOptions> options,
    IHostApplicationLifetime applicationLifetime,
    ILogger<AutomationWorkerService> logger) : BackgroundService
{
    private readonly TaskCompletionSource<WorkerRunSummary> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrencyOptions concurrency = options.Value.Concurrency;

    public Task<WorkerRunSummary> Completion => completion.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var summary = new WorkerRunSummary();
        using var intakeCancellation = new CancellationTokenSource();
        using var jobCancellation = new CancellationTokenSource();
        using var shutdownRegistration = stoppingToken.Register(() =>
        {
            intakeCancellation.Cancel();
            jobCancellation.CancelAfter(TimeSpan.FromSeconds(concurrency.ShutdownGracePeriodSeconds));
        });
        var channel = Channel.CreateBounded<JobInputResult>(new BoundedChannelOptions(concurrency.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = false,
            SingleWriter = true,
        });
        var producer = ProduceAsync(channel.Writer, summary, intakeCancellation.Token);
        var consumers = Enumerable.Range(0, concurrency.MaxConcurrentJobs)
            .Select(workerId => ConsumeAsync(workerId + 1, channel.Reader, summary, jobCancellation.Token))
            .ToArray();

        try
        {
            await producer;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || intakeCancellation.IsCancellationRequested)
        {
            summary.MarkCancelled();
            logger.LogInformation(new EventId(1004, "WorkerCancelled"), "Worker cancellation requested.");
        }
        catch (Exception error)
        {
            summary.MarkFailed();
            logger.LogCritical(new EventId(1005, "WorkerFailed"), error, "Worker input processing failed.");
        }
        finally
        {
            channel.Writer.TryComplete();
        }

        try
        {
            await Task.WhenAll(consumers);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested || jobCancellation.IsCancellationRequested)
        {
            logger.LogInformation(new EventId(1004, "WorkerCancelled"), "Worker cancellation requested.");
        }
        finally
        {
            completion.TrySetResult(summary);
            applicationLifetime.StopApplication();
        }
    }

    private async Task ProduceAsync(
        ChannelWriter<JobInputResult> writer,
        WorkerRunSummary summary,
        CancellationToken cancellationToken)
    {
        await foreach (var input in jobSource.ReadAllAsync(cancellationToken))
        {
            if (!input.IsValid)
            {
                summary.MarkRejected();
                logger.LogWarning(
                    new EventId(1000, "JobRejected"),
                    "Rejected JSON Lines record {LineNumber}: {Reason}",
                    input.LineNumber,
                    input.Error);
                continue;
            }

            await writer.WriteAsync(input, cancellationToken);
        }
    }

    private async Task ConsumeAsync(
        int workerId,
        ChannelReader<JobInputResult> reader,
        WorkerRunSummary summary,
        CancellationToken cancellationToken)
    {
        await foreach (var input in reader.ReadAllAsync(cancellationToken))
        {
            await ProcessJobAsync(workerId, input, summary, cancellationToken);
        }
    }

    private async Task ProcessJobAsync(
        int workerId,
        JobInputResult input,
        WorkerRunSummary summary,
        CancellationToken cancellationToken)
    {
        var job = input.Job!;
        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["jobId"] = job.JobId,
            ["target"] = job.Target,
            ["executionAttempt"] = 1,
            ["workerId"] = workerId,
        });

        try
        {
            await targetRateLimiter.WaitAsync(job.Target, cancellationToken);
            var result = await jobRunner.RunAsync(job, cancellationToken);
            summary.MarkCompleted();
            logger.LogInformation(
                new EventId(1001, "JobCompleted"),
                "Job completed. AlreadyCompleted: {WasAlreadyCompleted}; LastCompletedPage: {LastCompletedPage}",
                result.WasAlreadyCompleted,
                result.LastCompletedPage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            summary.MarkCancelled();
            logger.LogInformation(new EventId(1002, "JobCancelled"), "Job cancelled.");
            throw;
        }
        catch (Exception error)
        {
            summary.MarkFailed();
            logger.LogError(new EventId(1003, "JobFailed"), error, "Job failed.");
        }
    }
}
