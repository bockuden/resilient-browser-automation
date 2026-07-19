using System.Text.Json;
using System.Text.RegularExpressions;
using Automation.Application.Abstractions;
using Automation.Worker.Configuration;
using Microsoft.Extensions.Logging;

namespace Automation.Worker.Artifacts;

public sealed class FileFailureArtifactWriter(
    ArtifactOptions options,
    ILogger<FileFailureArtifactWriter> logger) : IFailureArtifactWriter
{
    public async Task<string?> CaptureAsync(string jobId, Exception error, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(options.RootPath);
        Directory.CreateDirectory(root);
        var jobDirectory = Path.Combine(root, SafeName(jobId));
        Directory.CreateDirectory(jobDirectory);
        var attemptDirectory = Path.Combine(jobDirectory, NextAttempt(jobDirectory).ToString());
        Directory.CreateDirectory(attemptDirectory);

        var metadata = new FailureMetadata(
            error.GetType().Name,
            Redact(error.Message),
            DateTimeOffset.UtcNow);
        var temporaryPath = Path.Combine(attemptDirectory, "error.json.tmp");
        var finalPath = Path.Combine(attemptDirectory, "error.json");
        await using (var stream = File.Create(temporaryPath))
        {
            await JsonSerializer.SerializeAsync(stream, metadata, cancellationToken: cancellationToken);
        }

        File.Move(temporaryPath, finalPath, overwrite: true);
        ApplyRetention(root);
        logger.LogInformation(new EventId(3001, "FailureArtifactsCreated"), "Failure artifacts created in {ArtifactDirectory}.", attemptDirectory);
        return attemptDirectory;
    }

    private void ApplyRetention(string root)
    {
        var cutoff = DateTime.UtcNow.AddDays(-options.RetentionDays);
        var directories = Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories)
            .Select(path => new DirectoryInfo(path))
            .Where(directory => directory.Parent?.Parent?.FullName == root)
            .OrderBy(directory => directory.LastWriteTimeUtc)
            .ToList();

        foreach (var directory in directories.Where(directory => directory.LastWriteTimeUtc < cutoff))
        {
            directory.Delete(recursive: true);
        }

        var remaining = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Sum(file => new FileInfo(file).Length);
        var limitBytes = options.MaximumTotalSizeMegabytes * 1024L * 1024L;
        foreach (var directory in directories.Where(directory => directory.Exists))
        {
            if (remaining <= limitBytes)
            {
                break;
            }

            var size = Directory.EnumerateFiles(directory.FullName, "*", SearchOption.AllDirectories)
                .Sum(file => new FileInfo(file).Length);
            directory.Delete(recursive: true);
            remaining -= size;
        }
    }

    private static int NextAttempt(string jobDirectory) =>
        Directory.EnumerateDirectories(jobDirectory)
            .Select(Path.GetFileName)
            .Select(name => int.TryParse(name, out var attempt) ? attempt : 0)
            .DefaultIfEmpty(0)
            .Max() + 1;

    private static string SafeName(string value) => Regex.Replace(value, "[^a-zA-Z0-9._-]", "_");

    private static string Redact(string value) => Regex.Replace(
        value,
        "(?i)((?:password|token|authorization|cookie|secret)=)[^&\\s]+",
        "$1[REDACTED]");

    private sealed record FailureMetadata(string ErrorType, string Message, DateTimeOffset OccurredAt);
}
