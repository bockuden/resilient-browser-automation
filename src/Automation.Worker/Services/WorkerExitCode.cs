namespace Automation.Worker.Services;

public enum WorkerExitCode
{
    Success = 0,
    HostConfigurationError = 1,
    RejectedInput = 2,
    Failed = 3,
    Cancelled = 4,
}

