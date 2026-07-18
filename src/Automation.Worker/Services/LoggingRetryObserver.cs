using Automation.Application.Retry;
using Microsoft.Extensions.Logging;

namespace Automation.Worker.Services;

public sealed class LoggingRetryObserver(ILogger<LoggingRetryObserver> logger) : IRetryObserver
{
    public void OnRetry(RetryDecision decision)
    {
        logger.LogWarning(
            new EventId(2001, "RetryScheduled"),
            "Retry {NextAttempt} scheduled after {DelayMilliseconds} ms. Reason: {Reason}. Remaining budget: {RemainingBudgetMilliseconds} ms.",
            decision.NextAttempt,
            decision.Delay.TotalMilliseconds,
            decision.Reason,
            decision.RemainingBudget.TotalMilliseconds);
    }
}
