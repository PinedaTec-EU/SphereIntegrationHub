using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class TemplateResolver
{
    private static readonly Regex TokenRegex = new(@"\{\{\s*(.+?)\s*\}\}", RegexOptions.Compiled);

    // Matches: system:datetime.utcnow + P1DT1H2M3S
    //          system:date.now - P1Y6M
    //          system:time.utcnow
    // Duration follows ISO 8601: P[nY][nM][nD][T[nH][nM][nS]]
    // Reference: https://www.iso.org/iso-8601-date-and-time-format.html
    private static readonly Regex SystemTokenRegex = new(
        @"^system[.:](?<type>datetime|date|time)[.:](?<variant>now|utcnow)(?:\s*(?<sign>[+\-])\s*(?<duration>P(?:\d+Y)?(?:\d+M)?(?:\d+D)?(?:T(?:\d+H)?(?:\d+M)?(?:\d+S)?)?))?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex IsoDurationRegex = new(
        @"^P(?:(?<years>\d+)Y)?(?:(?<months>\d+)M)?(?:(?<days>\d+)D)?(?:T(?:(?<hours>\d+)H)?(?:(?<minutes>\d+)M)?(?:(?<seconds>\d+)S)?)?$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ISystemTimeProvider _systemProvider;
    private readonly IRandomValueService _randomValueService;
    private readonly TemplateTokenResolverRegistry _tokenResolvers;

    public TemplateResolver(ISystemTimeProvider? systemProvider = null, IRandomValueService? randomValueService = null)
    {
        _systemProvider = systemProvider ?? new SystemTimeProvider();
        _randomValueService = randomValueService ?? new DynamicValueService(_systemProvider);
        _tokenResolvers = new TemplateTokenResolverRegistry()
            .Register("input", (segments, context, _, token) => ResolveScopedValue(segments, context.Inputs, context.InputJson, "Input", token))
            .Register("global", (segments, context, _, token) => ResolveScopedValue(segments, context.Globals, context.GlobalJson, "Global", token))
            .Register("context", (segments, context, _, token) => ResolveScopedValue(segments, context.Context, context.ContextJson, "Context", token))
            .Register("endpoint", (segments, context, _, token) => ResolveStageOutputValue(segments, context.EndpointOutputs, context.EndpointOutputJson, "endpoint", token))
            .Register("workflow", (segments, context, _, token) => ResolveStageOutputValue(segments, context.WorkflowOutputs, context.WorkflowOutputJson, "workflow", token))
            .Register("stage", (segments, context, _, token) => ResolveStageOutputAnyValue(segments, context, token))
            .Register("var", (segments, context, _, token) => ResolveWorkflowVarValue(segments, context, token))
            .Register("env", ResolveEnvironmentTokenValue)
            .Register("system", (segments, _, _, token) => ResolvedTokenValue.FromString(ResolveSystem(segments, token)))
            .Register("response", (segments, _, responseContext, token) => ResolveResponseValue(segments, responseContext, token));
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

        if (token.StartsWith("coalesce(", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveCoalesceTokenValue(token, context, responseContext, allowJsonStage);
        }

        if (token.StartsWith("rand:", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveRandomTokenValue(token, context, responseContext, allowJsonStage);
        }

        var segments = SplitToken(token);
        if (segments.Length == 0)
        {
            throw new InvalidOperationException($"Invalid token '{token}'.");
        }

        var root = segments[0].ToLowerInvariant();
        activity?.SetTag(TelemetryConstants.TagTemplateTokenRoot, root);
        return _tokenResolvers.Resolve(root, segments, context, responseContext, token);
    }

    private static ResolvedTokenValue ResolveEnvironmentTokenValue(
        string[] segments,
        TemplateContext context,
        ResponseContext? _,
        string token)
    {
        if (token.StartsWith("env.", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Invalid env token '{token}'. Use '{{{{env:NAME}}}}' syntax.");
        }

        return ResolveEnvironmentValue(segments, context);
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
            throw new InvalidOperationException(
                $"Invalid token '{token}'. Expected 'system:<datetime|date|time>.<now|utcnow>' with optional ISO 8601 duration offset, e.g. '{{{{system:datetime.utcnow + P1DT2H}}}}'.");
        }

        var match = SystemTokenRegex.Match(token);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                $"Invalid token '{token}'. Expected 'system:<datetime|date|time>.<now|utcnow>' with optional ISO 8601 duration offset, e.g. '{{{{system:date.now + P5D}}}}'.");
        }

        var type = match.Groups["type"].Value;
        var isUtc = match.Groups["variant"].Value.Equals("utcnow", StringComparison.OrdinalIgnoreCase);
        var baseDateTime = isUtc ? _systemProvider.UtcNow : _systemProvider.Now;

        if (match.Groups["duration"].Success)
        {
            var sign = match.Groups["sign"].Value == "+" ? 1 : -1;
            var durationMatch = IsoDurationRegex.Match(match.Groups["duration"].Value);
            if (!durationMatch.Success)
            {
                throw new InvalidOperationException(
                    $"Invalid ISO 8601 duration in token '{token}'. Expected format: P[nY][nM][nD][T[nH][nM][nS]], e.g. P1Y2M3DT4H5M6S.");
            }

            baseDateTime = ApplyIsoDuration(baseDateTime, durationMatch, sign);
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

        return TimeOnly.FromDateTime(baseDateTime.DateTime)
            .ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ApplyIsoDuration(DateTimeOffset dt, Match durationMatch, int sign)
    {
        if (durationMatch.Groups["years"].Success)
            dt = dt.AddYears(sign * int.Parse(durationMatch.Groups["years"].Value, CultureInfo.InvariantCulture));
        if (durationMatch.Groups["months"].Success)
            dt = dt.AddMonths(sign * int.Parse(durationMatch.Groups["months"].Value, CultureInfo.InvariantCulture));
        if (durationMatch.Groups["days"].Success)
            dt = dt.AddDays(sign * int.Parse(durationMatch.Groups["days"].Value, CultureInfo.InvariantCulture));
        if (durationMatch.Groups["hours"].Success)
            dt = dt.AddHours(sign * int.Parse(durationMatch.Groups["hours"].Value, CultureInfo.InvariantCulture));
        if (durationMatch.Groups["minutes"].Success)
            dt = dt.AddMinutes(sign * int.Parse(durationMatch.Groups["minutes"].Value, CultureInfo.InvariantCulture));
        if (durationMatch.Groups["seconds"].Success)
            dt = dt.AddSeconds(sign * int.Parse(durationMatch.Groups["seconds"].Value, CultureInfo.InvariantCulture));
        return dt;
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
        // Mejora 3: safe navigation — strip trailing '?' from stage name and/or output key
        var safeStage = segments.Length > 1 && segments[1].EndsWith('?');
        var safeKey = segments.Length > 3 && segments[3].EndsWith('?');

        string[]? normalized = null;
        if (safeStage || safeKey)
        {
            normalized = segments.ToArray();
            if (safeStage) normalized[1] = segments[1][..^1];
            if (safeKey) normalized[3] = segments[3][..^1];
        }

        var s = normalized ?? segments;

        // Mejora 1: skipped stages — check onSkip.output first, then return empty
        if (s.Length > 1 && context.SkippedStages?.Contains(s[1]) == true)
        {
            // A skipped stage may have onSkip.output registered — prefer those values
            if (TryResolveStageOutput(s, context.EndpointOutputs, context.EndpointOutputJson, out var skipEndpoint, token))
            {
                return skipEndpoint;
            }

            if (TryResolveStageOutput(s, context.WorkflowOutputs, context.WorkflowOutputJson, out var skipWorkflow, token))
            {
                return skipWorkflow;
            }

            return ResolvedTokenValue.NotFound;
        }

        try
        {
            if (TryResolveStageWorkflowResult(s, context.WorkflowResults, out var workflowResultValue))
            {
                return ResolvedTokenValue.FromString(workflowResultValue);
            }

            if (TryResolveStageWorkflowOutput(s, context.WorkflowOutputs, context.WorkflowOutputJson, out var workflowOutputValue))
            {
                return workflowOutputValue;
            }
        }
        catch (InvalidOperationException) when (safeStage || safeKey)
        {
            return ResolvedTokenValue.NotFound;
        }

        if (TryResolveStageOutput(s, context.EndpointOutputs, context.EndpointOutputJson, out var endpointValue, token))
        {
            return endpointValue;
        }

        if (TryResolveStageOutput(s, context.WorkflowOutputs, context.WorkflowOutputJson, out var workflowValue, token))
        {
            return workflowValue;
        }

        if (safeStage || safeKey)
        {
            return ResolvedTokenValue.NotFound;
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
        return token.Split(new[] { '.', ':' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
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

    private ResolvedTokenValue ResolveWorkflowVarValue(string[] segments, TemplateContext context, string token)
    {
        if (segments.Length < 2)
        {
            throw new InvalidOperationException($"var token requires a name: '{token}'.");
        }

        var varName = segments[1];
        if (context.WorkflowVars is null || !context.WorkflowVars.TryGetValue(varName, out var varTemplate))
        {
            throw new InvalidOperationException($"Workflow var '{varName}' was not found.");
        }

        var resolved = ResolveTemplate(varTemplate, context);

        if (segments.Length == 2)
        {
            if (JsonValueHelper.TryParse(resolved, out var rootJson))
            {
                return ResolvedTokenValue.FromJson(rootJson);
            }

            return ResolvedTokenValue.FromString(resolved);
        }

        if (JsonValueHelper.TryParse(resolved, out var json) &&
            JsonValueHelper.TryResolvePath(json, segments.Skip(2).ToArray(), out var nested))
        {
            return ResolvedTokenValue.FromJson(nested);
        }

        throw new InvalidOperationException($"Workflow var path '{token}' was not found.");
    }

    private ResolvedTokenValue ResolveCoalesceTokenValue(
        string token,
        TemplateContext context,
        ResponseContext? responseContext,
        bool allowJsonStage)
    {
        const string prefix = "coalesce(";
        if (!token.EndsWith(")"))
        {
            throw new InvalidOperationException($"Invalid coalesce token '{token}'. Expected 'coalesce(token1, token2, ...)'.");
        }

        var argsContent = token[prefix.Length..^1];
        foreach (var arg in SplitFunctionArguments(argsContent))
        {
            var trimmed = arg.Trim();
            if (string.IsNullOrEmpty(trimmed))
            {
                continue;
            }

            try
            {
                var value = ResolveTokenValue(trimmed, context, responseContext, allowJsonStage);
                if (value.Exists && !string.IsNullOrEmpty(value.StringValue))
                {
                    return value;
                }
            }
            catch (InvalidOperationException)
            {
                // Argument could not be resolved — try the next one
            }
        }

        return ResolvedTokenValue.FromString(string.Empty);
    }

    private static IEnumerable<string> SplitFunctionArguments(string content)
    {
        var depth = 0;
        char? quote = null;
        var start = 0;
        for (var i = 0; i < content.Length; i++)
        {
            if (quote.HasValue)
            {
                if (content[i] == quote.Value && (i == 0 || content[i - 1] != '\\'))
                {
                    quote = null;
                }

                continue;
            }

            switch (content[i])
            {
                case '\'':
                case '"':
                    quote = content[i];
                    break;
                case '(': depth++; break;
                case ')': depth--; break;
                case ',' when depth == 0:
                    yield return content[start..i];
                    start = i + 1;
                    break;
            }
        }

        yield return content[start..];
    }

    private ResolvedTokenValue ResolveRandomTokenValue(
        string token,
        TemplateContext context,
        ResponseContext? responseContext,
        bool allowJsonStage)
    {
        var openIndex = token.IndexOf('(');
        var closeIndex = token.LastIndexOf(')');
        if (openIndex <= "rand:".Length || closeIndex != token.Length - 1)
        {
            throw new InvalidOperationException($"Invalid rand token '{token}'. Expected 'rand:<function>(...)'.");
        }

        var functionName = token["rand:".Length..openIndex].Trim().ToLowerInvariant();
        var rawArguments = token[(openIndex + 1)..closeIndex];
        var arguments = string.IsNullOrWhiteSpace(rawArguments)
            ? Array.Empty<RandomArgument>()
            : SplitFunctionArguments(rawArguments)
                .Select(argument => ResolveRandomArgument(argument, context, responseContext, allowJsonStage))
                .Where(argument => !argument.IsEmpty)
                .ToArray();

        return functionName switch
        {
            "number" => ResolvedTokenValue.FromString(GenerateRandomNumber(arguments)),
            "text" => ResolvedTokenValue.FromString(GenerateRandomText(arguments)),
            "guid" => ResolvedTokenValue.FromString(GenerateRandomGuid(arguments)),
            "ulid" => ResolvedTokenValue.FromString(GenerateRandomUlid(arguments)),
            "date" => ResolvedTokenValue.FromString(GenerateRandomDate(arguments)),
            "datetime" => ResolvedTokenValue.FromString(GenerateRandomDateTime(arguments)),
            "time" => ResolvedTokenValue.FromString(GenerateRandomTime(arguments)),
            _ => throw new InvalidOperationException(
                $"Unknown rand function '{functionName}'. Supported values: number, text, guid, ulid, date, datetime, time.")
        };
    }

    private RandomArgument ResolveRandomArgument(
        string rawArgument,
        TemplateContext context,
        ResponseContext? responseContext,
        bool allowJsonStage)
    {
        var trimmed = rawArgument.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return RandomArgument.Empty;
        }

        if (IsQuotedLiteral(trimmed))
        {
            return new RandomArgument(Unquote(trimmed));
        }

        if (LooksLikeTemplateToken(trimmed))
        {
            var resolved = ResolveTokenValue(trimmed, context, responseContext, allowJsonStage);
            return new RandomArgument(resolved.StringValue);
        }

        return new RandomArgument(trimmed);
    }

    private string GenerateRandomNumber(IReadOnlyList<RandomArgument> arguments)
    {
        if (arguments.Count > 2)
        {
            throw new InvalidOperationException("rand:number expects 0, 1 or 2 arguments.");
        }

        var min = arguments.Count >= 1 && !arguments[0].IsEmpty
            ? ParseInt(arguments[0].Value!, "rand:number min")
            : (int?)null;
        var max = arguments.Count >= 2 && !arguments[1].IsEmpty
            ? ParseInt(arguments[1].Value!, "rand:number max")
            : (int?)null;

        return _randomValueService.Generate(
            new RandomValueDefinition(RandomValueType.Number, Min: min, Max: max),
            new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty),
            RandomValueFormattingOptions.Default);
    }

    private string GenerateRandomText(IReadOnlyList<RandomArgument> arguments)
    {
        if (arguments.Count > 2)
        {
            throw new InvalidOperationException("rand:text expects 0, 1 or 2 arguments.");
        }

        var length = arguments.Count >= 1 && !arguments[0].IsEmpty
            ? ParseInt(arguments[0].Value!, "rand:text length")
            : (int?)null;
        var characterSet = arguments.Count >= 2 && !arguments[1].IsEmpty
            ? arguments[1].Value
            : null;

        return _randomValueService.Generate(
            new RandomValueDefinition(RandomValueType.Text, Length: length, CharacterSet: characterSet),
            new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty),
            RandomValueFormattingOptions.Default);
    }

    private string GenerateRandomGuid(IReadOnlyList<RandomArgument> arguments)
    {
        if (arguments.Count != 0)
        {
            throw new InvalidOperationException("rand:guid expects no arguments.");
        }

        return _randomValueService.Generate(
            new RandomValueDefinition(RandomValueType.Guid),
            new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty),
            RandomValueFormattingOptions.Default);
    }

    private string GenerateRandomUlid(IReadOnlyList<RandomArgument> arguments)
    {
        if (arguments.Count != 0)
        {
            throw new InvalidOperationException("rand:ulid expects no arguments.");
        }

        return _randomValueService.Generate(
            new RandomValueDefinition(RandomValueType.Ulid),
            new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty),
            RandomValueFormattingOptions.Default);
    }

    private string GenerateRandomDate(IReadOnlyList<RandomArgument> arguments)
    {
        if (arguments.Count > 3)
        {
            throw new InvalidOperationException("rand:date expects up to 3 arguments: from, to, format.");
        }

        var from = arguments.Count >= 1 && !arguments[0].IsEmpty
            ? ParseDate(arguments[0].Value!, "rand:date from")
            : (DateOnly?)null;
        var to = arguments.Count >= 2 && !arguments[1].IsEmpty
            ? ParseDate(arguments[1].Value!, "rand:date to")
            : (DateOnly?)null;
        var format = arguments.Count >= 3 && !arguments[2].IsEmpty
            ? arguments[2].Value
            : null;

        return _randomValueService.Generate(
            new RandomValueDefinition(RandomValueType.Date, FromDate: from, ToDate: to, Format: format),
            new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty),
            RandomValueFormattingOptions.Default);
    }

    private string GenerateRandomDateTime(IReadOnlyList<RandomArgument> arguments)
    {
        if (arguments.Count > 3)
        {
            throw new InvalidOperationException("rand:datetime expects up to 3 arguments: from, to, format.");
        }

        var from = arguments.Count >= 1 && !arguments[0].IsEmpty
            ? ParseDateTimeOffset(arguments[0].Value!, "rand:datetime from")
            : (DateTimeOffset?)null;
        var to = arguments.Count >= 2 && !arguments[1].IsEmpty
            ? ParseDateTimeOffset(arguments[1].Value!, "rand:datetime to")
            : (DateTimeOffset?)null;
        var format = arguments.Count >= 3 && !arguments[2].IsEmpty
            ? arguments[2].Value
            : null;

        return _randomValueService.Generate(
            new RandomValueDefinition(RandomValueType.DateTime, FromDateTime: from, ToDateTime: to, Format: format),
            new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty),
            RandomValueFormattingOptions.Default);
    }

    private string GenerateRandomTime(IReadOnlyList<RandomArgument> arguments)
    {
        if (arguments.Count > 3)
        {
            throw new InvalidOperationException("rand:time expects up to 3 arguments: from, to, format.");
        }

        var from = arguments.Count >= 1 && !arguments[0].IsEmpty
            ? ParseTime(arguments[0].Value!, "rand:time from")
            : (TimeOnly?)null;
        var to = arguments.Count >= 2 && !arguments[1].IsEmpty
            ? ParseTime(arguments[1].Value!, "rand:time to")
            : (TimeOnly?)null;
        var format = arguments.Count >= 3 && !arguments[2].IsEmpty
            ? arguments[2].Value
            : null;

        return _randomValueService.Generate(
            new RandomValueDefinition(RandomValueType.Time, FromTime: from, ToTime: to, Format: format),
            new PayloadProcessorContext(1, string.Empty, string.Empty, string.Empty, string.Empty),
            RandomValueFormattingOptions.Default);
    }

    private static bool LooksLikeTemplateToken(string value)
    {
        return value.StartsWith("input.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("global.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("context:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("context.", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("endpoint:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("workflow:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("stage:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("var:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("env:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("system:", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("coalesce(", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsQuotedLiteral(string value)
    {
        return value.Length >= 2 &&
               ((value[0] == '\'' && value[^1] == '\'') ||
                (value[0] == '"' && value[^1] == '"'));
    }

    private static string Unquote(string value)
    {
        return value[1..^1]
            .Replace("\\'", "'")
            .Replace("\\\"", "\"");
    }

    private static int ParseInt(string value, string label)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw new InvalidOperationException($"{label} '{value}' is not a valid integer.");
        }

        return parsed;
    }

    private static DateOnly ParseDate(string value, string label)
    {
        if (!DateOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new InvalidOperationException($"{label} '{value}' is not a valid date.");
        }

        return parsed;
    }

    private static DateTimeOffset ParseDateTimeOffset(string value, string label)
    {
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
        {
            throw new InvalidOperationException($"{label} '{value}' is not a valid datetime.");
        }

        return parsed;
    }

    private static TimeOnly ParseTime(string value, string label)
    {
        if (!TimeOnly.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new InvalidOperationException($"{label} '{value}' is not a valid time.");
        }

        return parsed;
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
    string? WorkflowPath = null,
    IReadOnlySet<string>? SkippedStages = null,
    IReadOnlyDictionary<string, string>? WorkflowVars = null);

public readonly record struct ResolvedTokenValue(bool Exists, string? StringValue, JsonElement? JsonValue)
{
    public static ResolvedTokenValue NotFound { get; } = new(false, null, null);

    public static ResolvedTokenValue FromString(string? value)
        => value is null ? NotFound : new(true, value, null);

    public static ResolvedTokenValue FromJson(JsonElement value)
        => new(true, JsonValueHelper.ToDisplayString(value), value.Clone());
}

public readonly record struct RandomArgument(string? Value)
{
    public static RandomArgument Empty { get; } = new(null);

    public bool IsEmpty => string.IsNullOrWhiteSpace(Value);
}
