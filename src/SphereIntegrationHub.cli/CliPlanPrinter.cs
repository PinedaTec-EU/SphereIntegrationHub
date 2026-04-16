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
        if (verbose)
        {
            PrintKeyValues($"{prefix}Resolved env:", plan.EnvironmentVariables, writer);
        }

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
                if (stage.Kind == WorkflowStageKind.Endpoint)
                {
                    writer.WriteLine($"{prefix}    Api: {stage.ApiRef}");
                    writer.WriteLine($"{prefix}    Endpoint: {stage.Endpoint}");
                    writer.WriteLine($"{prefix}    Verb: {stage.HttpVerb}");
                    if (stage.ExpectedStatus is not null)
                    {
                        writer.WriteLine($"{prefix}    Expected status: {stage.ExpectedStatus}");
                    }
                    else if (stage.ExpectedStatuses is { Length: > 0 })
                    {
                        writer.WriteLine($"{prefix}    Expected statuses: [{string.Join(", ", stage.ExpectedStatuses)}]");
                    }

                    if (!string.IsNullOrWhiteSpace(stage.BodyFile))
                    {
                        writer.WriteLine($"{prefix}    Body file: {stage.BodyFile}");
                    }

                    if (!string.IsNullOrWhiteSpace(stage.DataFile))
                    {
                        writer.WriteLine($"{prefix}    Data file: {stage.DataFile}");
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
                    if (!string.IsNullOrWhiteSpace(stage.ResolvedWorkflowPath))
                    {
                        writer.WriteLine($"{prefix}    Resolved path: {stage.ResolvedWorkflowPath}");
                    }
                    if (stage.Inputs is not null && stage.Inputs.Count > 0)
                    {
                        writer.WriteLine($"{prefix}    Inputs:");
                        foreach (var input in stage.Inputs)
                        {
                            writer.WriteLine($"{prefix}      {input.Key}: {WorkflowStageInputValueHelper.ToDisplayString(input.Value)}");
                        }
                    }
                }

                if (!string.IsNullOrWhiteSpace(stage.RunIf))
                {
                    writer.WriteLine($"{prefix}    Run if: {stage.RunIf}");
                }

                if (!string.IsNullOrWhiteSpace(stage.ForEach))
                {
                    var mode = stage.ForEachSequential == true ? "Sequential" : "Parallel";
                    writer.WriteLine($"{prefix}    For each: {stage.ForEach} ({mode})");
                    if (!string.IsNullOrWhiteSpace(stage.ItemName))
                    {
                        writer.WriteLine($"{prefix}    Item name: {stage.ItemName}");
                    }

                    if (!string.IsNullOrWhiteSpace(stage.IndexName))
                    {
                        writer.WriteLine($"{prefix}    Index name: {stage.IndexName}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(stage.Message))
                {
                    writer.WriteLine($"{prefix}    Message: {stage.Message}");
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

    private static void PrintKeyValues(string title, IReadOnlyDictionary<string, string>? values, TextWriter writer)
    {
        if (values is null || values.Count == 0)
        {
            return;
        }

        writer.WriteLine(title);
        var itemPrefix = new string(' ', title.TakeWhile(char.IsWhiteSpace).Count());
        foreach (var pair in values)
        {
            writer.WriteLine($"{itemPrefix}  {pair.Key}: {pair.Value}");
        }
    }
}
