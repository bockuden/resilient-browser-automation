namespace Automation.Worker.Services;

public interface ITargetRateLimiter
{
    Task WaitAsync(string target, CancellationToken cancellationToken);
}
