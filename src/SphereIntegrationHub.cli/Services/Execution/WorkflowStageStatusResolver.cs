using System.Text.Json;

using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services.Execution;

internal static class WorkflowStageStatusResolver
{
    private const int DefaultEnsureExistsStatus = 409;

    public static bool TryResolveStatusAction(
        WorkflowStageDefinition stage,
        int statusCode,
        out WorkflowStageStatusAction statusAction)
    {
        statusAction = new WorkflowStageStatusAction();
        if (stage.OnStatus is not null && stage.OnStatus.TryGetValue(statusCode, out var configured))
        {
            statusAction = configured;
            return true;
        }

        if (TryResolveEnsureStatusAction(stage, statusCode, out var ensureAction))
        {
            statusAction = ensureAction;
            return true;
        }

        if (stage.JumpOnStatus is not null && stage.JumpOnStatus.TryGetValue(statusCode, out var jumpTarget))
        {
            statusAction = new WorkflowStageStatusAction { JumpTo = jumpTarget };
            return true;
        }

        return false;
    }

    public static bool IsExpectedStatus(WorkflowStageDefinition stage, int statusCode)
    {
        var expectedStatuses = BuildAllowedStatuses(stage);
        return expectedStatuses.Count == 0 || expectedStatuses.Contains(statusCode);
    }

    public static InvalidOperationException BuildUnexpectedStatusException(WorkflowStageDefinition stage, int statusCode)
    {
        var expectedStatuses = BuildAllowedStatuses(stage);
        if (expectedStatuses.Count > 0)
        {
            return new InvalidOperationException(
                $"Stage '{stage.Name}' returned {statusCode} but expected one of [{string.Join(", ", expectedStatuses.Order())}].");
        }

        return new InvalidOperationException(
            $"Stage '{stage.Name}' returned {statusCode} but expected {stage.ExpectedStatus}.");
    }

    public static HashSet<int> BuildAllowedStatuses(WorkflowStageDefinition stage)
    {
        var statuses = new HashSet<int>();
        if (stage.ExpectedStatuses is { Length: > 0 })
        {
            foreach (var status in stage.ExpectedStatuses)
            {
                statuses.Add(status);
            }
        }
        else if (stage.ExpectedStatus.HasValue)
        {
            statuses.Add(stage.ExpectedStatus.Value);
        }

        foreach (var status in GetEnsureExistsStatuses(stage))
        {
            statuses.Add(status);
        }

        return statuses;
    }

    public static void ApplyEnsureOutputs(
        WorkflowStageDefinition stage,
        int statusCode,
        IDictionary<string, string> stageOutput,
        IDictionary<string, JsonElement> stageOutputJson)
    {
        if (stage.Ensure is null)
        {
            return;
        }

        var existed = GetEnsureExistsStatuses(stage).Contains(statusCode);
        stageOutput["ensure_status"] = existed ? "existing" : "created";
        stageOutput["ensured"] = "true";
        stageOutput["existed"] = existed ? "true" : "false";
        AssignJsonValue(stageOutputJson, "ensure_status", stageOutput["ensure_status"]);
    }

    private static bool TryResolveEnsureStatusAction(
        WorkflowStageDefinition stage,
        int statusCode,
        out WorkflowStageStatusAction statusAction)
    {
        statusAction = new WorkflowStageStatusAction();
        if (stage.Ensure is null || !GetEnsureExistsStatuses(stage).Contains(statusCode))
        {
            return false;
        }

        var output = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ensure_status"] = "existing",
            ["ensured"] = "true",
            ["existed"] = "true"
        };
        if (stage.Ensure.Output is not null)
        {
            foreach (var pair in stage.Ensure.Output)
            {
                output[pair.Key] = pair.Value;
            }
        }

        statusAction = new WorkflowStageStatusAction
        {
            JumpTo = stage.Ensure.JumpTo,
            Message = stage.Ensure.Message,
            Output = output
        };
        return true;
    }

    private static IReadOnlyList<int> GetEnsureExistsStatuses(WorkflowStageDefinition stage)
    {
        if (stage.Ensure is null)
        {
            return Array.Empty<int>();
        }

        return stage.Ensure.ExistsOn is { Length: > 0 }
            ? stage.Ensure.ExistsOn
            : new[] { DefaultEnsureExistsStatus };
    }

    private static void AssignJsonValue(IDictionary<string, JsonElement> target, string key, string value)
    {
        if (JsonValueHelper.TryParse(value, out var json))
        {
            target[key] = json;
        }
    }
}
