using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;
using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Tests;

public sealed class CliPlanPrinterTests
{
    [Fact]
    public void PrintPlan_WritesWorkflowSummary()
    {
        var plan = new WorkflowPlan(
            "name",
            "id",
            "1.0",
            "/tmp/workflow.yaml",
            new List<WorkflowInputDefinition>
            {
                new() { Name = "inputA", Type = RandomValueType.Text, Required = true }
            },
            new List<WorkflowStagePlan>
            {
                new(
                    "stageA",
                    WorkflowStageKind.Endpoint,
                    "api",
                    "/endpoint",
                    "GET",
                    200,
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<KeyValuePair<string, string>>(),
                    null,
                    null,
                    null,
                    null)
            },
            new Dictionary<string, string>
            {
                ["out"] = "value"
            },
            true,
            null,
            null);

        using var writer = new StringWriter();
        ICliPlanPrinter printer = new CliPlanPrinter();

        printer.PrintPlan(plan, 0, verbose: false, parentVersion: null, allowVersion: null, writer);

        var output = writer.ToString();
        Assert.Contains("Workflow: name (id)", output, StringComparison.Ordinal);
        Assert.Contains("Version: 1.0", output, StringComparison.Ordinal);
        Assert.Contains("Stages:", output, StringComparison.Ordinal);
        Assert.Contains("Workflow output:", output, StringComparison.Ordinal);
    }
}
