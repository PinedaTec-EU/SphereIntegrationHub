using System.Collections.Concurrent;
using System.Text.Json;

using SphereIntegrationHub.Definitions;
using SphereIntegrationHub.Plugins;
using SphereIntegrationHub.Services.Interfaces;

// path → (verb → operation)
using SwaggerVerbMap = System.Collections.Generic.Dictionary<string, SphereIntegrationHub.Services.SwaggerOperation>;
// apiName → SwaggerVerbMap per path
using SwaggerPathMap = System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, SphereIntegrationHub.Services.SwaggerOperation>>;

namespace SphereIntegrationHub.Services;

public sealed class ApiEndpointValidator
{
    private static readonly ConcurrentDictionary<string, (DateTime LastWrite, SwaggerPathMap Paths)> _swaggerOperationsCache = new();

    // Gauge registered once per process; reads the live count from the static dictionary.
    private static readonly System.Diagnostics.Metrics.ObservableGauge<int> _swaggerCacheSizeGauge =
        Telemetry.Meter.CreateObservableGauge(
            "sih.cache.swagger.operations.size",
            () => _swaggerOperationsCache.Count,
            "{entries}",
            "Current number of Swagger API definitions held in the in-memory parse cache.");

    private readonly IExecutionLogger _logger;
    private readonly StagePluginRegistry _stagePluginRegistry;

