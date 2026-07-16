using Automation.Application.Abstractions;
using Automation.Core.Checkpoints;
using Automation.Core.Jobs;
using Automation.Core.Results;

namespace Automation.Application;

public sealed class JobRunner(
    IJobRepository jobs,
    ICheckpointRepository checkpoints,
    IBrowserCatalogSessionFactory sessions,
    IFailureArtifactWriter failureArtifacts)
{
    public async Task<JobRunResult> RunAsync(
        AutomationJob job,
        CancellationToken cancellationToken)
    {
        job.Validate();

        var existing = await jobs.FindAsync(job.JobId, cancellationToken);
        if (existing?.Status == JobStatus.Completed)
        {
            var completedCheckpoint = await checkpoints.FindAsync(job.JobId, cancellationToken);
            return new JobRunResult(
                job.JobId,
                WasAlreadyCompleted: true,
                completedCheckpoint?.LastCompletedPage ?? job.MaxPages);
        }

        await jobs.MarkRunningAsync(job.JobId, cancellationToken);
        var checkpoint = await checkpoints.FindAsync(job.JobId, cancellationToken);
        var lastCompletedPage = checkpoint?.LastCompletedPage ?? 0;

        try
        {
            await using var session = await sessions.OpenAsync(job, cancellationToken);

            for (var page = lastCompletedPage + 1; page <= job.MaxPages; page++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var items = await session.ExtractPageAsync(page, cancellationToken);

                // Storage adapters must upsert by (jobId, externalId). Save the
                // checkpoint only after the item write succeeds.
                await jobs.StoreItemsAsync(job.JobId, items, cancellationToken);
                lastCompletedPage = page;
                await checkpoints.SaveAsync(
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

