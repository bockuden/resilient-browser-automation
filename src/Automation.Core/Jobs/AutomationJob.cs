namespace Automation.Core.Jobs;

public sealed record AutomationJob(
    string JobId,
    string Target,
    Uri StartUrl,
    int MaxPages)
{
    public const int MaximumAllowedPages = 100;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(JobId))
        {
            throw new ArgumentException("JobId is required.", nameof(JobId));
        }

        if (string.IsNullOrWhiteSpace(Target))
        {
            throw new ArgumentException("Target is required.", nameof(Target));
        }

        if (!StartUrl.IsAbsoluteUri ||
            (StartUrl.Scheme != Uri.UriSchemeHttp && StartUrl.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("StartUrl must be an absolute HTTP(S) URL.", nameof(StartUrl));
        }

        if (MaxPages is < 1 or > MaximumAllowedPages)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxPages),
                $"MaxPages must be between 1 and {MaximumAllowedPages}.");
        }
    }
}

