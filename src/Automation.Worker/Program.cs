using Automation.Application;
using Automation.Application.Abstractions;
using Automation.Application.Retry;
using Automation.Playwright;
using Automation.Storage;
using Automation.Worker.Adapters;
using Automation.Worker.Configuration;
using Automation.Worker.Jobs;
using Automation.Worker.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using var cancellationSource = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellationSource.Cancel();
};

try
{
    var commandLine = WorkerCommandLine.Parse(args);
    var builder = Host.CreateApplicationBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddJsonConsole(options => options.IncludeScopes = true);

    builder.Services.AddOptions<AutomationWorkerOptions>()
        .Bind(builder.Configuration.GetSection(AutomationWorkerOptions.SectionName))
        .ValidateOnStart();
    builder.Services.AddSingleton<IValidateOptions<AutomationWorkerOptions>, AutomationWorkerOptionsValidator>();

    builder.Services.AddSingleton(commandLine);
    builder.Services.AddSingleton<JobLineParser>();
    builder.Services.AddSingleton<IJobSource, JsonLinesJobSource>();
    builder.Services.AddSingleton(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<AutomationWorkerOptions>>().Value;
        return new SqliteConnectionFactory(options.Storage.ConnectionString);
    });
    builder.Services.AddSingleton<SqliteMigrator>();
    builder.Services.AddSingleton(serviceProvider =>
    {
        var options = serviceProvider.GetRequiredService<IOptions<AutomationWorkerOptions>>().Value;
        return new SqliteAutomationRepository(
            serviceProvider.GetRequiredService<SqliteConnectionFactory>(),
            TimeSpan.FromSeconds(options.Storage.StaleRunningJobSeconds));
    });
    builder.Services.AddSingleton<IJobRepository>(serviceProvider => serviceProvider.GetRequiredService<SqliteAutomationRepository>());
    builder.Services.AddSingleton<ICheckpointRepository>(serviceProvider => serviceProvider.GetRequiredService<SqliteAutomationRepository>());
    builder.Services.AddSingleton<IJobPageCommitter>(serviceProvider => serviceProvider.GetRequiredService<SqliteAutomationRepository>());
    builder.Services.AddSingleton<IJobClock, SystemJobClock>();
    builder.Services.AddSingleton<IRetryRandom, SystemRetryRandom>();
    builder.Services.AddSingleton<IRetryObserver, LoggingRetryObserver>();
    builder.Services.AddSingleton<TransientFailureClassifier>();
    builder.Services.AddSingleton(serviceProvider =>
    {
        var retry = serviceProvider.GetRequiredService<IOptions<AutomationWorkerOptions>>().Value.Retry;
        return new RetryExecutor(
            new RetrySettings(
                retry.MaxAttempts,
                TimeSpan.FromMilliseconds(retry.BaseDelayMilliseconds),
                TimeSpan.FromMilliseconds(retry.MaxDelayMilliseconds)),
            serviceProvider.GetRequiredService<TransientFailureClassifier>(),
            serviceProvider.GetRequiredService<IJobClock>(),
            serviceProvider.GetRequiredService<IRetryRandom>(),
            serviceProvider.GetRequiredService<IRetryObserver>());
    });
    builder.Services.AddSingleton(serviceProvider =>
    {
        var timeout = serviceProvider.GetRequiredService<IOptions<AutomationWorkerOptions>>().Value.Timeouts;
        return new JobExecutionSettings(TimeSpan.FromSeconds(timeout.WholeJobTimeoutSeconds));
    });
    builder.Services.AddSingleton<IBrowserCatalogSessionFactory>(serviceProvider =>
    {
        var browser = serviceProvider.GetRequiredService<IOptions<AutomationWorkerOptions>>().Value.Browser;
        return new PlaywrightCatalogSessionFactory(new PlaywrightBrowserOptions
        {
            Headless = browser.Headless,
            NavigationTimeoutMilliseconds = browser.NavigationTimeoutSeconds * 1000,
            OperationTimeoutMilliseconds = browser.OperationTimeoutSeconds * 1000,
            DemoUsername = browser.DemoUsername,
            DemoPassword = browser.DemoPassword,
        });
    });
    builder.Services.AddSingleton<IFailureArtifactWriter, NoOpFailureArtifactWriter>();
    builder.Services.AddSingleton<JobRunner>();
    builder.Services.AddSingleton<AutomationWorkerService>();
    builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<AutomationWorkerService>());

    using var host = builder.Build();
    await host.Services.GetRequiredService<SqliteMigrator>().MigrateAsync(cancellationSource.Token);
    await host.StartAsync(cancellationSource.Token);

    var worker = host.Services.GetRequiredService<AutomationWorkerService>();
    var summary = await worker.Completion;
    await host.StopAsync(CancellationToken.None);

    Console.Out.WriteLine($"{{\"completedJobs\":{summary.CompletedJobs},\"rejectedJobs\":{summary.RejectedJobs},\"failedJobs\":{summary.FailedJobs},\"cancelledJobs\":{summary.CancelledJobs},\"exitCode\":{(int)summary.ExitCode}}}");
    return (int)summary.ExitCode;
}
catch (OptionsValidationException error)
{
    Console.Error.WriteLine($"Configuration error: {string.Join(" ", error.Failures)}");
    return (int)WorkerExitCode.HostConfigurationError;
}
catch (ArgumentException error)
{
    Console.Error.WriteLine($"Command-line error: {error.Message}");
    return (int)WorkerExitCode.RejectedInput;
}
catch (OperationCanceledException) when (cancellationSource.IsCancellationRequested)
{
    return (int)WorkerExitCode.Cancelled;
}
