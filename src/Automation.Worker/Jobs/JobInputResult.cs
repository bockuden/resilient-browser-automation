using Automation.Core.Jobs;

namespace Automation.Worker.Jobs;

public sealed record JobInputResult(
    int LineNumber,
    AutomationJob? Job,
    string? Error)
{
    public bool IsValid => Job is not null;

    public static JobInputResult Valid(int lineNumber, AutomationJob job) => new(lineNumber, job, null);

    public static JobInputResult Invalid(int lineNumber, string error) => new(lineNumber, null, error);
}