    public ApiEndpointValidator(IExecutionLogger? logger = null, StagePluginRegistry? stagePluginRegistry = null)
    {
        _logger = logger ?? new ConsoleExecutionLogger();
        _stagePluginRegistry = stagePluginRegistry ?? new StagePluginRegistryBuilder().CreateBuiltInRegistry();
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

        foreach (var stage in workflow.Stages.Where(stage => !WorkflowStageKind.IsWorkflow(stage.Kind)))
        {
            if (!_stagePluginRegistry.TryGetByKind(stage.Kind, out _))
            {
                errors.Add($"Stage '{stage.Name}' kind '{stage.Kind}' is not registered by any active plugin.");
                continue;
            }

            var apiRef = stage.GetConfigString("apiRef") ?? stage.ApiRef;
            var endpoint = stage.GetConfigString("endpoint") ?? stage.Endpoint;
            var httpVerb = stage.GetConfigString("httpVerb") ?? stage.HttpVerb;
            if (string.IsNullOrWhiteSpace(apiRef) || string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(httpVerb))
            {
                continue;
            }

            if (!apiLookup.TryGetValue(apiRef, out var definitionName))
            {
                errors.Add($"Stage '{stage.Name}' apiRef '{apiRef}' is not declared.");
                continue;
            }

            var definition = catalogVersion.Definitions.FirstOrDefault(def =>
                string.Equals(def.Name, definitionName, StringComparison.OrdinalIgnoreCase));
            if (definition is null)
            {
                errors.Add($"API definition '{definitionName}' was not found in catalog version '{catalogVersion.Version}'.");
                continue;
            }

            if (!swaggerCache.TryGetValue(definitionName, out var paths))
            {
                errors.Add($"Swagger cache for API definition '{definitionName}' was not found.");
                continue;
            }

            string? matchedPath = null;
            if (!TryFindEndpointMatch(paths, endpoint, definition.BasePath, out var methods, out matchedPath))
            {
                errors.Add($"Stage '{stage.Name}' endpoint '{endpoint}' was not found in swagger '{definitionName}'.");
                continue;
            }

            var verb = httpVerb.Trim().ToLowerInvariant();
            if (!methods.TryGetValue(verb, out var operation))
            {
                errors.Add($"Stage '{stage.Name}' verb '{httpVerb}' was not found for endpoint '{endpoint}' in swagger '{definitionName}'.");
                continue;
            }

            if (verbose)
            {
                _logger.Info($"Validated endpoint stage '{stage.Name}': {httpVerb} {endpoint} ({definitionName}).");
            }

            if (validateRequiredParameters)
            {
                ValidateRequiredParameters(stage, operation, matchedPath ?? endpoint, errors);
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

        var pathPlaceholderNames = ExtractPathPlaceholderNames(stage.Endpoint);
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
                    if (!pathPlaceholderNames.Contains(parameter.Name))
                    {
                        errors.Add(
                            $"Stage '{stage.Name}' is missing required path parameter '{parameter.Name}'. " +
                            $"Swagger path: '{swaggerPath}'. Workflow endpoint: '{stage.Endpoint}'.");
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

    private static Dictionary<string, SwaggerPathMap> LoadSwaggerOperations(
        ApiCatalogVersion catalogVersion,
        Dictionary<string, string> apiLookup,
        string cacheRoot,
        List<string> errors)
    {
        var result = new Dictionary<string, SwaggerPathMap>(StringComparer.OrdinalIgnoreCase);

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

            var lastWrite = File.GetLastWriteTimeUtc(swaggerPath);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            if (_swaggerOperationsCache.TryGetValue(swaggerPath, out var cached) && cached.LastWrite == lastWrite)
            {
                sw.Stop();
                Telemetry.SwaggerCacheHits.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.TagApiDefinition, definition.Name));
                Telemetry.SwaggerLoadDuration.Record(
                    sw.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>(TelemetryConstants.TagApiDefinition, definition.Name),
                    new KeyValuePair<string, object?>(TelemetryConstants.TagCacheHit, true));
                result[definition.Name] = cached.Paths;
                continue;
            }

            Telemetry.SwaggerCacheMisses.Add(1, new KeyValuePair<string, object?>(TelemetryConstants.TagApiDefinition, definition.Name));
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

                sw.Stop();
                Telemetry.SwaggerLoadDuration.Record(
                    sw.Elapsed.TotalMilliseconds,
                    new KeyValuePair<string, object?>(TelemetryConstants.TagApiDefinition, definition.Name),
                    new KeyValuePair<string, object?>(TelemetryConstants.TagCacheHit, false));

                _swaggerOperationsCache[swaggerPath] = (lastWrite, pathMap);
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
        SwaggerPathMap paths,
        string endpoint,
        out SwaggerVerbMap methods,
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

        methods = new SwaggerVerbMap(StringComparer.OrdinalIgnoreCase);
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

    private static bool TryFindEndpointMatch(
        SwaggerPathMap paths,
        string endpoint,
        string? basePath,
        out SwaggerVerbMap methods,
        out string? matchedPath)
    {
        if (paths.TryGetValue(endpoint, out var directMethods) && directMethods is not null)
        {
            methods = directMethods;
            matchedPath = endpoint;
            return true;
        }

        if (TryFindByNormalizedPath(paths, endpoint, out methods, out matchedPath))
        {
            return true;
        }

        var endpointWithBasePath = CombinePath(basePath, endpoint);
        if (string.Equals(endpointWithBasePath, endpoint, StringComparison.OrdinalIgnoreCase))
        {
            methods = new SwaggerVerbMap(StringComparer.OrdinalIgnoreCase);
            matchedPath = null;
            return false;
        }

        if (paths.TryGetValue(endpointWithBasePath, out var basePathMethods) && basePathMethods is not null)
        {
            methods = basePathMethods;
            matchedPath = endpointWithBasePath;
            return true;
        }

        return TryFindByNormalizedPath(paths, endpointWithBasePath, out methods, out matchedPath);
    }

    private static string CombinePath(string? basePath, string endpoint)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return endpoint;
        }

        var normalizedBasePath = NormalizePath(basePath);
        var normalizedEndpoint = NormalizePath(endpoint);
        if (normalizedEndpoint.Equals(normalizedBasePath, StringComparison.OrdinalIgnoreCase) ||
            normalizedEndpoint.StartsWith($"{normalizedBasePath}/", StringComparison.OrdinalIgnoreCase))
        {
            return endpoint;
        }

        return $"{normalizedBasePath}{normalizedEndpoint}";
    }

    private static HashSet<string> ExtractPathPlaceholderNames(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var segment in segments)
        {
            // Template-style: {{inputs.paramName}} or {{steps.x.output.paramName}}
            if (segment.StartsWith("{{", StringComparison.Ordinal) && segment.EndsWith("}}", StringComparison.Ordinal))
            {
                var inner = segment[2..^2].Trim();
                var lastDot = inner.LastIndexOf('.');
                var name = lastDot >= 0 ? inner[(lastDot + 1)..].Trim() : inner;
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
            // Swagger-style: {paramName}
            else if (segment.StartsWith("{", StringComparison.Ordinal) && segment.EndsWith("}", StringComparison.Ordinal))
            {
                var name = segment[1..^1].Trim();
                if (!string.IsNullOrEmpty(name))
                {
                    names.Add(name);
                }
            }
        }

        return names;
    }

}

internal sealed record SwaggerOperation(List<SwaggerParameter> Parameters);

internal sealed record SwaggerParameter(string Name, string Location, bool Required);
