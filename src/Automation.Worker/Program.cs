using Automation.Application;
using Automation.Application.Abstractions;
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
    builder.Services.AddSingleton<IJobRepository, InMemoryJobRepository>();
    builder.Services.AddSingleton<ICheckpointRepository, InMemoryCheckpointRepository>();
    builder.Services.AddSingleton<IBrowserCatalogSessionFactory, FakeBrowserCatalogSessionFactory>();
    builder.Services.AddSingleton<IFailureArtifactWriter, NoOpFailureArtifactWriter>();
    builder.Services.AddSingleton<JobRunner>();
    builder.Services.AddSingleton<AutomationWorkerService>();
    builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<AutomationWorkerService>());

    using var host = builder.Build();
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
