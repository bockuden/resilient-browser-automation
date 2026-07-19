namespace Automation.Storage;

public sealed class SqliteMigrator(SqliteConnectionFactory connections)
{
    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        using var connection = connections.Create();
        await connection.OpenAsync(cancellationToken);

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_versions (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_at_unix_ms INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS jobs (
                job_id TEXT NOT NULL PRIMARY KEY,
                status TEXT NOT NULL,
                updated_at_unix_ms INTEGER NOT NULL,
                error_code TEXT NULL,
                error_message TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS checkpoints (
                job_id TEXT NOT NULL PRIMARY KEY REFERENCES jobs(job_id),
                last_completed_page INTEGER NOT NULL,
                saved_at_unix_ms INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS catalog_items (
                job_id TEXT NOT NULL REFERENCES jobs(job_id),
                external_id TEXT NOT NULL,
                name TEXT NOT NULL,
                price TEXT NOT NULL,
                page_number INTEGER NOT NULL,
                source_url TEXT NOT NULL,
                PRIMARY KEY (job_id, external_id)
            );

            CREATE TABLE IF NOT EXISTS job_attempts (
                attempt_id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL REFERENCES jobs(job_id),
                started_at_unix_ms INTEGER NOT NULL,
                finished_at_unix_ms INTEGER NULL,
                outcome TEXT NULL
            );

            INSERT OR IGNORE INTO schema_versions (version, applied_at_unix_ms)
            VALUES (1, unixepoch('subsec') * 1000);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        await EnsureCatalogItemPageNumberAsync(connection, cancellationToken);
    }

    private static async Task EnsureCatalogItemPageNumberAsync(
        Microsoft.Data.Sqlite.SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var hasPageNumber = false;
        using (var columns = connection.CreateCommand())
        {
            columns.CommandText = "PRAGMA table_info(catalog_items);";
            using var reader = await columns.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(reader.GetString(1), "page_number", StringComparison.Ordinal))
                {
                    hasPageNumber = true;
                    break;
                }
            }
        }

        if (!hasPageNumber)
        {
            using var migration = connection.CreateCommand();
            migration.CommandText = "ALTER TABLE catalog_items ADD COLUMN page_number INTEGER NOT NULL DEFAULT 1;";
            await migration.ExecuteNonQueryAsync(cancellationToken);
        }

        using var version = connection.CreateCommand();
        version.CommandText = "INSERT OR IGNORE INTO schema_versions (version, applied_at_unix_ms) VALUES (2, unixepoch('subsec') * 1000);";
        await version.ExecuteNonQueryAsync(cancellationToken);
    }
}
