namespace SphereIntegrationHub.cli;

internal sealed class CliApp
{
    private readonly ICliArgumentParser _argumentParser;
    private readonly ICliUsagePrinter _usagePrinter;
    private readonly ICliPathResolver _pathResolver;
    private readonly ICliPlanPrinter _planPrinter;
    private readonly ICliWorkflowEnvironmentValidator _environmentValidator;
    private readonly ICliOutputProvider _output;
    private readonly ICliServiceFactory _serviceFactory;
    private readonly ICliPipeline _pipeline;

    public CliApp(
        ICliArgumentParser? argumentParser = null,
        ICliUsagePrinter? usagePrinter = null,
        ICliPathResolver? pathResolver = null,
        ICliPlanPrinter? planPrinter = null,
        ICliWorkflowEnvironmentValidator? environmentValidator = null,
        ICliOutputProvider? outputProvider = null,
        ICliServiceFactory? serviceFactory = null,
        ICliPipeline? pipeline = null,
        IWorkflowConfigLoader? configLoader = null,
        IOpenTelemetryBootstrapper? telemetryBootstrapper = null)
    {
        _argumentParser = argumentParser ?? new CliArgumentParser();
        _usagePrinter = usagePrinter ?? new CliUsagePrinter();
        _pathResolver = pathResolver ?? new CliPathResolver();
        _planPrinter = planPrinter ?? new CliPlanPrinter();
        _environmentValidator = environmentValidator ?? new CliWorkflowEnvironmentValidator();
        _output = outputProvider ?? new ConsoleOutputProvider();
        _serviceFactory = serviceFactory ?? new CliServiceFactory(_output);
        var loader = configLoader ?? new WorkflowConfigLoader();
        var otel = telemetryBootstrapper ?? new OpenTelemetryBootstrapper();
        _pipeline = pipeline ?? new CliPipeline(_pathResolver, _planPrinter, _environmentValidator, _serviceFactory, loader, otel);
    }

    public async Task<int> RunAsync(string[] args)
    {
        // Resolve assembly version
        var assemblyVersion = typeof(global::Program).Assembly.GetName().Version;
        var message = $" API Orchestrator Sphere CLI v.{assemblyVersion}";

        _output.Out.WriteLine(message);
        _output.Out.WriteLine(new string('-', message.Length + 1));

        var parseResult = _argumentParser.ParseArgs(args);
        if (parseResult.Error is not null)
        {
            _output.Error.WriteLine(parseResult.Error);
            _output.Error.WriteLine();
            _usagePrinter.PrintUsage(_output.Error);
            return 1;
        }

        if (parseResult.ShowHelp)
        {
            _usagePrinter.PrintUsage(_output.Error);
            return 0;
        }

        if (string.IsNullOrWhiteSpace(parseResult.WorkflowPath) || string.IsNullOrWhiteSpace(parseResult.Environment))
        {
            _output.Error.WriteLine("Missing required parameters.");
            _output.Error.WriteLine();
            _usagePrinter.PrintUsage(_output.Error);
            return 1;
        }

        var runResult = await _pipeline.RunAsync(parseResult, CancellationToken.None);
        foreach (var resultMessage in runResult.Messages)
        {
            var writer = resultMessage.Kind == CliRunMessageKind.Error ? _output.Error : _output.Out;
            writer.WriteLine(resultMessage.Text);
        }

        return runResult.ExitCode;
    }
}
