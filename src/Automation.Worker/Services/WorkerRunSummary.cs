namespace Automation.Worker.Services;

public sealed class WorkerRunSummary
{
    public int CompletedJobs { get; private set; }

    public int RejectedJobs { get; private set; }

    public int FailedJobs { get; private set; }

    public int CancelledJobs { get; private set; }

    public WorkerExitCode ExitCode => FailedJobs > 0
        ? WorkerExitCode.Failed
        : CancelledJobs > 0
            ? WorkerExitCode.Cancelled
            : RejectedJobs > 0
                ? WorkerExitCode.RejectedInput
                : WorkerExitCode.Success;

    public void MarkCompleted() => CompletedJobs++;

    public void MarkRejected() => RejectedJobs++;

    public void MarkFailed() => FailedJobs++;

    public void MarkCancelled() => CancelledJobs++;
}

