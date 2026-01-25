using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.cli;

internal interface ICliPlanPrinter
{
    void PrintPlan(WorkflowPlan plan, int indent, bool verbose, string? parentVersion, string? allowVersion, TextWriter writer);
}
