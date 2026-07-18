using Automation.Application.Abstractions;
using Automation.Core.Checkpoints;
using Automation.Core.Jobs;
using Automation.Core.Results;
using Automation.Application.Retry;

namespace Automation.Application;

public sealed class JobRunner(
    IJobRepository jobs,
    ICheckpointRepository checkpoints,
    IJobPageCommitter pageCommitter,
    IBrowserCatalogSessionFactory sessions,
    IFailureArtifactWriter failureArtifacts,
    RetryExecutor retries,
    JobExecutionSettings executionSettings)
{
    public async Task<JobRunResult> RunAsync(
        AutomationJob job,
        CancellationToken cancellationToken)
    {
        job.Validate();

        var claim = await jobs.TryClaimAsync(job.JobId, cancellationToken);
        if (claim == JobClaimResult.AlreadyCompleted)
        {
            var completedCheckpoint = await checkpoints.FindAsync(job.JobId, cancellationToken);
            return new JobRunResult(
                job.JobId,
                WasAlreadyCompleted: true,
                completedCheckpoint?.LastCompletedPage ?? job.MaxPages);
        }

        if (claim == JobClaimResult.AlreadyRunning)
        {
            throw new JobAlreadyRunningException(job.JobId);
        }

        var checkpoint = await checkpoints.FindAsync(job.JobId, cancellationToken);
        var lastCompletedPage = checkpoint?.LastCompletedPage ?? 0;
        using var wholeJobCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wholeJobCancellation.CancelAfter(executionSettings.WholeJobTimeout);
        var deadline = DateTimeOffset.UtcNow + executionSettings.WholeJobTimeout;

        try
        {
            IBrowserCatalogSession? session = null;
            try
            {
                for (var page = lastCompletedPage + 1; page <= job.MaxPages; page++)
                {
                    var pageNumber = page;
                    var items = await retries.ExecuteAsync(
                        async (attempt, retryCancellationToken) =>
                        {
                            if (attempt > 1 || session is null)
                            {
                                if (session is not null)
                                {
                                    await session.DisposeAsync();
                                }

                                session = await sessions.OpenAsync(job, retryCancellationToken);
                            }

                            return await session.ExtractPageAsync(pageNumber, retryCancellationToken);
                        },
                        deadline,
                        wholeJobCancellation.Token);

                    await pageCommitter.CommitPageAsync(
                        job.JobId,
                        items,
                        new JobCheckpoint(job.JobId, pageNumber, DateTimeOffset.UtcNow),
                        wholeJobCancellation.Token);
                    lastCompletedPage = pageNumber;
                }
            }
            finally
            {
                if (session is not null)
                {
                    await session.DisposeAsync();
                }
            }

            await jobs.MarkCompletedAsync(job.JobId, cancellationToken);
            return new JobRunResult(job.JobId, WasAlreadyCompleted: false, lastCompletedPage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await jobs.MarkCancelledAsync(job.JobId, CancellationToken.None);
            throw;
        }
        catch (OperationCanceledException) when (wholeJobCancellation.IsCancellationRequested)
        {
            var timeout = new TimeoutException("The whole-job timeout was exceeded.");
            await failureArtifacts.CaptureAsync(job.JobId, timeout, CancellationToken.None);
            await jobs.MarkFailedAsync(job.JobId, timeout.GetType().Name, timeout.Message, CancellationToken.None);
            throw timeout;
        }
        catch (Exception error)
        {
            await failureArtifacts.CaptureAsync(job.JobId, error, CancellationToken.None);
            await jobs.MarkFailedAsync(
                job.JobId,
                error.GetType().Name,
                error.Message,
                CancellationToken.None);
            throw;
        }
    }
}
