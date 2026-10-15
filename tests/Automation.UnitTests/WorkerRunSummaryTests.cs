using Automation.Application.Retry;
using Automation.Worker.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Automation.UnitTests;

[TestFixture]
public sealed class WorkerRunSummaryTests
{
    [Test]
    public void ExitCode_PrioritizesFailuresOverOtherOutcomes()
    {
        var summary = new WorkerRunSummary();
        summary.MarkCompleted();
        summary.MarkRejected();
        summary.MarkCancelled();
        summary.MarkFailed();

        Assert.That(summary.ExitCode, Is.EqualTo(WorkerExitCode.Failed));
    }

    [Test]
    public void MarkRetryScheduled_IncrementsRetryCount()
    {
        var summary = new WorkerRunSummary();

        summary.MarkRetryScheduled();
        summary.MarkRetryScheduled();

        Assert.That(summary.RetriesScheduled, Is.EqualTo(2));
    }

    [Test]
    public void LoggingRetryObserver_AddsRetriesToRunSummary()
    {
        var summary = new WorkerRunSummary();
        var observer = new LoggingRetryObserver(NullLogger<LoggingRetryObserver>.Instance, summary);

        observer.OnRetry(new RetryDecision(
            NextAttempt: 2,
            Reason: "HTTP 429",
            Delay: TimeSpan.FromSeconds(1),
            RemainingBudget: TimeSpan.FromSeconds(5)));

        Assert.That(summary.RetriesScheduled, Is.EqualTo(1));
    }
}
