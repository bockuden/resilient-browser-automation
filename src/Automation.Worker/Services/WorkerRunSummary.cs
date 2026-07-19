namespace Automation.Worker.Services;

public sealed class WorkerRunSummary
{
    private int completedJobs;
    private int rejectedJobs;
    private int failedJobs;
    private int cancelledJobs;

    public int CompletedJobs => Volatile.Read(ref completedJobs);

    public int RejectedJobs => Volatile.Read(ref rejectedJobs);

    public int FailedJobs => Volatile.Read(ref failedJobs);

    public int CancelledJobs => Volatile.Read(ref cancelledJobs);

    public WorkerExitCode ExitCode => FailedJobs > 0
        ? WorkerExitCode.Failed
        : CancelledJobs > 0
            ? WorkerExitCode.Cancelled
            : RejectedJobs > 0
                ? WorkerExitCode.RejectedInput
                : WorkerExitCode.Success;

    public void MarkCompleted() => Interlocked.Increment(ref completedJobs);

    public void MarkRejected() => Interlocked.Increment(ref rejectedJobs);

    public void MarkFailed() => Interlocked.Increment(ref failedJobs);

    public void MarkCancelled() => Interlocked.Increment(ref cancelledJobs);
}
