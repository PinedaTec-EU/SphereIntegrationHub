namespace SphereIntegrationHub.cli;

internal sealed class CliArgumentParser : ICliArgumentParser
{
    public InlineArguments ParseArgs(string[] args)
    {
        // Detect 'report' subcommand
        if (args.Length > 0 && args[0] == "report")
        {
            return ParseReportArgs(args);
        }

        if (args.Length > 0 && args[0] == "snapshot")
        {
            return ParseSnapshotArgs(args);
        }

        string? workflowPath = null;
        string? environment = null;
        string? catalogPath = null;
        string? envFileOverride = null;
        string? varsFilePath = null;
        string? reportFormat = null;
        string? captureHttp = null;
        var refreshCache = false;
        var dryRun = false;
        var verbose = false;
        var debug = false;
        var mocked = false;
        bool? redactSensitiveData = null;
        bool? summaryConsole = null;
        bool? assertionFailuresBlock = null;
        var showHelp = false;
        var showVersion = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--workflow":
                case "-w":
                    if (!TryReadValue(args, ref i, out workflowPath))
                    {
                        return new InlineArguments(Error: "Missing value for --workflow.");
                    }
                    break;
                case "--env":
                case "-e":
                    if (!TryReadValue(args, ref i, out environment))
                    {
                        return new InlineArguments(Error: "Missing value for --env.");
                    }
                    break;
                case "--catalog":
                case "-c":
                    if (!TryReadValue(args, ref i, out catalogPath))
                    {
                        return new InlineArguments(Error: "Missing value for --catalog.");
                    }
                    break;
                case "--envfile":
                    if (!TryReadValue(args, ref i, out envFileOverride))
                    {
                        return new InlineArguments(Error: "Missing value for --envfile.");
                    }
                    break;
                case "--varsfile":
                    if (!TryReadValue(args, ref i, out varsFilePath))
                    {
                        return new InlineArguments(Error: "Missing value for --varsfile.");
                    }
                    break;
                case "--report-format":
                    if (!TryReadValue(args, ref i, out reportFormat))
                    {
                        return new InlineArguments(Error: "Missing value for --report-format.");
                    }
                    break;
                case "--capture-http":
                    if (!TryReadValue(args, ref i, out captureHttp))
                    {
                        return new InlineArguments(Error: "Missing value for --capture-http.");
                    }
                    break;
                case "--refresh-cache":
                    refreshCache = true;
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--verbose":
                    verbose = true;
                    break;
                case "--debug":
                    debug = true;
                    break;
                case "--mocked":
                    mocked = true;
                    break;
                case "--no-redact":
                    redactSensitiveData = false;
                    break;
                case "--no-summary":
                    summaryConsole = false;
                    break;
                case "--assertion-failures-block":
                    if (!TryReadValue(args, ref i, out var assertionFailuresBlockValue))
                    {
                        return new InlineArguments(Error: "Missing value for --assertion-failures-block.");
                    }

                    if (!TryParseBool(assertionFailuresBlockValue, out assertionFailuresBlock))
                    {
                        return new InlineArguments(Error: "Invalid value for --assertion-failures-block. Use true or false.");
                    }
                    break;
                case "--help":
                case "-h":
                    showHelp = true;
                    break;
                case "--version":
                    showVersion = true;
                    break;
                default:
                    return new InlineArguments(Error: $"Unknown argument '{arg}'.");
            }
        }

        if (dryRun)
        {
            mocked = false;
        }

        return new InlineArguments(
            workflowPath,
            environment,
            catalogPath,
            envFileOverride,
            varsFilePath,
            reportFormat,
            captureHttp,
            refreshCache,
            dryRun,
            verbose,
            debug,
            mocked,
            redactSensitiveData,
            summaryConsole,
            assertionFailuresBlock,
            null,
            showHelp,
            showVersion);
    }

    private static InlineArguments ParseReportArgs(string[] args)
    {
        string? execPath = null;
        string? outputPath = null;
        string? snapshotPath = null;
        string? catalogPath = null;
        var openAfterGenerate = true;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    return new InlineArguments(IsReportCommand: true, ShowHelp: true);
                case "--execution":
                case "-x":
                    if (!TryReadValue(args, ref i, out execPath))
                        return new InlineArguments(Error: "Missing value for --execution.");
                    break;
                case "--output":
                case "-o":
                    if (!TryReadValue(args, ref i, out outputPath))
                        return new InlineArguments(Error: "Missing value for --output.");
                    break;
                case "--snapshot":
                case "-s":
                    if (!TryReadValue(args, ref i, out snapshotPath))
                        return new InlineArguments(Error: "Missing value for --snapshot.");
                    break;
                case "--catalog":
                case "-c":
                    if (!TryReadValue(args, ref i, out catalogPath))
                        return new InlineArguments(Error: "Missing value for --catalog.");
                    break;
                case "--no-open":
                    openAfterGenerate = false;
                    break;
                default:
                    if (!args[i].StartsWith('-') && execPath is null)
                        execPath = args[i];
                    else
                        return new InlineArguments(Error: $"Unknown report argument '{args[i]}'.");
                    break;
            }
        }

        if (execPath is null)
            return new InlineArguments(Error: "Missing execution report path. Usage: sih report <path-to-json-or-dir> [--output <dir>] [--no-open]");

        return new InlineArguments(
            IsReportCommand: true,
            ExecutionReportPath: execPath,
            CatalogPath: catalogPath,
            ReportOutputPath: outputPath,
            OpenAfterGenerate: openAfterGenerate,
            SnapshotPath: snapshotPath);
    }

    private static InlineArguments ParseSnapshotArgs(string[] args)
    {
        if (args.Length == 1)
        {
            return new InlineArguments(Error: "Missing snapshot action. Usage: sih snapshot <create|compare> ...");
        }

        var action = args[1];
        if (action is "--help" or "-h")
        {
            return new InlineArguments(IsSnapshotCommand: true, ShowHelp: true);
        }

        if (!string.Equals(action, "create", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(action, "compare", StringComparison.OrdinalIgnoreCase))
        {
            return new InlineArguments(Error: $"Unknown snapshot action '{action}'. Use create or compare.");
        }

        string? executionPath = null;
        string? snapshotPath = null;
        string? snapshotName = null;
        string? outputPath = null;

        for (var i = 2; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--help":
                case "-h":
                    return new InlineArguments(IsSnapshotCommand: true, ShowHelp: true);
                case "--execution":
                case "-x":
                    if (!TryReadValue(args, ref i, out executionPath))
                    {
                        return new InlineArguments(Error: "Missing value for --execution.");
                    }
                    break;
                case "--snapshot":
                case "-s":
                    if (!TryReadValue(args, ref i, out snapshotPath))
                    {
                        return new InlineArguments(Error: "Missing value for --snapshot.");
                    }
                    break;
                case "--name":
                case "-n":
                    if (!TryReadValue(args, ref i, out snapshotName))
                    {
                        return new InlineArguments(Error: "Missing value for --name.");
                    }
                    break;
                case "--output":
                case "-o":
                    if (!TryReadValue(args, ref i, out outputPath))
                    {
                        return new InlineArguments(Error: "Missing value for --output.");
                    }
                    break;
                default:
                    if (!args[i].StartsWith('-') && executionPath is null)
                    {
                        executionPath = args[i];
                    }
                    else
                    {
                        return new InlineArguments(Error: $"Unknown snapshot argument '{args[i]}'.");
                    }
                    break;
            }
        }

        if (executionPath is null)
        {
            return new InlineArguments(Error: "Missing execution report path. Usage: sih snapshot create <report-json> [--output <path>] [--name <name>]");
        }

        if (string.Equals(action, "compare", StringComparison.OrdinalIgnoreCase) && snapshotPath is null)
        {
            return new InlineArguments(Error: "Missing snapshot path. Usage: sih snapshot compare <report-json> --snapshot <snapshot-json>");
        }

        return new InlineArguments(
            IsSnapshotCommand: true,
            SnapshotAction: action.ToLowerInvariant(),
            ExecutionReportPath: executionPath,
            ReportOutputPath: outputPath,
            SnapshotPath: snapshotPath,
            SnapshotName: snapshotName);
    }

    private static bool TryReadValue(string[] args, ref int index, out string? value)
    {
        if (index + 1 >= args.Length)
        {
            value = null;
            return false;
        }

        index++;
        value = args[index];
        return true;
    }

    private static bool TryParseBool(string? value, out bool? result)
    {
        if (bool.TryParse(value, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }
}
