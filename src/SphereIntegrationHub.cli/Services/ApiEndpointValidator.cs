using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Services.Interfaces;

namespace SphereIntegrationHub.Services;

public sealed class ApiEndpointValidator
{
    private readonly IExecutionLogger _logger;

    public ApiEndpointValidator(IExecutionLogger? logger = null)
    {
        _logger = logger ?? new ConsoleExecutionLogger();
    }

    public IReadOnlyList<string> Validate(
        WorkflowDefinition workflow,
        ApiCatalogVersion catalogVersion,
        string cacheRoot,
        bool validateRequiredParameters,
        bool verbose)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityEndpointValidate);
        activity?.SetTag(TelemetryConstants.TagCatalogVersion, catalogVersion.Version);
        var errors = new List<string>();

        if (workflow.References?.Apis is null || workflow.References.Apis.Count == 0)
        {
            return errors;
        }

        var apiLookup = workflow.References.Apis.ToDictionary(item => item.Name, item => item.Definition, StringComparer.OrdinalIgnoreCase);

        var swaggerCache = LoadSwaggerOperations(catalogVersion, apiLookup, cacheRoot, errors);
        if (workflow.Stages is null)
        {
            return errors;
        }

        foreach (var stage in workflow.Stages.Where(stage => stage.Kind == WorkflowStageKind.Endpoint))
        {
            if (string.IsNullOrWhiteSpace(stage.ApiRef) || string.IsNullOrWhiteSpace(stage.Endpoint) || string.IsNullOrWhiteSpace(stage.HttpVerb))
            {
                continue;
            }

            if (!apiLookup.TryGetValue(stage.ApiRef, out var definitionName))
            {
                errors.Add($"Stage '{stage.Name}' apiRef '{stage.ApiRef}' is not declared.");
                continue;
            }

            if (!swaggerCache.TryGetValue(definitionName, out var paths))
            {
                errors.Add($"Swagger cache for API definition '{definitionName}' was not found.");
                continue;
            }

            string? matchedPath = null;
            if (!paths.TryGetValue(stage.Endpoint, out var methods))
            {
                if (!TryFindByNormalizedPath(paths, stage.Endpoint, out methods, out matchedPath))
                {
                    errors.Add($"Stage '{stage.Name}' endpoint '{stage.Endpoint}' was not found in swagger '{definitionName}'.");
                    continue;
                }

                if (verbose)
                {
                    _logger.Info($"Endpoint '{stage.Endpoint}' matched swagger path by normalization for '{definitionName}'.");
                }
            }
            else
            {
                matchedPath = stage.Endpoint;
            }

            var verb = stage.HttpVerb.Trim().ToLowerInvariant();
            if (!methods.TryGetValue(verb, out var operation))
            {
                errors.Add($"Stage '{stage.Name}' verb '{stage.HttpVerb}' was not found for endpoint '{stage.Endpoint}' in swagger '{definitionName}'.");
                continue;
            }

            if (verbose)
            {
                _logger.Info($"Validated endpoint stage '{stage.Name}': {stage.HttpVerb} {stage.Endpoint} ({definitionName}).");
            }

            if (validateRequiredParameters)
            {
                ValidateRequiredParameters(stage, operation, matchedPath ?? stage.Endpoint, errors);
            }
        }

        return errors;
    }

    private static void ValidateRequiredParameters(
        WorkflowStageDefinition stage,
        SwaggerOperation operation,
        string swaggerPath,
        List<string> errors)
    {
        if (operation.Parameters.Count == 0)
        {
            return;
        }

        var headerNames = stage.Headers is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(stage.Headers.Keys, StringComparer.OrdinalIgnoreCase);

        var queryNames = stage.Query is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(stage.Query.Keys, StringComparer.OrdinalIgnoreCase);

        var placeholderCount = CountPathPlaceholders(stage.Endpoint);
        foreach (var parameter in operation.Parameters)
        {
            if (!parameter.Required)
            {
                continue;
            }

            switch (parameter.Location)
            {
                case "query":
                    if (!queryNames.Contains(parameter.Name))
                    {
                        errors.Add($"Stage '{stage.Name}' is missing required query parameter '{parameter.Name}'.");
                    }
                    break;
                case "header":
                    if (!headerNames.Contains(parameter.Name))
                    {
                        errors.Add($"Stage '{stage.Name}' is missing required header '{parameter.Name}'.");
                    }
                    break;
                case "path":
                    if (placeholderCount <= 0)
                    {
                        errors.Add(
                            $"Stage '{stage.Name}' is missing required path parameter '{parameter.Name}'. " +
                            $"Swagger path: '{swaggerPath}'. Workflow endpoint: '{stage.Endpoint}'.");
                    }
                    else
                    {
                        placeholderCount--;
                    }
                    break;
                case "body":
                case "formdata":
                    if (string.IsNullOrWhiteSpace(stage.Body))
                    {
                        errors.Add($"Stage '{stage.Name}' is missing required body parameter '{parameter.Name}'.");
                    }
                    break;
            }
        }
    }

    private static Dictionary<string, Dictionary<string, Dictionary<string, SwaggerOperation>>> LoadSwaggerOperations(
        ApiCatalogVersion catalogVersion,
        Dictionary<string, string> apiLookup,
        string cacheRoot,
        List<string> errors)
    {
        var result = new Dictionary<string, Dictionary<string, Dictionary<string, SwaggerOperation>>>(StringComparer.OrdinalIgnoreCase);

        foreach (var definitionName in apiLookup.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var definition = catalogVersion.Definitions.FirstOrDefault(def =>
                string.Equals(def.Name, definitionName, StringComparison.OrdinalIgnoreCase));

            if (definition is null)
            {
                errors.Add($"API definition '{definitionName}' was not found in catalog version '{catalogVersion.Version}'.");
                continue;
            }

            var swaggerPath = Path.Combine(cacheRoot, $"{definition.Name}.json");
            if (!File.Exists(swaggerPath))
            {
                errors.Add($"Swagger cache file was not found: {swaggerPath}");
                continue;
            }

            try
            {
                var json = File.ReadAllText(swaggerPath);
                using var document = JsonDocument.Parse(json);
                if (!document.RootElement.TryGetProperty("paths", out var pathsElement) ||
                    pathsElement.ValueKind != JsonValueKind.Object)
                {
                    errors.Add($"Swagger cache '{definition.Name}' is missing 'paths'.");
                    continue;
                }

                var pathMap = new Dictionary<string, Dictionary<string, SwaggerOperation>>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in pathsElement.EnumerateObject())
                {
                    if (path.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var pathParameters = ParseParameters(path.Value, document, errors);
                    var verbMap = new Dictionary<string, SwaggerOperation>(StringComparer.OrdinalIgnoreCase);

                    foreach (var verb in path.Value.EnumerateObject())
                    {
                        if (!IsHttpVerb(verb.Name) || verb.Value.ValueKind != JsonValueKind.Object)
                        {
                            continue;
                        }

                        var parameters = new List<SwaggerParameter>();
                        parameters.AddRange(pathParameters);
                        parameters.AddRange(ParseParameters(verb.Value, document, errors));
                        parameters = parameters
                            .GroupBy(param => $"{param.Location}:{param.Name}", StringComparer.OrdinalIgnoreCase)
                            .Select(group => group.Last())
                            .ToList();

                        verbMap[verb.Name.ToLowerInvariant()] = new SwaggerOperation(parameters);
                    }

                    pathMap[path.Name] = verbMap;
                }

                result[definition.Name] = pathMap;
            }
            catch (Exception ex)
            {
                errors.Add($"Failed to parse swagger cache '{definition.Name}': {ex.Message}");
            }
        }

        return result;
    }

    private static List<SwaggerParameter> ParseParameters(JsonElement parent, JsonDocument document, List<string> errors)
    {
        if (!parent.TryGetProperty("parameters", out var parametersElement) ||
            parametersElement.ValueKind != JsonValueKind.Array)
        {
            return new List<SwaggerParameter>();
        }

        var parameters = new List<SwaggerParameter>();
        foreach (var parameterElement in parametersElement.EnumerateArray())
        {
            if (parameterElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var resolved = parameterElement;
            if (parameterElement.TryGetProperty("$ref", out var referenceElement) &&
                referenceElement.ValueKind == JsonValueKind.String &&
                TryResolveRef(document, referenceElement.GetString(), out var resolvedElement))
            {
                resolved = resolvedElement;
            }

            if (!resolved.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (!resolved.TryGetProperty("in", out var inElement) || inElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var required = resolved.TryGetProperty("required", out var requiredElement) &&
                           requiredElement.ValueKind == JsonValueKind.True;

            parameters.Add(new SwaggerParameter(
                nameElement.GetString() ?? string.Empty,
                inElement.GetString() ?? string.Empty,
                required));
        }

        return parameters;
    }

    private static bool TryResolveRef(JsonDocument document, string? reference, out JsonElement element)
    {
        element = default;
        if (string.IsNullOrWhiteSpace(reference) || !reference.StartsWith("#/", StringComparison.Ordinal))
        {
            return false;
        }

        var path = reference[2..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = document.RootElement;

        foreach (var segment in path)
        {
            if (!current.TryGetProperty(segment, out var next))
            {
                return false;
            }

            current = next;
        }

        element = current;
        return true;
    }

    private static bool IsHttpVerb(string name)
    {
        return name.Equals("get", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("post", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("put", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("patch", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("delete", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("options", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("head", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryFindByNormalizedPath(
        Dictionary<string, Dictionary<string, SwaggerOperation>> paths,
        string endpoint,
        out Dictionary<string, SwaggerOperation> methods,
        out string? matchedPath)
    {
        var normalizedEndpoint = NormalizePath(endpoint);
        foreach (var path in paths)
        {
            if (string.Equals(NormalizePath(path.Key), normalizedEndpoint, StringComparison.OrdinalIgnoreCase))
            {
                methods = path.Value;
                matchedPath = path.Key;
                return true;
            }
        }

        methods = new Dictionary<string, SwaggerOperation>(StringComparer.OrdinalIgnoreCase);
        matchedPath = null;
        return false;
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];
            if (segment.StartsWith("{", StringComparison.Ordinal) && segment.EndsWith("}", StringComparison.Ordinal))
            {
                segments[i] = "{}";
            }
            else if (segment.Contains("{{", StringComparison.Ordinal))
            {
                segments[i] = "{}";
            }
        }

        return "/" + string.Join('/', segments);
    }

    private static int CountPathPlaceholders(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return 0;
        }

        var count = 0;
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            if ((segment.StartsWith("{", StringComparison.Ordinal) && segment.EndsWith("}", StringComparison.Ordinal)) ||
                segment.Contains("{{", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    private sealed record SwaggerOperation(List<SwaggerParameter> Parameters);

    private sealed record SwaggerParameter(string Name, string Location, bool Required);
}
