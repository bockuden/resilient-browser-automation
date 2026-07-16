namespace Automation.Application;

public sealed record JobRunResult(
    string JobId,
    bool WasAlreadyCompleted,
    int LastCompletedPage);

