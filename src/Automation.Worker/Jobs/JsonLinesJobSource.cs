namespace Automation.Worker.Jobs;

public sealed class JsonLinesJobSource(WorkerCommandLine commandLine, JobLineParser parser) : IJobSource
{
    public async IAsyncEnumerable<JobInputResult> ReadAllAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(commandLine.JobsPath) || commandLine.JobsPath == "-")
        {
            await foreach (var result in ReadAsync(Console.In, cancellationToken))
            {
                yield return result;
            }

            yield break;
        }

        using var reader = File.OpenText(commandLine.JobsPath);
        await foreach (var result in ReadAsync(reader, cancellationToken))
        {
            yield return result;
        }
    }

    private async IAsyncEnumerable<JobInputResult> ReadAsync(
        TextReader reader,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var lineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            yield return parser.Parse(line, lineNumber);
        }
    }
}
