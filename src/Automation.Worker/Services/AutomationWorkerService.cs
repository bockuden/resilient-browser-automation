using Automation.Application;
using Automation.Worker.Jobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Automation.Worker.Services;

public sealed class AutomationWorkerService(
    IJobSource jobSource,
    JobRunner jobRunner,
    IHostApplicationLifetime applicationLifetime,
    ILogger<AutomationWorkerService> logger) : BackgroundService
{
    private readonly TaskCompletionSource<WorkerRunSummary> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<WorkerRunSummary> Completion => completion.Task;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var summary = new WorkerRunSummary();

        try
        {
            await foreach (var input in jobSource.ReadAllAsync(stoppingToken))
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

                var job = input.Job!;
                using var scope = logger.BeginScope(new Dictionary<string, object?>
                {
                    ["jobId"] = job.JobId,
                    ["target"] = job.Target,
                    ["executionAttempt"] = 1,
                });

                try
                {
                    var result = await jobRunner.RunAsync(job, stoppingToken);
                    summary.MarkCompleted();
                    logger.LogInformation(
                        new EventId(1001, "JobCompleted"),
                        "Job completed. AlreadyCompleted: {WasAlreadyCompleted}; LastCompletedPage: {LastCompletedPage}",
                        result.WasAlreadyCompleted,
                        result.LastCompletedPage);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    summary.MarkCancelled();
                    logger.LogInformation(new EventId(1002, "JobCancelled"), "Job cancelled.");
                    break;
                }
                catch (Exception error)
                {
                    summary.MarkFailed();
                    logger.LogError(new EventId(1003, "JobFailed"), error, "Job failed.");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
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
            completion.TrySetResult(summary);
            applicationLifetime.StopApplication();
        }
    }
}
