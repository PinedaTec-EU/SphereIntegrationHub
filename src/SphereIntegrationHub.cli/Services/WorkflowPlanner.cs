using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

public sealed class WorkflowPlanner
{
    private readonly WorkflowLoader _loader;

    public WorkflowPlanner(WorkflowLoader loader)
    {
        _loader = loader ?? throw new ArgumentNullException(nameof(loader));
    }

    public WorkflowPlan BuildPlan(WorkflowDocument document, bool verbose)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityWorkflowPlan);
        activity?.SetTag(TelemetryConstants.TagWorkflowPath, document.FilePath);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return BuildPlan(document, verbose, visited);
    }

    private WorkflowPlan BuildPlan(WorkflowDocument document, bool verbose, HashSet<string> visited)
    {
        visited.Add(document.FilePath);
        var definition = document.Definition;
        var stages = new List<WorkflowStagePlan>();
        var workflowRefs = BuildWorkflowReferenceLookup(definition.References?.Workflows, document.FilePath);

        if (definition.Stages is not null)
        {
            foreach (var stage in definition.Stages)
            {
                WorkflowPlan? nestedPlan = null;
                if (stage.Kind == WorkflowStageKind.Workflow && !string.IsNullOrWhiteSpace(stage.WorkflowRef))
                {
                    if (workflowRefs.TryGetValue(stage.WorkflowRef, out var referencePath))
                    {
                        if (verbose && !visited.Contains(referencePath))
                        {
                            var referencedDocument = _loader.Load(referencePath, document.EnvironmentVariables);
                            nestedPlan = BuildPlan(referencedDocument, verbose, visited);
                        }
                        else if (verbose && visited.Contains(referencePath))
                        {
                            nestedPlan = new WorkflowPlan(
                                "Already included",
                                string.Empty,
                                string.Empty,
                                referencePath,
                                Array.Empty<WorkflowInputDefinition>(),
                                Array.Empty<WorkflowStagePlan>(),
                                new Dictionary<string, string>(),
                                false,
                                null,
                                null);
                        }
                    }
                }

                var stageOutput = stage.Output is null
                    ? Array.Empty<KeyValuePair<string, string>>()
                    : stage.Output.ToArray();
                stages.Add(new WorkflowStagePlan(
                    stage.Name,
                    stage.Kind,
                    stage.ApiRef,
                    stage.Endpoint,
                    stage.HttpVerb,
                    stage.ExpectedStatus,
                    stage.Headers,
                    stage.Query,
                    stage.Body,
                    stage.WorkflowRef,
                    stage.Inputs,
                    stageOutput,
                    stage.AllowVersion,
                    stage.Context,
                    stage.Set,
                    nestedPlan));
            }
        }

        var workflowOutput = definition.EndStage?.Output ?? new Dictionary<string, string>();
        return new WorkflowPlan(
            definition.Name,
            definition.Id,
            definition.Version,
            document.FilePath,
            definition.Input ?? new List<WorkflowInputDefinition>(),
            stages,
            workflowOutput,
            definition.Output,
            definition.InitStage?.Context,
            definition.EndStage?.Context);
    }

    private static Dictionary<string, string> BuildWorkflowReferenceLookup(
        IReadOnlyList<WorkflowReferenceItem>? references,
        string workflowPath)
    {
        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (references is null)
        {
            return lookup;
        }

        var baseDirectory = Path.GetDirectoryName(workflowPath) ?? string.Empty;
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.Name) || string.IsNullOrWhiteSpace(reference.Path))
            {
                continue;
            }

            var resolvedPath = Path.GetFullPath(Path.Combine(baseDirectory, reference.Path));
            lookup[reference.Name] = resolvedPath;
        }

        return lookup;
    }
}

public sealed record WorkflowPlan(
    string Name,
    string Id,
    string Version,
    string FilePath,
    IReadOnlyList<WorkflowInputDefinition> Inputs,
    IReadOnlyList<WorkflowStagePlan> Stages,
    IReadOnlyDictionary<string, string> Output,
    bool OutputEnabled,
    IReadOnlyDictionary<string, string>? InitContext,
    IReadOnlyDictionary<string, string>? EndContext);

public sealed record WorkflowStagePlan(
    string Name,
    WorkflowStageKind Kind,
    string? ApiRef,
    string? Endpoint,
    string? HttpVerb,
    int? ExpectedStatus,
    IReadOnlyDictionary<string, string>? Headers,
    IReadOnlyDictionary<string, string>? Query,
    string? Body,
    string? WorkflowRef,
    IReadOnlyDictionary<string, string>? Inputs,
    IReadOnlyCollection<KeyValuePair<string, string>> Output,
    string? AllowVersion,
    IReadOnlyDictionary<string, string>? Context,
    IReadOnlyDictionary<string, string>? Set,
    WorkflowPlan? NestedPlan);
