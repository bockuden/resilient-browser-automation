using Automation.Application.Abstractions;
using Automation.Core.Checkpoints;
using Automation.Core.Jobs;
using Automation.Core.Results;

namespace Automation.Application;

public sealed class JobRunner(
    IJobRepository jobs,
    ICheckpointRepository checkpoints,
    IJobPageCommitter pageCommitter,
    IBrowserCatalogSessionFactory sessions,
    IFailureArtifactWriter failureArtifacts)
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

        try
        {
            await using var session = await sessions.OpenAsync(job, cancellationToken);

            for (var page = lastCompletedPage + 1; page <= job.MaxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var items = await session.ExtractPageAsync(page, cancellationToken);

                lastCompletedPage = page;
                await pageCommitter.CommitPageAsync(
                    job.JobId,
                    items,
                    new JobCheckpoint(job.JobId, page, DateTimeOffset.UtcNow),
                    cancellationToken);
            }

            await jobs.MarkCompletedAsync(job.JobId, cancellationToken);
            return new JobRunResult(job.JobId, WasAlreadyCompleted: false, lastCompletedPage);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await jobs.MarkCancelledAsync(job.JobId, CancellationToken.None);
            throw;
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
