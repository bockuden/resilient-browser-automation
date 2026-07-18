namespace Automation.Application.Retry;

public sealed class TransientFailureClassifier
{
    public bool IsTransient(Exception error) => error switch
    {
        BrowserOperationException { StatusCode: 408 or 429 or 502 or 503 or 504 } => true,
        TimeoutException => true,
        _ when error.GetType().Name == "PlaywrightException" &&
            (error.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
             error.Message.Contains("browser has been closed", StringComparison.OrdinalIgnoreCase) ||
             error.Message.Contains("Target page, context or browser has been closed", StringComparison.OrdinalIgnoreCase)) => true,
        _ => false,
    };

    public TimeSpan? GetRetryAfter(Exception error) =>
        error is BrowserOperationException { RetryAfter: not null } browserError
            ? browserError.RetryAfter
            : null;
}
