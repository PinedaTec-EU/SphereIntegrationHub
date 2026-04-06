namespace SphereIntegrationHub.cli;

internal sealed class CliUsagePrinter : ICliUsagePrinter
{
    public void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  sih --workflow <path> --env <environment> [--catalog <path>] [--envfile <path>] [--varsfile <path>] [--report-format <json|html|both|none>] [--capture-http <none|headers|bodies>] [--refresh-cache] [--dry-run] [--verbose] [--debug] [--mocked]");
        writer.WriteLine("  sih report <path-to-json-or-dir> [--output <dir>] [--no-open]");
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
        writer.WriteLine("      --refresh-cache  Redownload swagger definitions even if cached.");
        writer.WriteLine("      --dry-run   Print the execution plan without calling endpoints.");
        writer.WriteLine("      --verbose   Print detailed plan information.");
        writer.WriteLine("      --debug     Print stage debug sections before invocation.");
        writer.WriteLine("      --mocked    Use mock payloads/outputs when defined.");
        writer.WriteLine("      --no-redact Disable redaction of sensitive headers and payload fields in reports.");
        writer.WriteLine("      --no-summary Disable the post-execution summary block.");
        writer.WriteLine("      Preflight: validates environment, probes configured API healthCheck endpoints, refreshes swagger cache, and validates referenced endpoints before execution.");
        writer.WriteLine();
        writer.WriteLine("Report options:");
        writer.WriteLine("  sih report <path>  Generate an interactive HTML trace report from one execution JSON artifact or from a directory of report JSON files.");
        writer.WriteLine("  -x, --execution    Path to a .workflow.report.json file or a directory containing them.");
        writer.WriteLine("  -o, --output       Output directory for the HTML file (defaults to same dir as JSON).");
        writer.WriteLine("      --no-open      Do not open the report in the browser after generation.");
        writer.WriteLine("  Examples:");
        writer.WriteLine("    sih --workflow ./my.workflow --env local --report-format both");
        writer.WriteLine("    sih report ./output/my.workflow.report.json --no-open");
        writer.WriteLine("    sih report ./.sphere/workflows/bootstrap/output --no-open");
        writer.WriteLine();
        writer.WriteLine("Global:");
        writer.WriteLine("      --version   Print only the CLI version.");
        writer.WriteLine("  -h, --help      Show this help.");
        writer.WriteLine();
        writer.WriteLine("PinedaTec.eu © 2026");
    }
}
