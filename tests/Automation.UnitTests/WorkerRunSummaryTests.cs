using Automation.Worker.Services;

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
}

