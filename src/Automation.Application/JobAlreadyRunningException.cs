namespace Automation.Application;

public sealed class JobAlreadyRunningException(string jobId)
    : InvalidOperationException($"Job '{jobId}' is already running.");
