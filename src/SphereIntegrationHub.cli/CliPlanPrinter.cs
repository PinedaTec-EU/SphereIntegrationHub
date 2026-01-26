using System;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services;

namespace SphereIntegrationHub.cli;

internal sealed class CliPlanPrinter : ICliPlanPrinter
{
    public void PrintPlan(WorkflowPlan plan, int indent, bool verbose, string? parentVersion, string? allowVersion, TextWriter writer)
    {
        var prefix = new string(' ', indent);
        writer.WriteLine($"{prefix}Workflow: {plan.Name} ({plan.Id})");
        if (!string.IsNullOrWhiteSpace(parentVersion) &&
            !string.Equals(plan.Version, parentVersion, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(allowVersion))
        {
            writer.WriteLine($"{prefix}Version: {plan.Version} (allowed by parent: {parentVersion})");
        }
        else
        {
            writer.WriteLine($"{prefix}Version: {plan.Version}");
        }

        writer.WriteLine($"{prefix}File: {plan.FilePath}");

        if (plan.Inputs.Count > 0)
        {
            writer.WriteLine($"{prefix}Inputs:");
            foreach (var input in plan.Inputs)
            {
                var requirement = input.Required ? "required" : "optional";
                writer.WriteLine($"{prefix}  - {input.Name} ({input.Type}, {requirement})");
            }
        }

        if (plan.Stages.Count > 0)
        {
            writer.WriteLine($"{prefix}Stages:");
            foreach (var stage in plan.Stages)
            {
                writer.WriteLine($"{prefix}  - {stage.Name} [{stage.Kind}]");
                if (IsHttpStage(stage.Kind))
                {
                    writer.WriteLine($"{prefix}    Api: {stage.ApiRef}");
                    writer.WriteLine($"{prefix}    Endpoint: {stage.Endpoint}");
                    writer.WriteLine($"{prefix}    Verb: {stage.HttpVerb}");
                    if (stage.ExpectedStatus is not null)
                    {
                        writer.WriteLine($"{prefix}    Expected status: {stage.ExpectedStatus}");
                    }

                    if (verbose)
                    {
                        PrintKeyValues($"{prefix}    Headers:", stage.Headers, writer);
                        PrintKeyValues($"{prefix}    Query:", stage.Query, writer);
                        if (!string.IsNullOrWhiteSpace(stage.Body))
                        {
                            writer.WriteLine($"{prefix}    Body:");
                            foreach (var line in stage.Body.Split(Environment.NewLine))
                            {
                                writer.WriteLine($"{prefix}      {line}");
                            }
                        }
                    }
                }
                else
                {
                    writer.WriteLine($"{prefix}    Workflow: {stage.WorkflowRef}");
                    if (stage.Inputs is not null && stage.Inputs.Count > 0)
                    {
                        writer.WriteLine($"{prefix}    Inputs:");
                        foreach (var input in stage.Inputs)
                        {
                            writer.WriteLine($"{prefix}      {input.Key}: {input.Value}");
                        }
                    }
                }

                if (stage.Output.Count > 0)
                {
                    writer.WriteLine($"{prefix}    Output:");
                    foreach (var output in stage.Output)
                    {
                        writer.WriteLine($"{prefix}      {output.Key}: {output.Value}");
                    }
                }
                else
                {
                    writer.WriteLine($"{prefix}    Output: (not defined)");
                }

                if (verbose)
                {
                    PrintKeyValues($"{prefix}    Context:", stage.Context, writer);
                    PrintKeyValues($"{prefix}    Set:", stage.Set, writer);
                }

                if (verbose && stage.NestedPlan is not null)
                {
                    writer.WriteLine($"{prefix}    Workflow plan:");
                    PrintPlan(stage.NestedPlan, indent + 6, verbose, plan.Version, stage.AllowVersion, writer);
                }
            }
        }

        if (plan.OutputEnabled)
        {
            if (plan.Output.Count > 0)
            {
                writer.WriteLine($"{prefix}Workflow output:");
                foreach (var output in plan.Output)
                {
                    writer.WriteLine($"{prefix}  {output.Key}: {output.Value}");
                }
            }
            else
            {
                writer.WriteLine($"{prefix}Workflow output: (not defined)");
            }
        }
        else
        {
            writer.WriteLine($"{prefix}Workflow output: disabled");
        }

        if (verbose)
        {
            PrintKeyValues($"{prefix}Init-stage context:", plan.InitContext, writer);
            PrintKeyValues($"{prefix}End-stage context:", plan.EndContext, writer);
        }
    }

    private static bool IsHttpStage(string kind)
    {
        return string.Equals(kind, WorkflowStageKinds.Endpoint, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(kind, WorkflowStageKinds.Http, StringComparison.OrdinalIgnoreCase);
    }

    private static void PrintKeyValues(string title, IReadOnlyDictionary<string, string>? values, TextWriter writer)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        writer.WriteLine(title);
        foreach (var pair in values)
        {
            writer.WriteLine($"  {pair.Key}: {pair.Value}");
        }
    }
}
