using Microsoft.Extensions.Options;

namespace Automation.Worker.Configuration;

public sealed class AutomationWorkerOptionsValidator : IValidateOptions<AutomationWorkerOptions>
{
    public ValidateOptionsResult Validate(string? name, AutomationWorkerOptions options)
    {
        var errors = new List<string>();

        if (options.Browser.NavigationTimeoutSeconds <= 0)
        {
            errors.Add("Automation:Browser:NavigationTimeoutSeconds must be greater than zero.");
        }

        if (options.Browser.OperationTimeoutSeconds <= 0)
        {
            errors.Add("Automation:Browser:OperationTimeoutSeconds must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Browser.DemoUsername) || string.IsNullOrWhiteSpace(options.Browser.DemoPassword))
        {
            errors.Add("Automation:Browser demo credentials are required.");
        }

        if (options.Retry.MaxAttempts is < 1 or > 20)
        {
            errors.Add("Automation:Retry:MaxAttempts must be between 1 and 20.");
        }

        if (options.Retry.BaseDelayMilliseconds < 0 ||
            options.Retry.MaxDelayMilliseconds < options.Retry.BaseDelayMilliseconds)
        {
            errors.Add("Automation retry delays must be non-negative and max delay must not be less than base delay.");
        }

        if (options.Timeouts.WholeJobTimeoutSeconds <= 0)
        {
            errors.Add("Automation:Timeouts:WholeJobTimeoutSeconds must be greater than zero.");
        }

        if (options.Concurrency.MaxConcurrentJobs <= 0)
        {
            errors.Add("Automation:Concurrency:MaxConcurrentJobs must be greater than zero.");
        }

        if (options.Concurrency.QueueCapacity <= 0)
        {
            errors.Add("Automation:Concurrency:QueueCapacity must be greater than zero.");
        }

        if (options.Concurrency.PerTargetRateLimit <= 0 ||
            options.Concurrency.PerTargetRatePeriodMilliseconds <= 0 ||
            options.Concurrency.PerTargetBurstSize <= 0)
        {
            errors.Add("Automation per-target rate limit values must be greater than zero.");
        }

        if (options.Concurrency.PerTargetBurstSize < options.Concurrency.PerTargetRateLimit)
        {
            errors.Add("Automation:Concurrency:PerTargetBurstSize must not be less than PerTargetRateLimit.");
        }

        if (options.Concurrency.ShutdownGracePeriodSeconds <= 0)
        {
            errors.Add("Automation:Concurrency:ShutdownGracePeriodSeconds must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Storage.ConnectionString))
        {
            errors.Add("Automation:Storage:ConnectionString is required.");
        }

        if (options.Storage.StaleRunningJobSeconds <= 0)
        {
            errors.Add("Automation:Storage:StaleRunningJobSeconds must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Artifacts.RootPath))
        {
            errors.Add("Automation:Artifacts:RootPath is required.");
        }

        if (options.Artifacts.RetentionDays <= 0 || options.Artifacts.MaximumTotalSizeMegabytes <= 0)
        {
            errors.Add("Automation artifact retention values must be greater than zero.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
