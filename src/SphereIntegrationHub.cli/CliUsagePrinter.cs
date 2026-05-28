namespace SphereIntegrationHub.cli;

internal sealed class CliUsagePrinter : ICliUsagePrinter
{
    public void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  sih --workflow <path> --env <environment> [--catalog <path>] [--envfile <path>] [--varsfile <path>] [--report-format <json|html|both|none>] [--capture-http <none|headers|bodies>] [--assertion-failures-block <true|false>] [--refresh-cache] [--dry-run] [--verbose] [--debug] [--mocked]");
        writer.WriteLine("  sih report <path-to-json-or-dir> [--catalog <path>] [--snapshot <snapshot-json-or-dir>] [--output <dir>] [--no-open]");
        writer.WriteLine("  sih snapshot create <path-to-report-json> [--output <snapshot-json>] [--name <snapshot-name>]");
        writer.WriteLine("  sih snapshot compare <path-to-report-json> --snapshot <snapshot-json>");
        writer.WriteLine("  sih --version");
        writer.WriteLine();
        writer.WriteLine("Run options:");
        writer.WriteLine("  -w, --workflow  Workflow file to execute.");
        writer.WriteLine("  -e, --env       Environment key to resolve base url.");
        writer.WriteLine("  -c, --catalog   Optional catalog path (defaults to embedded catalog).");
        writer.WriteLine("      --envfile   Optional env file override for the root workflow.");
        writer.WriteLine("      --varsfile  Optional vars file override (must be .wfvars).");
        writer.WriteLine("      --report-format  Execution report artifact format: json, html, both, or none. Default: json.");
        writer.WriteLine("      --capture-http   Capture level for HTTP details in reports: none, headers, or bodies.");
        writer.WriteLine("      --assertion-failures-block  Treat assertion failures as blocking. Overrides api.catalog. Default: true.");
        writer.WriteLine("      --refresh-cache  Redownload swagger definitions even if cached.");
        writer.WriteLine("      --dry-run   Print the execution plan without calling endpoints.");
        writer.WriteLine("      --verbose   Print detailed plan information.");
        writer.WriteLine("      --debug     Print stage debug sections before invocation.");
        writer.WriteLine("      --mocked    Use mock payloads/outputs when defined.");
        writer.WriteLine("      --no-redact Disable redaction of sensitive headers and payload fields in reports.");
        writer.WriteLine("      --no-summary Disable the post-execution summary block.");
        writer.WriteLine("      Preflight: validates environment, waits for configured API healthCheck readiness (with catalog retry policy), refreshes swagger cache, and validates referenced endpoints before execution.");
        writer.WriteLine();
        writer.WriteLine("Report options:");
        writer.WriteLine("  sih report <path>  Generate an interactive HTML trace report from one execution JSON artifact or from a directory of report JSON files. Loads snapshots from the same dir or sibling snapshots/ dir.");
        writer.WriteLine("  -x, --execution    Path to a .workflow.report.json file or a directory containing them.");
        writer.WriteLine("  -c, --catalog      Optional api.catalog path. Used to load the catalog version baselineSnapshot.");
        writer.WriteLine("  -s, --snapshot     Optional .workflow.snapshot.json file or directory containing snapshots for baseline comparison.");
        writer.WriteLine("  -o, --output       Output directory for the HTML file (defaults to same dir as JSON).");
        writer.WriteLine("      --no-open      Do not open the report in the browser after generation.");
        writer.WriteLine("  Examples:");
        writer.WriteLine("    sih --workflow ./my.workflow --env local --report-format both");
        writer.WriteLine("    sih report ./output/my.workflow.report.json --no-open");
        writer.WriteLine("    sih report ./output --snapshot ./snapshots --no-open");
        writer.WriteLine("    sih report ./.sphere/workflows/bootstrap/output --no-open");
        writer.WriteLine();
        writer.WriteLine("Snapshot options:");
        writer.WriteLine("  sih snapshot create <path>     Create a stable regression snapshot from a known-good execution report.");
        writer.WriteLine("  sih snapshot compare <path>    Compare an execution report against a stored snapshot and fail on meaningful differences.");
        writer.WriteLine("  -x, --execution                Path to a .workflow.report.json file.");
        writer.WriteLine("  -s, --snapshot                 Snapshot path to compare against.");
        writer.WriteLine("  -o, --output                   Snapshot output path for create.");
        writer.WriteLine("  -n, --name                     Optional snapshot name. Default: workflow version.");
        writer.WriteLine("  Examples:");
        writer.WriteLine("    sih snapshot create ./output/my.workflow.report.json --name happy-path");
        writer.WriteLine("    sih snapshot compare ./output/new.workflow.report.json --snapshot ./snapshots/my.happy-path.workflow.snapshot.json");
        writer.WriteLine();
        writer.WriteLine("Global:");
        writer.WriteLine("      --version   Print only the CLI version.");
        writer.WriteLine("  -h, --help      Show this help.");
        writer.WriteLine();
        writer.WriteLine("PinedaTec.eu © 2026");
    }
}
