namespace Automation.Worker.Jobs;

public sealed record WorkerCommandLine(string? JobsPath)
{
    public static WorkerCommandLine Parse(string[] args)
    {
        string? jobsPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument.StartsWith("--jobs=", StringComparison.Ordinal))
            {
                jobsPath = argument["--jobs=".Length..];
                continue;
            }

            if (argument == "--jobs")
            {
                if (index + 1 >= args.Length)
                {
                    throw new ArgumentException("--jobs requires a JSON Lines file path or '-'.");
                }

                jobsPath = args[++index];
            }
        }

        return new WorkerCommandLine(jobsPath);
    }
}

