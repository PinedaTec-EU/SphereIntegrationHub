using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class TemplateResolver
{
    private static readonly Regex TokenRegex = new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    // Matches: system:datetime.utcnow + 0001:06:15-01:02:03
    //          system:date.now - 0000:01:00
    //          system:time.utcnow
    private static readonly Regex SystemTokenRegex = new(
        @"^system[.:](?<type>datetime|date|time)[.:](?<variant>now|utcnow)(?:\s*(?<sign>[+\-])\s*(?<years>\d+):(?<months>\d+):(?<days>\d+)(?:-(?<hours>\d+):(?<minutes>\d+):(?<seconds>\d+))?)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ISystemTimeProvider _systemProvider;

    public TemplateResolver(ISystemTimeProvider? systemProvider = null)
    {
        _systemProvider = systemProvider ?? new SystemTimeProvider();
    }

    public string ResolveTemplate(string template, TemplateContext context, ResponseContext? responseContext = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityTemplateResolve);
        activity?.SetTag(TelemetryConstants.TagTemplateLength, template?.Length ?? 0);

        if (string.IsNullOrEmpty(template))
        {
            return template ?? string.Empty;
        }

        return TokenRegex.Replace(template, match =>
        {
            var token = match.Groups[1].Value;
            var value = ResolveTokenValue(token, context, responseContext, allowJsonStage: true);
            return value.StringValue ?? string.Empty;
        });
    }

    public string ResolveToken(string token, TemplateContext context, ResponseContext? responseContext)
    {
        var value = ResolveTokenValue(token, context, responseContext, allowJsonStage: false);
        if (!value.Exists)
        {
            throw new InvalidOperationException($"Token '{token}' could not be resolved.");
        }

        return value.StringValue ?? string.Empty;
    }

    public string ResolveToken(string token, TemplateContext context, ResponseContext? responseContext, bool allowJsonStage)
    {
        var value = ResolveTokenValue(token, context, responseContext, allowJsonStage);
        if (!value.Exists)
        {
            throw new InvalidOperationException($"Token '{token}' could not be resolved.");
        }

        return value.StringValue ?? string.Empty;
    }

    public ResolvedTokenValue ResolveTokenValue(
        string token,
        TemplateContext context,
        ResponseContext? responseContext,
        bool allowJsonStage)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityTemplateTokenResolve);

        if (allowJsonStage &&
            token.StartsWith("stage:json(", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveStageJsonTokenValue(token, context);
        }

        var segments = SplitToken(token);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException($"Invalid token '{token}'.");
        }

        var root = segments[0].ToLowerInvariant();
        activity?.SetTag(TelemetryConstants.TagTemplateTokenRoot, root);
        return root switch
        {
            "input" => ResolveScopedValue(segments, context.Inputs, context.InputJson, "Input", token),
            "global" => ResolveScopedValue(segments, context.Globals, context.GlobalJson, "Global", token),
            "context" => ResolveScopedValue(segments, context.Context, context.ContextJson, "Context", token),
            "endpoint" => ResolveStageOutputValue(segments, context.EndpointOutputs, context.EndpointOutputJson, "endpoint", token),
            "workflow" => ResolveStageOutputValue(segments, context.WorkflowOutputs, context.WorkflowOutputJson, "workflow", token),
            "stage" => ResolveStageOutputAnyValue(segments, context, token),
            "env" => ResolveEnvironmentValue(segments, context),
            "system" => ResolvedTokenValue.FromString(ResolveSystem(segments, token)),
            "response" => ResolveResponseValue(segments, responseContext, token),
            _ => throw new InvalidOperationException($"Unknown token root '{segments[0]}'.")
        };
    }

    private static ResolvedTokenValue ResolveScopedValue(
        string[] segments,
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, JsonElement>? jsonValues,
        string label,
        string token)
    {
        if (segments.Length < 2)
        {
            throw new InvalidOperationException($"{label} token requires a name.");
        }

        var name = segments[1];
        if (!values.TryGetValue(name, out var value))
        {
            throw new InvalidOperationException($"{label} '{name}' was not found.");
        }

        if (segments.Length == 2)
        {
            if (jsonValues is not null && jsonValues.TryGetValue(name, out var rootJson))
            {
                return ResolvedTokenValue.FromJson(rootJson);
            }

            return ResolvedTokenValue.FromString(value);
        }

        if (jsonValues is not null &&
            jsonValues.TryGetValue(name, out var jsonValue) &&
            JsonValueHelper.TryResolvePath(jsonValue, segments.Skip(2).ToArray(), out var nested))
        {
            return ResolvedTokenValue.FromJson(nested);
        }

        throw new InvalidOperationException($"{label} path '{token}' was not found.");
    }

    private static ResolvedTokenValue ResolveEnvironmentValue(string[] segments, TemplateContext context)
    {
        if (segments.Length < 2)
        {
            throw new InvalidOperationException("Env token requires a name.");
        }

        var name = segments[1];
        if (context.EnvVariables.TryGetValue(name, out var value))
        {
            return ResolvedTokenValue.FromString(value);
        }

        value = Environment.GetEnvironmentVariable(name);
        if (value is null)
        {
            throw new InvalidOperationException($"Environment variable '{name}' was not found.");
        }

        return ResolvedTokenValue.FromString(value);
    }

    private string ResolveSystem(string[] segments, string token)
    {
        if (segments.Length < 3)
        {
            throw new InvalidOperationException($"Invalid token '{token}'. Expected 'system:<datetime|date|time>.<now|utcnow>' with optional offset '±YYYY:MM:DD' or '±YYYY:MM:DD-HH:mm:ss'.");
        }

        var match = SystemTokenRegex.Match(token);
        if (!match.Success)
        {
            throw new InvalidOperationException($"Invalid token '{token}'. Expected 'system:<datetime|date|time>.<now|utcnow>' with optional offset '±YYYY:MM:DD' or '±YYYY:MM:DD-HH:mm:ss'.");
        }

        var type = match.Groups["type"].Value;
        var isUtc = match.Groups["variant"].Value.Equals("utcnow", StringComparison.OrdinalIgnoreCase);
        var baseDateTime = isUtc ? _systemProvider.UtcNow : _systemProvider.Now;

        if (match.Groups["sign"].Success)
        {
            var sign = match.Groups["sign"].Value == "+" ? 1 : -1;
            var years = int.Parse(match.Groups["years"].Value, CultureInfo.InvariantCulture);
            var months = int.Parse(match.Groups["months"].Value, CultureInfo.InvariantCulture);
            var days = int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture);

            baseDateTime = baseDateTime.AddYears(sign * years).AddMonths(sign * months).AddDays(sign * days);

            if (match.Groups["hours"].Success)
            {
                var hours = int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture);
                var minutes = int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture);
                var seconds = int.Parse(match.Groups["seconds"].Value, CultureInfo.InvariantCulture);

                baseDateTime = baseDateTime.AddHours(sign * hours).AddMinutes(sign * minutes).AddSeconds(sign * seconds);
            }
        }

        if (type.Equals("datetime", StringComparison.OrdinalIgnoreCase))
        {
            return baseDateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (type.Equals("date", StringComparison.OrdinalIgnoreCase))
        {
            return DateOnly.FromDateTime(baseDateTime.DateTime)
                .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // time
        return TimeOnly.FromDateTime(baseDateTime.DateTime)
            .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static ResolvedTokenValue ResolveStageOutputValue(
        string[] segments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? jsonOutputs,
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

        if (segments.Length == 4)
        {
            if (jsonOutputs is not null &&
                jsonOutputs.TryGetValue(stageName, out var stageJsonOutputs) &&
                stageJsonOutputs.TryGetValue(outputKey, out var jsonValue))
            {
                return ResolvedTokenValue.FromJson(jsonValue);
            }

            return ResolvedTokenValue.FromString(value);
        }

        if (jsonOutputs is not null &&
            jsonOutputs.TryGetValue(stageName, out var nestedJsonOutputs) &&
            nestedJsonOutputs.TryGetValue(outputKey, out var nestedJson) &&
            JsonValueHelper.TryResolvePath(nestedJson, segments.Skip(4).ToArray(), out var nestedResolved))
        {
            return ResolvedTokenValue.FromJson(nestedResolved);
        }

        throw new InvalidOperationException($"{kind} path '{token}' was not found.");
    }

    private static ResolvedTokenValue ResolveStageOutputAnyValue(string[] segments, TemplateContext context, string token)
    {
        if (TryResolveStageWorkflowResult(segments, context.WorkflowResults, out var workflowResultValue))
        {
            return ResolvedTokenValue.FromString(workflowResultValue);
        }

        if (TryResolveStageWorkflowOutput(segments, context.WorkflowOutputs, context.WorkflowOutputJson, out var workflowOutputValue))
        {
            return workflowOutputValue;
        }

        if (TryResolveStageOutput(segments, context.EndpointOutputs, context.EndpointOutputJson, out var endpointValue, token))
        {
            return endpointValue;
        }

        if (TryResolveStageOutput(segments, context.WorkflowOutputs, context.WorkflowOutputJson, out var workflowValue, token))
        {
            return workflowValue;
        }

        throw new InvalidOperationException($"Stage token '{token}' outputs were not found.");
    }

    private static bool TryResolveStageOutput(
        string[] segments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? jsonOutputs,
        out ResolvedTokenValue value,
        string token)
    {
        value = ResolvedTokenValue.NotFound;
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

        if (!stageOutputs.TryGetValue(outputKey, out var resolved) || resolved is null)
        {
            return false;
        }

        if (segments.Length == 4)
        {
            if (jsonOutputs is not null &&
                jsonOutputs.TryGetValue(stageName, out var stageJsonOutputs) &&
                stageJsonOutputs.TryGetValue(outputKey, out var jsonValue))
            {
                value = ResolvedTokenValue.FromJson(jsonValue);
                return true;
            }

            value = ResolvedTokenValue.FromString(resolved);
            return true;
        }

        if (jsonOutputs is not null &&
            jsonOutputs.TryGetValue(stageName, out var nestedOutputs) &&
            nestedOutputs.TryGetValue(outputKey, out var nestedJson) &&
            JsonValueHelper.TryResolvePath(nestedJson, segments.Skip(4).ToArray(), out var nestedResolved))
        {
            value = ResolvedTokenValue.FromJson(nestedResolved);
            return true;
        }

        return false;
    }

    private static bool TryResolveStageWorkflowOutput(
        string[] segments,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> outputs,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? jsonOutputs,
        out ResolvedTokenValue value)
    {
        value = ResolvedTokenValue.NotFound;
        if (segments.Length < 5 ||
            !segments[2].Equals("workflow", StringComparison.OrdinalIgnoreCase) ||
            !segments[3].Equals("output", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var workflowSegments = new[] { "workflow", segments[1], "output", segments[4] }.Concat(segments.Skip(5)).ToArray();
        value = ResolveStageOutputValue(workflowSegments, outputs, jsonOutputs, "workflow", string.Join(".", workflowSegments));
        return true;
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

        if (!stageResults.TryGetValue(resultKey, out var resolved) || resolved is null)
        {
            return false;
        }

        value = resolved;
        return true;
    }

    private static ResolvedTokenValue ResolveResponseValue(string[] segments, ResponseContext? responseContext, string token)
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
            return ResolvedTokenValue.FromString(responseContext.StatusCode.ToString());
        }

        if (segments[1].Equals("body", StringComparison.OrdinalIgnoreCase))
        {
            if (responseContext.Json is not null)
            {
                if (segments.Length == 2)
                {
                    return ResolvedTokenValue.FromJson(responseContext.Json.RootElement);
                }

                if (JsonValueHelper.TryResolvePath(responseContext.Json.RootElement, segments.Skip(2).ToArray(), out var nestedBody))
                {
                    return ResolvedTokenValue.FromJson(nestedBody);
                }
            }

            if (segments.Length == 2)
            {
                return ResolvedTokenValue.FromString(responseContext.Body);
            }

            throw new InvalidOperationException($"Response token '{token}' requires a JSON body.");
        }

        if (segments[1].Equals("headers", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length < 3)
            {
                throw new InvalidOperationException("Response headers token requires a header name.");
            }

            if (responseContext.Headers.TryGetValue(segments[2], out var headerValue))
            {
                return ResolvedTokenValue.FromString(headerValue);
            }

            throw new InvalidOperationException($"Response header '{segments[2]}' was not found.");
        }

        if (responseContext.Json is null)
        {
            throw new InvalidOperationException($"Response token '{token}' requires a JSON body.");
        }

        if (!JsonValueHelper.TryResolvePath(responseContext.Json.RootElement, segments.Skip(1).ToArray(), out var resolved))
        {
            throw new InvalidOperationException($"Response path '{string.Join(".", segments.Skip(1))}' was not found.");
        }

        return ResolvedTokenValue.FromJson(resolved);
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
        var value = ResolveStageJsonTokenValue(token, context);
        return value.StringValue ?? string.Empty;
    }

    private static ResolvedTokenValue ResolveStageJsonTokenValue(string token, TemplateContext context)
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

        var stageSegments = new[] { "stage", innerSegments[0], "output", innerSegments[2] }.Concat(innerSegments.Skip(3)).ToArray();
        var tokenValue = ResolveStageOutputAnyValue(stageSegments, context, token);
        if (!tokenValue.JsonValue.HasValue && tokenValue.StringValue is not null && JsonValueHelper.TryParse(tokenValue.StringValue, out var parsedJson))
        {
            tokenValue = ResolvedTokenValue.FromJson(parsedJson);
        }

        if (!tokenValue.JsonValue.HasValue)
        {
            throw new InvalidOperationException($"Stage json token '{token}' requires JSON content.");
        }

        var element = tokenValue.JsonValue.Value;
        if (!string.IsNullOrWhiteSpace(remainder))
        {
            var pathSegments = remainder.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!JsonValueHelper.TryResolvePath(element, pathSegments, out element))
            {
                throw new InvalidOperationException($"Stage json token path '{remainder}' was not found.");
            }
        }

        return ResolvedTokenValue.FromJson(element);
    }
}

public sealed record TemplateContext(
    IReadOnlyDictionary<string, string> Inputs,
    IReadOnlyDictionary<string, string> Globals,
    IReadOnlyDictionary<string, string> Context,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> EndpointOutputs,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> WorkflowOutputs,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> WorkflowResults,
    IReadOnlyDictionary<string, string> EnvVariables,
    IReadOnlyDictionary<string, JsonElement>? InputJson = null,
    IReadOnlyDictionary<string, JsonElement>? GlobalJson = null,
    IReadOnlyDictionary<string, JsonElement>? ContextJson = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? EndpointOutputJson = null,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, JsonElement>>? WorkflowOutputJson = null,
    string? WorkflowPath = null);

public sealed record ResponseContext(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    JsonDocument? Json);

public readonly record struct ResolvedTokenValue(bool Exists, string? StringValue, JsonElement? JsonValue)
{
    public static ResolvedTokenValue NotFound { get; } = new(false, null, null);

    public static ResolvedTokenValue FromString(string? value)
        => value is null ? NotFound : new(true, value, null);

    public static ResolvedTokenValue FromJson(JsonElement value)
        => new(true, JsonValueHelper.ToDisplayString(value), value.Clone());
}
