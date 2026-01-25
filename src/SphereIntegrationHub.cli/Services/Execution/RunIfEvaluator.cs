using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;
using System;
using System.Collections.Generic;

namespace SphereIntegrationHub.Services;

internal sealed class RunIfEvaluator : IRunIfEvaluator
{
    private readonly ISystemTimeProvider _systemProvider;

    public RunIfEvaluator(ISystemTimeProvider systemProvider)
    {
        _systemProvider = systemProvider ?? throw new ArgumentNullException(nameof(systemProvider));
    }

    public bool ShouldRunStage(WorkflowStageDefinition stage, ExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(stage.RunIf))
        {
            return true;
        }

        return EvaluateCondition(stage.RunIf, context);
    }

    private bool EvaluateCondition(string expression, ExecutionContext context)
    {
        if (!RunIfParser.TryParse(expression, out var token, out var op, out var rawValue))
        {
            throw new InvalidOperationException($"Invalid runIf expression '{expression}'.");
        }

        var actual = ResolveNullableToken(token, context);
        if (op.Equals("in", StringComparison.OrdinalIgnoreCase))
        {
            var values = NormalizeRunIfList(rawValue);
            return values.Contains(actual ?? string.Empty);
        }

        if (op.Equals("not in", StringComparison.OrdinalIgnoreCase))
        {
            var values = NormalizeRunIfList(rawValue);
            return !values.Contains(actual ?? string.Empty);
        }

        var expected = NormalizeRunIfValue(rawValue, out var expectedIsNull);
        var isEqual = expectedIsNull
            ? string.IsNullOrEmpty(actual)
            : string.Equals(actual ?? string.Empty, expected ?? string.Empty, StringComparison.Ordinal);
        return op == "==" ? isEqual : !isEqual;
    }

    private static string? NormalizeRunIfValue(string rawValue, out bool expectedIsNull)
    {
        expectedIsNull = rawValue.Equals("null", StringComparison.OrdinalIgnoreCase);
        if (expectedIsNull)
        {
            return null;
        }

        if (rawValue.Length >= 2 &&
            ((rawValue.StartsWith('"') && rawValue.EndsWith('"')) ||
             (rawValue.StartsWith('\'') && rawValue.EndsWith('\''))))
        {
            return rawValue[1..^1];
        }

        return rawValue;
    }

    private static HashSet<string> NormalizeRunIfList(string rawValue)
    {
        var values = new HashSet<string>(StringComparer.Ordinal);
        if (!rawValue.StartsWith('[') || !rawValue.EndsWith(']'))
        {
            return values;
        }

        var inner = rawValue[1..^1];
        if (string.IsNullOrWhiteSpace(inner))
        {
            return values;
        }

        foreach (var item in inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            values.Add(item);
        }

        return values;
    }

    private string? ResolveNullableToken(string token, ExecutionContext context)
    {
        var segments = TemplateResolver.SplitToken(token);
        if (segments.Length < 2)
        {
            return null;
        }

        var root = segments[0].ToLowerInvariant();
        var name = segments[1];

        return root switch
        {
            "input" => context.Inputs.TryGetValue(name, out var inputValue) ? inputValue : null,
            "global" => context.Globals.TryGetValue(name, out var globalValue) ? globalValue : null,
            "context" => context.Context.TryGetValue(name, out var contextValue) ? contextValue : null,
            "endpoint" => TryResolveStageOutput(context.EndpointOutputs, name, segments, out var endpointValue) ? endpointValue : null,
            "workflow" => TryResolveStageOutput(context.WorkflowOutputs, name, segments, out var workflowValue) ? workflowValue : null,
            "stage" => TryResolveStageOutput(context.EndpointOutputs, name, segments, out var stageEndpointValue)
                ? stageEndpointValue
                : TryResolveStageOutput(context.WorkflowOutputs, name, segments, out var stageWorkflowValue)
                    ? stageWorkflowValue
                    : TryResolveStageWorkflowResult(context.WorkflowResults, name, segments, out var stageWorkflowResultValue)
                        ? stageWorkflowResultValue
                        : null,
            "env" => context.EnvironmentVariables.TryGetValue(name, out var envValue)
                ? envValue
                : Environment.GetEnvironmentVariable(name),
            "system" => ResolveSystemToken(segments),
            _ => null
        };
    }

    private string? ResolveSystemToken(string[] segments)
    {
        if (segments.Length < 3)
        {
            return null;
        }

        if (segments[1].Equals("datetime", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[2].Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                return _systemProvider.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (segments[2].Equals("utcnow", StringComparison.OrdinalIgnoreCase))
            {
                return _systemProvider.UtcNow.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        if (segments[1].Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[2].Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                return DateOnly.FromDateTime(_systemProvider.Now.DateTime)
                    .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (segments[2].Equals("utcnow", StringComparison.OrdinalIgnoreCase))
            {
                return DateOnly.FromDateTime(_systemProvider.UtcNow.DateTime)
                    .ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        if (segments[1].Equals("time", StringComparison.OrdinalIgnoreCase))
        {
            if (segments[2].Equals("now", StringComparison.OrdinalIgnoreCase))
            {
                return TimeOnly.FromDateTime(_systemProvider.Now.DateTime)
                    .ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }

            if (segments[2].Equals("utcnow", StringComparison.OrdinalIgnoreCase))
            {
                return TimeOnly.FromDateTime(_systemProvider.UtcNow.DateTime)
                    .ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
            }

            return null;
        }

        return null;
    }

    private static bool TryResolveStageOutput(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs,
        string stageName,
        string[] segments,
        out string value)
    {
        value = string.Empty;
        if (segments.Length < 4 || !segments[2].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!outputs.TryGetValue(stageName, out var stageOutputs))
        {
            return false;
        }

        var key = segments[3];
        if (!stageOutputs.TryGetValue(key, out var found) || found is null)
        {
            value = string.Empty;
            return false;
        }

        value = found;
        return true;
    }

    private static bool TryResolveStageWorkflowResult(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> results,
        string stageName,
        string[] segments,
        out string value)
    {
        value = string.Empty;
        if (segments.Length < 5 ||
            !segments[2].Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("result", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!results.TryGetValue(stageName, out var stageResults))
        {
            return false;
        }

        var key = segments[4];
        if (!stageResults.TryGetValue(key, out var found) || found is null)
        {
            value = string.Empty;
            return false;
        }

        value = found;
        return true;
    }
}
