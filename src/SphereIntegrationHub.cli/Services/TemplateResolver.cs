using System.Text.RegularExpressions;
using System.Text.Json;

using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class TemplateResolver
{
    private static readonly Regex TokenRegex = new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);
    private readonly ISystemTimeProvider _systemProvider;

    public TemplateResolver(ISystemTimeProvider? systemProvider = null)
    {
        _systemProvider = systemProvider ?? new SystemTimeProvider();
    }

    public string ResolveTemplate(string? template, TemplateContext context, ResponseContext? responseContext = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityTemplateResolve);
        activity?.SetTag(TelemetryConstants.TagTemplateLength, template?.Length ?? 0);

        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        return TokenRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value;
            var value = ResolveToken(token, context, responseContext, allowJsonStage: true);
            return value ?? string.Empty;
        });
    }

    public string ResolveToken(string token, TemplateContext context, ResponseContext? responseContext)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityTemplateTokenResolve);

        var segments = SplitToken(token);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException($"Invalid token '{token}'.");
        }

        var root = segments[0].ToLowerInvariant();
        activity?.SetTag(TelemetryConstants.TagTemplateTokenRoot, root);
        return root switch
        {
            "input" => ResolveInput(segments, context),
            "global" => ResolveGlobal(segments, context),
            "endpoint" => ResolveStageOutput(segments, context.EndpointOutputs, "endpoint", token),
            "workflow" => ResolveStageOutput(segments, context.WorkflowOutputs, "workflow", token),
            "stage" => ResolveStageOutputAny(segments, context, token),
            "context" => ResolveContext(segments, context),
            "env" => ResolveEnvironment(segments, context),
            "system" => ResolveSystem(segments, token),
            "response" => ResolveResponse(segments, responseContext, token),
            _ => throw new InvalidOperationException($"Unknown token root '{segments[0]}'.")
        };
    }

    public string ResolveToken(string token, TemplateContext context, ResponseContext? responseContext, bool allowJsonStage)
    {
        if (allowJsonStage &&
            token.StartsWith("stage:json(", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveStageJsonToken(token, context);
        }

        return ResolveToken(token, context, responseContext);
    }

    private static string ResolveInput(string[] segments, TemplateContext context)
    {
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("Input token requires a name.");
        }

        var name = segments[1];
        if (!context.Inputs.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Input '{name}' was not provided.");
        }

        return value;
    }

    private static string ResolveGlobal(string[] segments, TemplateContext context)
    {
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("Global token requires a name.");
        }

        var name = segments[1];
        if (!context.Globals.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Global variable '{name}' was not found.");
        }

        return value;
    }

    private static string ResolveContext(string[] segments, TemplateContext context)
    {
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("Context token requires a name.");
        }

        var name = segments[1];
        if (!context.Context.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"Context variable '{name}' was not found.");
        }

        return value;
    }

    private static string ResolveEnvironment(string[] segments, TemplateContext context)
    {
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("Env token requires a name.");
        }

        var name = segments[1];
        if (context.EnvVariables.TryGetValue(name, out var value))
        {
            return value;
        }

        value = Environment.GetEnvironmentVariable(name);
        if (value is null)
        {
            throw new InvalidOperationException($"Environment variable '{name}' was not found.");
        }

        return value;
    }

    private string ResolveSystem(string[] segments, string token)
    {
        if (segments.Length < 3)
        {
            throw new InvalidOperationException($"Invalid token '{token}'. Expected 'system:<datetime|date|time>.<now|utcnow>'.");
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

            throw new InvalidOperationException($"Invalid token '{token}'. Expected 'system:datetime.<now|utcnow>'.");
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

            throw new InvalidOperationException($"Invalid token '{token}'. Expected 'system:date.<now|utcnow>'.");
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

            throw new InvalidOperationException($"Invalid token '{token}'. Expected 'system:time.<now|utcnow>'.");
        }

        throw new InvalidOperationException($"Invalid token '{token}'. Expected 'system:<datetime|date|time>.<now|utcnow>'.");
    }

    private static string ResolveStageOutput(
        string[] segments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs,
        string kind,
        string token)
    {
        if (segments.Length < 4 || !segments[2].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid {kind} token '{token}'. Expected '{kind}:<name>.output.<key>'.");
        }

        var stageName = segments[1];
        var outputKey = segments[3];
        if (!outputs.TryGetValue(stageName, out var stageOutputs))
        {
            throw new InvalidOperationException($"{kind} '{stageName}' outputs were not found.");
        }

        if (!stageOutputs.TryGetValue(outputKey, out var value))
        {
            throw new InvalidOperationException($"{kind} '{stageName}' output '{outputKey}' was not found.");
        }

        return value;
    }

    private static string ResolveStageOutputAny(string[] segments, TemplateContext context, string token)
    {
        if (TryResolveStageWorkflowResult(segments, context.WorkflowResults, out var workflowResultValue))
        {
            return workflowResultValue;
        }

        if (TryResolveStageWorkflowOutput(segments, context.WorkflowOutputs, out var workflowOutputValue))
        {
            return workflowOutputValue;
        }

        if (TryResolveStageOutput(segments, context.EndpointOutputs, out var endpointValue, token))
        {
            return endpointValue;
        }

        if (TryResolveStageOutput(segments, context.WorkflowOutputs, out var workflowValue, token))
        {
            return workflowValue;
        }

        throw new InvalidOperationException($"Stage token '{token}' outputs were not found.");
    }

    private static bool TryResolveStageOutput(
        string[] segments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs,
        out string value,
        string token)
    {
        value = string.Empty;
        if (segments.Length < 4 || !segments[2].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid stage token '{token}'. Expected 'stage:<name>.output.<key>'.");
        }

        var stageName = segments[1];
        var outputKey = segments[3];
        if (!outputs.TryGetValue(stageName, out var stageOutputs))
        {
            return false;
        }

        if (stageOutputs.TryGetValue(outputKey, out var found) && found is not null)
        {
            value = found;
            return true;
        }

        return false;
    }

    private static bool TryResolveStageWorkflowOutput(
        string[] segments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs,
        out string value)
    {
        value = string.Empty;
        if (segments.Length < 5 ||
            !segments[2].Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stageName = segments[1];
        var outputKey = segments[4];
        if (!outputs.TryGetValue(stageName, out var stageOutputs))
        {
            return false;
        }

        if (stageOutputs.TryGetValue(outputKey, out var found) && found is not null)
        {
            value = found;
            return true;
        }

        return false;
    }

    private static bool TryResolveStageWorkflowResult(
        string[] segments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> results,
        out string value)
    {
        value = string.Empty;
        if (segments.Length < 5 ||
            !segments[2].Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("result", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var stageName = segments[1];
        var resultKey = segments[4];
        if (!results.TryGetValue(stageName, out var stageResults))
        {
            return false;
        }

        if (stageResults.TryGetValue(resultKey, out var found) && found is not null)
        {
            value = found;
            return true;
        }

        return false;
    }

    private static string ResolveResponse(string[] segments, ResponseContext? responseContext, string token)
    {
        if (responseContext is null)
        {
            throw new InvalidOperationException($"Response token '{token}' is not available.");
        }

        if (segments.Length < 2)
        {
            throw new InvalidOperationException("Response token requires a path.");
        }

        if (segments[1].Equals("status", StringComparison.OrdinalIgnoreCase))
        {
            return responseContext.StatusCode.ToString();
        }

        if (segments[1].Equals("body", StringComparison.OrdinalIgnoreCase))
        {
            return responseContext.Body;
        }

        if (segments[1].Equals("headers", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length < 3)
            {
                throw new InvalidOperationException("Response headers token requires a header name.");
            }

            if (responseContext.Headers.TryGetValue(segments[2], out var headerValue))
            {
                return headerValue;
            }

            throw new InvalidOperationException($"Response header '{segments[2]}' was not found.");
        }

        if (responseContext.Json is null)
        {
            throw new InvalidOperationException($"Response token '{token}' requires a JSON body.");
        }

        var element = responseContext.Json.RootElement;
        for (var i = 1; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment, out var property))
            {
                element = property;
                continue;
            }

            if (element.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
            {
                if (index >= 0 && index < element.GetArrayLength())
                {
                    element = element[index];
                    continue;
                }
            }

            throw new InvalidOperationException($"Response path '{string.Join(".", segments.Skip(1))}' was not found.");
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.ToString()
        };
    }

    public static string[] SplitToken(string token)
    {
        var normalized = token.Replace(":", ".", StringComparison.Ordinal);
        return normalized.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    public static IEnumerable<string> ExtractTokens(string template)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            yield break;
        }

        var matches = TokenRegex.Matches(template);
        foreach (Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                yield return match.Groups[1].Value;
            }
        }
    }

    private static string ResolveStageJsonToken(string token, TemplateContext context)
    {
        const string prefix = "stage:json(";
        if (!token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid stage json token '{token}'.");
        }

        var closeIndex = token.IndexOf(')', prefix.Length);
        if (closeIndex <= prefix.Length)
        {
            throw new InvalidOperationException($"Invalid stage json token '{token}'. Expected closing ')'.");
        }

        var inner = token[prefix.Length..closeIndex];
        var remainder = token[(closeIndex + 1)..];
        if (remainder.StartsWith(".", StringComparison.Ordinal))
        {
            remainder = remainder[1..];
        }

        var innerSegments = inner.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (innerSegments.Length < 3 || !innerSegments[1].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid stage json token '{token}'. Expected 'stage:json(<stage>.output.<key>)'.");
        }

        var stageSegments = new[] { "stage", innerSegments[0], "output", innerSegments[2] };
        var jsonPayload = ResolveStageOutputAny(stageSegments, context, token);

        using var document = JsonDocument.Parse(jsonPayload);
        var element = document.RootElement;

        if (!string.IsNullOrWhiteSpace(remainder))
        {
            var pathSegments = remainder.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var segment in pathSegments)
            {
                if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(segment, out var property))
                {
                    element = property;
                    continue;
                }

                if (element.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
                {
                    if (index >= 0 && index < element.GetArrayLength())
                    {
                        element = element[index];
                        continue;
                    }
                }

                throw new InvalidOperationException($"Stage json token path '{remainder}' was not found.");
            }
        }

        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.Number => element.ToString(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => string.Empty,
            _ => element.ToString()
        };
    }
}

public sealed record TemplateContext(
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string> Globals,
    IReadOnlyDictionary<string, string> Context,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EndpointOutputs,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> WorkflowOutputs,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> WorkflowResults,
    IReadOnlyDictionary<string, string> EnvVariables);

public sealed record ResponseContext(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    JsonDocument? Json);
