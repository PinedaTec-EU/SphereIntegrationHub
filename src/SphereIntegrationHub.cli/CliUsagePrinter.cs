namespace SphereIntegrationHub.cli;

internal sealed class CliUsagePrinter : ICliUsagePrinter
{
    public void PrintUsage(TextWriter writer)
    {
        writer.WriteLine("Usage:");
        writer.WriteLine("  SphereIntegrationHub.cli --workflow <path> --env <environment> [--catalog <path>] [--envfile <path>] [--varsfile <path>] [--refresh-cache] [--dry-run] [--verbose] [--debug] [--mocked]");
        writer.WriteLine();
        writer.WriteLine("Options:");
        writer.WriteLine("  -w, --workflow  Workflow file to execute.");
        writer.WriteLine("  -e, --env       Environment key to resolve base url.");
        writer.WriteLine("  -c, --catalog   Optional catalog path (defaults to embedded catalog).");
        writer.WriteLine("      --envfile   Optional env file override for the root workflow.");
        writer.WriteLine("      --varsfile  Optional vars file override (must be .wfvars).");
        writer.WriteLine("      --refresh-cache  Redownload swagger definitions even if cached.");
        writer.WriteLine("      --dry-run   Print the execution plan without calling endpoints.");
        writer.WriteLine("      --verbose   Print detailed plan information.");
        writer.WriteLine("      --debug     Print stage debug sections before invocation.");
        writer.WriteLine("      --mocked    Use mock payloads/outputs when defined.");
        writer.WriteLine("  -h, --help      Show this help.");
        writer.WriteLine();
        writer.WriteLine("PinedaTec.eu © 2026");
    }
}
