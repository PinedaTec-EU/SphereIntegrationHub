namespace SphereIntegrationHub.cli;

internal sealed class CliArgumentParser : ICliArgumentParser
{
    public InlineArguments ParseArgs(string[] args)
    {
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
            null,
            showHelp,
            showVersion);
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
}
