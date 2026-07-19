using System.Diagnostics.Metrics;

namespace Automation.Application;

public static class AutomationMetrics
{
    private static readonly Meter Meter = new("ResilientBrowserAutomation.Worker", "1.0.0");

    public static readonly Counter<long> JobsCompleted = Meter.CreateCounter<long>("automation.jobs.completed");
    public static readonly Counter<long> JobsSkipped = Meter.CreateCounter<long>("automation.jobs.skipped");
    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>("automation.jobs.failed");
    public static readonly Counter<long> JobsCancelled = Meter.CreateCounter<long>("automation.jobs.cancelled");
    public static readonly Counter<long> PagesCompleted = Meter.CreateCounter<long>("automation.pages.completed");
    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("automation.retries");
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "automation.jobs.duration",
        unit: "ms");
}
