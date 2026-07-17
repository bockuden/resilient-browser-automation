using System.Globalization;
using Automation.Application;
using Automation.Application.Abstractions;
using Automation.Core.Checkpoints;
using Automation.Core.Results;
using Microsoft.Data.Sqlite;

namespace Automation.Storage;

public sealed class SqliteAutomationRepository(
    SqliteConnectionFactory connections,
    TimeSpan staleRunningThreshold) : IJobRepository, ICheckpointRepository, IJobPageCommitter
{
    public async Task<JobExecution?> FindAsync(string jobId, CancellationToken cancellationToken)
    {
        using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT status, updated_at_unix_ms, error_code, error_message FROM jobs WHERE job_id = $jobId;";
        command.Parameters.AddWithValue("$jobId", jobId);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new JobExecution(
            jobId,
            ParseStatus(reader.GetString(0)),
            DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            reader.IsDBNull(3) ? null : reader.GetString(3));
    }

    public async Task<JobClaimResult> TryClaimAsync(string jobId, CancellationToken cancellationToken)
    {
        using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();
        var now = DateTimeOffset.UtcNow;

        using (var create = connection.CreateCommand())
        {
            create.Transaction = transaction;
            create.CommandText = "INSERT INTO jobs (job_id, status, updated_at_unix_ms) VALUES ($jobId, $pending, $updatedAt) ON CONFLICT(job_id) DO NOTHING;";
            create.Parameters.AddWithValue("$jobId", jobId);
            create.Parameters.AddWithValue("$pending", JobStatus.Pending.ToString());
            create.Parameters.AddWithValue("$updatedAt", now.ToUnixTimeMilliseconds());
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        JobStatus status;
        long updatedAt;
        using (var select = connection.CreateCommand())
        {
            select.Transaction = transaction;
            select.CommandText = "SELECT status, updated_at_unix_ms FROM jobs WHERE job_id = $jobId;";
            select.Parameters.AddWithValue("$jobId", jobId);
            using var reader = await select.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            status = ParseStatus(reader.GetString(0));
            updatedAt = reader.GetInt64(1);
        }

        if (status == JobStatus.Completed)
        {
            transaction.Commit();
            return JobClaimResult.AlreadyCompleted;
        }

        if (status == JobStatus.Running && DateTimeOffset.FromUnixTimeMilliseconds(updatedAt) > now - staleRunningThreshold)
        {
            transaction.Commit();
            return JobClaimResult.AlreadyRunning;
        }

        using (var claim = connection.CreateCommand())
        {
            claim.Transaction = transaction;
            claim.CommandText = """
                UPDATE jobs SET status = $running, updated_at_unix_ms = $updatedAt,
                    error_code = NULL, error_message = NULL WHERE job_id = $jobId;
                INSERT INTO job_attempts (job_id, started_at_unix_ms) VALUES ($jobId, $updatedAt);
                """;
            claim.Parameters.AddWithValue("$jobId", jobId);
            claim.Parameters.AddWithValue("$running", JobStatus.Running.ToString());
            claim.Parameters.AddWithValue("$updatedAt", now.ToUnixTimeMilliseconds());
            await claim.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
        return JobClaimResult.Claimed;
    }

    public async Task CommitPageAsync(string jobId, IReadOnlyCollection<CatalogItem> items, JobCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction();

        foreach (var item in items)
        {
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO catalog_items (job_id, external_id, name, price, source_url)
                VALUES ($jobId, $externalId, $name, $price, $sourceUrl)
                ON CONFLICT(job_id, external_id) DO UPDATE SET
                    name = excluded.name, price = excluded.price, source_url = excluded.source_url;
                """;
            insert.Parameters.AddWithValue("$jobId", jobId);
            insert.Parameters.AddWithValue("$externalId", item.ExternalId);
            insert.Parameters.AddWithValue("$name", item.Name);
            insert.Parameters.AddWithValue("$price", item.Price.ToString(CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("$sourceUrl", item.SourceUrl.AbsoluteUri);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        using (var saveCheckpoint = connection.CreateCommand())
        {
            saveCheckpoint.Transaction = transaction;
            saveCheckpoint.CommandText = """
                INSERT INTO checkpoints (job_id, last_completed_page, saved_at_unix_ms)
                VALUES ($jobId, $lastCompletedPage, $savedAt)
                ON CONFLICT(job_id) DO UPDATE SET
                    last_completed_page = excluded.last_completed_page,
                    saved_at_unix_ms = excluded.saved_at_unix_ms;
                """;
            saveCheckpoint.Parameters.AddWithValue("$jobId", checkpoint.JobId);
            saveCheckpoint.Parameters.AddWithValue("$lastCompletedPage", checkpoint.LastCompletedPage);
            saveCheckpoint.Parameters.AddWithValue("$savedAt", checkpoint.SavedAt.ToUnixTimeMilliseconds());
            await saveCheckpoint.ExecuteNonQueryAsync(cancellationToken);
        }

        transaction.Commit();
    }

    async Task<JobCheckpoint?> ICheckpointRepository.FindAsync(string jobId, CancellationToken cancellationToken)
    {
        using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT last_completed_page, saved_at_unix_ms FROM checkpoints WHERE job_id = $jobId;";
        command.Parameters.AddWithValue("$jobId", jobId);
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new JobCheckpoint(jobId, reader.GetInt32(0), DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1)))
            : null;
    }

    public Task SaveAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken) =>
        CommitPageAsync(checkpoint.JobId, [], checkpoint, cancellationToken);

    public Task MarkCompletedAsync(string jobId, CancellationToken cancellationToken) =>
        SetStatusAsync(jobId, JobStatus.Completed, null, null, cancellationToken);

    public Task MarkFailedAsync(string jobId, string errorCode, string errorMessage, CancellationToken cancellationToken) =>
        SetStatusAsync(jobId, JobStatus.Failed, errorCode, errorMessage, cancellationToken);

    public Task MarkCancelledAsync(string jobId, CancellationToken cancellationToken) =>
        SetStatusAsync(jobId, JobStatus.Cancelled, null, null, cancellationToken);

    public async Task<int> CountItemsAsync(string jobId, CancellationToken cancellationToken)
    {
        using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM catalog_items WHERE job_id = $jobId;";
        command.Parameters.AddWithValue("$jobId", jobId);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    private async Task SetStatusAsync(string jobId, JobStatus status, string? errorCode, string? errorMessage, CancellationToken cancellationToken)
    {
        using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE jobs SET status = $status, updated_at_unix_ms = $updatedAt,
                error_code = $errorCode, error_message = $errorMessage WHERE job_id = $jobId;
            UPDATE job_attempts SET finished_at_unix_ms = $updatedAt, outcome = $status
            WHERE attempt_id = (
                SELECT attempt_id FROM job_attempts
                WHERE job_id = $jobId AND finished_at_unix_ms IS NULL
                ORDER BY attempt_id DESC LIMIT 1
            );
            """;
        command.Parameters.AddWithValue("$jobId", jobId);
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$errorCode", (object?)errorCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$errorMessage", (object?)errorMessage ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static JobStatus ParseStatus(string value) => Enum.Parse<JobStatus>(value, ignoreCase: false);
}
