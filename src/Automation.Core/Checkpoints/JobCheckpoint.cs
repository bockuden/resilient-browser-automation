namespace Automation.Core.Checkpoints;

public sealed record JobCheckpoint(
    string JobId,
    int LastCompletedPage,
    DateTimeOffset SavedAt);

