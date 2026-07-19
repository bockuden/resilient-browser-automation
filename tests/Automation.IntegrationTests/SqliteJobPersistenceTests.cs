using Automation.Application;
using Automation.Application.Abstractions;
using Automation.Application.Retry;
using Automation.Core.Checkpoints;
using Automation.Core.Jobs;
using Automation.Core.Results;
using Automation.Storage;

namespace Automation.IntegrationTests;

[TestFixture]
public sealed class SqliteJobPersistenceTests
{
    [Test]
    public async Task CompletedJob_IsNotOpenedAgain()
    {
        await using var database = await TestDatabase.CreateAsync();
        var factory = new RecordingSessionFactory(_ => [Item("item-1")]);
        var runner = CreateRunner(database.Repository, factory);
        var job = Job("completed-job", maxPages: 1);

        var first = await runner.RunAsync(job, CancellationToken.None);
        var second = await runner.RunAsync(job, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(first.WasAlreadyCompleted, Is.False);
            Assert.That(second.WasAlreadyCompleted, Is.True);
            Assert.That(factory.OpenCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Replay_UpsertsItemsBeforeCheckpoint()
    {
        await using var database = await TestDatabase.CreateAsync();
        const string jobId = "replay-job";
        await database.Repository.TryClaimAsync(jobId, CancellationToken.None);

        await database.Repository.CommitPageAsync(jobId, [Item("item-1")], new JobCheckpoint(jobId, 1, DateTimeOffset.UtcNow), CancellationToken.None);
        await database.Repository.CommitPageAsync(jobId, [Item("item-1")], new JobCheckpoint(jobId, 1, DateTimeOffset.UtcNow), CancellationToken.None);

        var checkpoint = await ((ICheckpointRepository)database.Repository).FindAsync(jobId, CancellationToken.None);
        var itemCount = await database.Repository.CountItemsAsync(jobId, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(itemCount, Is.EqualTo(1));
            Assert.That(checkpoint!.LastCompletedPage, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task FailedJob_ResumesFromTheNextPageAfterCheckpoint()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstFactory = new RecordingSessionFactory(page =>
            page == 3 ? throw new InvalidOperationException("simulated browser failure") : [Item($"item-{page}")]);
        var job = Job("resume-job", maxPages: 3);

        Assert.ThrowsAsync<InvalidOperationException>(async () => await CreateRunner(database.Repository, firstFactory).RunAsync(job, CancellationToken.None));
        var checkpoint = await ((ICheckpointRepository)database.Repository).FindAsync(job.JobId, CancellationToken.None);

        var resumedPages = new List<int>();
        var resumedFactory = new RecordingSessionFactory(page =>
        {
            resumedPages.Add(page);
            return [Item($"item-{page}")];
        });
        await CreateRunner(database.Repository, resumedFactory).RunAsync(job, CancellationToken.None);
        var itemCount = await database.Repository.CountItemsAsync(job.JobId, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(checkpoint!.LastCompletedPage, Is.EqualTo(2));
            Assert.That(resumedPages, Is.EqualTo([3]));
            Assert.That(itemCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ConcurrentClaims_AllowOnlyOneActiveExecution()
    {
        await using var database = await TestDatabase.CreateAsync();

        var results = await Task.WhenAll(
            database.Repository.TryClaimAsync("contended-job", CancellationToken.None),
            database.Repository.TryClaimAsync("contended-job", CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(results.Count(result => result == JobClaimResult.Claimed), Is.EqualTo(1));
            Assert.That(results.Count(result => result == JobClaimResult.AlreadyRunning), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TransientPageFailure_RetriesOnlyThatPageAndThenContinues()
    {
        await using var database = await TestDatabase.CreateAsync();
        var firstPageAttempts = 0;
        var factory = new RecordingSessionFactory(page =>
        {
            if (page == 1 && ++firstPageAttempts < 3)
            {
                throw new BrowserOperationException("HTTP 503", 503);
            }

            return [Item($"item-{page}")];
        });

        var result = await CreateRunner(database.Repository, factory, maxAttempts: 3)
            .RunAsync(Job("retry-page-job", maxPages: 2), CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.LastCompletedPage, Is.EqualTo(2));
            Assert.That(firstPageAttempts, Is.EqualTo(3));
            Assert.That(factory.OpenCount, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task NaturalCatalogEnd_StopsBeforeConfiguredMaximum()
    {
        await using var database = await TestDatabase.CreateAsync();
        var factory = new RecordingSessionFactory(
            page => [Item($"item-{page}", page)],
            lastPage: 4);

        var result = await CreateRunner(database.Repository, factory)
            .RunAsync(Job("natural-end-job", maxPages: 10), CancellationToken.None);
        var checkpoint = await ((ICheckpointRepository)database.Repository)
            .FindAsync("natural-end-job", CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result.LastCompletedPage, Is.EqualTo(4));
            Assert.That(checkpoint!.LastCompletedPage, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task CommitPage_PersistsItemPageNumber()
    {
        await using var database = await TestDatabase.CreateAsync();
        const string jobId = "page-number-job";
        await database.Repository.TryClaimAsync(jobId, CancellationToken.None);
        await database.Repository.CommitPageAsync(
            jobId,
            [Item("item-page-3", pageNumber: 3)],
            new JobCheckpoint(jobId, 3, DateTimeOffset.UtcNow),
            CancellationToken.None);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={database.Path};Pooling=False");
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT page_number FROM catalog_items WHERE job_id = $jobId;";
        command.Parameters.AddWithValue("$jobId", jobId);

        Assert.That(await command.ExecuteScalarAsync(), Is.EqualTo(3L));
    }

    private static JobRunner CreateRunner(SqliteAutomationRepository repository, RecordingSessionFactory factory, int maxAttempts = 1) =>
        new(
            repository,
            repository,
            repository,
            factory,
            new NoOpFailureArtifactWriter(),
            new RetryExecutor(
                new RetrySettings(maxAttempts, TimeSpan.Zero, TimeSpan.Zero),
                new TransientFailureClassifier(),
                new TestClock(),
                new FixedRetryRandom(),
                new NoOpRetryObserver()),
            new JobExecutionSettings(TimeSpan.FromMinutes(1)));

    private static AutomationJob Job(string jobId, int maxPages) =>
        new(jobId, "test", new Uri("https://example.test/catalog"), maxPages);

    private static CatalogItem Item(string externalId, int pageNumber = 1) =>
        new(externalId, externalId, 10m, pageNumber, new Uri("https://example.test/catalog"));

    private sealed class RecordingSessionFactory(
        Func<int, IReadOnlyCollection<CatalogItem>> extract,
        int lastPage = int.MaxValue) : IBrowserCatalogSessionFactory
    {
        public int OpenCount { get; private set; }

        public Task<IBrowserCatalogSession> OpenAsync(AutomationJob job, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult<IBrowserCatalogSession>(new RecordingSession(extract, lastPage));
        }
    }

    private sealed class RecordingSession(
        Func<int, IReadOnlyCollection<CatalogItem>> extract,
        int lastPage) : IBrowserCatalogSession
    {
        public Task<CatalogPageExtraction?> ExtractPageAsync(int pageNumber, CancellationToken cancellationToken) =>
            Task.FromResult(
                pageNumber > lastPage
                    ? null
                    : new CatalogPageExtraction(extract(pageNumber), pageNumber < lastPage));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpFailureArtifactWriter : IFailureArtifactWriter
    {
        public Task<string?> CaptureAsync(string jobId, Exception error, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed class TestClock : IJobClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

        public Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FixedRetryRandom : IRetryRandom
    {
        public double NextDouble() => 0.5;
    }

    private sealed class TestDatabase : IAsyncDisposable
    {
        private TestDatabase(string path, SqliteAutomationRepository repository)
        {
            Path = path;
            Repository = repository;
        }

        public string Path { get; }

        public SqliteAutomationRepository Repository { get; }

        public static async Task<TestDatabase> CreateAsync()
        {
            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"resilient-automation-{Guid.NewGuid():N}.db");
            var connections = new SqliteConnectionFactory($"Data Source={path};Pooling=False");
            await new SqliteMigrator(connections).MigrateAsync(CancellationToken.None);
            return new TestDatabase(path, new SqliteAutomationRepository(connections, TimeSpan.FromMinutes(5)));
        }

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { string.Empty, "-shm", "-wal" })
            {
                var path = Path + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }

            return ValueTask.CompletedTask;
        }
    }
}
