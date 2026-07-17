using Microsoft.Data.Sqlite;

namespace Automation.Storage;

public sealed class SqliteConnectionFactory
{
    private readonly string connectionString;

    public SqliteConnectionFactory(string connectionString)
    {
        var builder = new SqliteConnectionStringBuilder(connectionString);
        if (string.IsNullOrWhiteSpace(builder.DataSource))
        {
            throw new ArgumentException("SQLite connection string must specify Data Source.", nameof(connectionString));
        }

        if (builder.DataSource is not ":memory:" && !Uri.IsWellFormedUriString(builder.DataSource, UriKind.Absolute))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(builder.DataSource));
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        builder.ForeignKeys = true;
        builder.DefaultTimeout = 5;
        this.connectionString = builder.ToString();
    }

    public SqliteConnection Create() => new(connectionString);
}
