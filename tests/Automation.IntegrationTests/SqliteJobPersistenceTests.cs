using Automation.Application;
using Automation.Application.Abstractions;
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

    private static JobRunner CreateRunner(SqliteAutomationRepository repository, RecordingSessionFactory factory) =>
        new(repository, repository, repository, factory, new NoOpFailureArtifactWriter());

    private static AutomationJob Job(string jobId, int maxPages) =>
        new(jobId, "test", new Uri("https://example.test/catalog"), maxPages);

    private static CatalogItem Item(string externalId) =>
        new(externalId, externalId, 10m, new Uri("https://example.test/catalog"));

    private sealed class RecordingSessionFactory(Func<int, IReadOnlyCollection<CatalogItem>> extract) : IBrowserCatalogSessionFactory
    {
        public int OpenCount { get; private set; }

        public Task<IBrowserCatalogSession> OpenAsync(AutomationJob job, CancellationToken cancellationToken)
        {
            OpenCount++;
            return Task.FromResult<IBrowserCatalogSession>(new RecordingSession(extract));
        }
    }

    private sealed class RecordingSession(Func<int, IReadOnlyCollection<CatalogItem>> extract) : IBrowserCatalogSession
    {
        public Task<IReadOnlyCollection<CatalogItem>> ExtractPageAsync(int pageNumber, CancellationToken cancellationToken) =>
            Task.FromResult(extract(pageNumber));

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class NoOpFailureArtifactWriter : IFailureArtifactWriter
    {
        public Task CaptureAsync(string jobId, Exception error, CancellationToken cancellationToken) => Task.CompletedTask;
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
