using System.Text.Json;
using Automation.Core.Jobs;

namespace Automation.Worker.Jobs;

public sealed class JobLineParser
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public JobInputResult Parse(string line, int lineNumber)
    {
        line = line.TrimStart('\uFEFF');

        if (string.IsNullOrWhiteSpace(line))
        {
            return JobInputResult.Invalid(lineNumber, "A JSON Lines record must not be blank.");
        }

        try
        {
            var input = JsonSerializer.Deserialize<JobInput>(line, SerializerOptions);
            if (input is null)
            {
                return JobInputResult.Invalid(lineNumber, "The JSON record is empty.");
            }

            if (!Uri.TryCreate(input.StartUrl, UriKind.Absolute, out var startUrl))
            {
                return JobInputResult.Invalid(lineNumber, "StartUrl must be an absolute URI.");
            }

            var job = new AutomationJob(input.JobId ?? string.Empty, input.Target ?? string.Empty, startUrl, input.MaxPages);
            job.Validate();
            return JobInputResult.Valid(lineNumber, job);
        }
        catch (JsonException error)
        {
            return JobInputResult.Invalid(lineNumber, $"Invalid JSON: {error.Message}");
        }
        catch (ArgumentException error)
        {
            return JobInputResult.Invalid(lineNumber, error.Message);
        }
    }

    private sealed record JobInput(string? JobId, string? Target, string? StartUrl, int MaxPages);
}
