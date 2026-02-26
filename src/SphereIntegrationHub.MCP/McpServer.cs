using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tools;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace SphereIntegrationHub.MCP;

/// <summary>
/// Main MCP server that handles JSON-RPC requests over stdio
/// </summary>
public sealed class McpServer
{
    private const int MaxRequestCharacters = 262_144; // 256 KiB JSON line cap
    private const string JsonRpcVersion = "2.0";
    private readonly SihServicesAdapter _servicesAdapter;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly McpToolRegistry _registry;
    private readonly string _serverVersion;
    private readonly string _toolProfile;
    private readonly bool _verboseLogs;

    public McpServer(SihServicesAdapter servicesAdapter, JsonSerializerOptions jsonOptions)
    {
        _servicesAdapter = servicesAdapter ?? throw new ArgumentNullException(nameof(servicesAdapter));
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        _registry = new McpToolRegistry();
        _serverVersion = ResolveServerVersion();
        _toolProfile = ResolveToolProfile();
        _verboseLogs = ResolveVerboseLogsFlag();

        RegisterTools();
    }

    /// <summary>
    /// Registers all MCP tools organized by levels
    /// </summary>
    private void RegisterTools()
    {
        if (_toolProfile.Equals("cache", StringComparison.OrdinalIgnoreCase))
        {
            RegisterCacheProfileTools();
            Console.Error.WriteLine($"[McpServer] Registered {_registry.Count} tools (profile: {_toolProfile})");
            return;
        }

        // Level 1 Tools (18 tools)

        // Catalog Tools (4 tools)
        RegisterTool(new ListApiCatalogVersionsTool(_servicesAdapter));
        RegisterTool(new GetApiDefinitionsTool(_servicesAdapter));
        RegisterTool(new GetApiEndpointsTool(_servicesAdapter));
        RegisterTool(new GetEndpointSchemaTool(_servicesAdapter));

        // Validation Tools (3 tools)
        RegisterTool(new ValidateWorkflowTool(_servicesAdapter));
        RegisterTool(new ValidateStageTool(_servicesAdapter));
        RegisterTool(new PlanWorkflowExecutionTool(_servicesAdapter));

        // Generation Tools (7 tools)
        RegisterTool(new GenerateEndpointStageTool(_servicesAdapter));
        RegisterTool(new GenerateWorkflowSkeletonTool(_servicesAdapter));
        RegisterTool(new GenerateMockPayloadTool(_servicesAdapter));
        RegisterTool(new GenerateWorkflowBundleTool(_servicesAdapter));
        RegisterTool(new WriteWorkflowArtifactsTool(_servicesAdapter));
        RegisterTool(new GenerateWfvarsFromWorkflowTool(_servicesAdapter));
        RegisterTool(new RepairWorkflowArtifactsTool(_servicesAdapter));
        RegisterTool(new GenerateStartupBootstrapTool());
        RegisterTool(new GenerateApiCatalogFileTool(_servicesAdapter));
        RegisterTool(new UpsertApiCatalogAndCacheTool(_servicesAdapter));
        RegisterTool(new RefreshSwaggerCacheFromCatalogTool(_servicesAdapter));
        RegisterTool(new QuickRefreshSwaggerCacheTool(_servicesAdapter));

        // Analysis Tools (3 tools)
        RegisterTool(new GetAvailableVariablesTool(_servicesAdapter));
        RegisterTool(new ResolveTemplateTokenTool(_servicesAdapter));
        RegisterTool(new AnalyzeContextFlowTool(_servicesAdapter));

        // Reference Tools (2 tools)
        RegisterTool(new ListAvailableWorkflowsTool(_servicesAdapter));
        RegisterTool(new GetWorkflowInputsOutputsTool(_servicesAdapter));

        // Diagnostic Tools (3 tools)
        RegisterTool(new ExplainValidationErrorTool(_servicesAdapter));
        RegisterTool(new GetPluginCapabilitiesTool(_servicesAdapter));
        RegisterTool(new SuggestResilienceConfigTool(_servicesAdapter));

        // Level 2 Tools (5 tools)

        // Semantic Analysis Tools (3 tools)
        RegisterTool(new AnalyzeEndpointDependenciesTool(_servicesAdapter));
        RegisterTool(new InferDataFlowTool(_servicesAdapter));
        RegisterTool(new SuggestWorkflowFromGoalTool(_servicesAdapter));

        // Pattern Detection Tools (2 tools)
        RegisterTool(new DetectApiPatternsTool(_servicesAdapter));
        RegisterTool(new GenerateCrudWorkflowTool(_servicesAdapter));

        // Level 3 Tools (1 tool)

        // Synthesis Tool
        RegisterTool(new SynthesizeSystemFromDescriptionTool(_servicesAdapter));

        // Level 4 Tools (2 tools)

        // Optimization Tools
        RegisterTool(new SuggestOptimizationsTool(_servicesAdapter));
        RegisterTool(new AnalyzeSwaggerCoverageTool(_servicesAdapter));

        Console.Error.WriteLine($"[McpServer] Registered {_registry.Count} tools (profile: {_toolProfile})");
    }

    private void RegisterCacheProfileTools()
    {
        // Minimal toolset for fast cache operations with low token overhead.
        RegisterTool(new ListApiCatalogVersionsTool(_servicesAdapter));
        RegisterTool(new GenerateApiCatalogFileTool(_servicesAdapter));
        RegisterTool(new UpsertApiCatalogAndCacheTool(_servicesAdapter));
        RegisterTool(new RefreshSwaggerCacheFromCatalogTool(_servicesAdapter));
        RegisterTool(new QuickRefreshSwaggerCacheTool(_servicesAdapter));
    }

    private void RegisterTool(IMcpTool tool)
    {
        _registry.Register(tool);
        if (_verboseLogs)
        {
            Console.Error.WriteLine($"[McpServer] Registered tool: {tool.Name}");
        }
    }

    /// <summary>
    /// Starts the MCP server and processes requests from stdin
    /// </summary>
    public async Task StartAsync(Stream inputStream, Stream outputStream)
    {
        Console.Error.WriteLine("[McpServer] Server started, waiting for requests...");

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(inputStream, utf8NoBom, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        using var writer = new StreamWriter(outputStream, utf8NoBom, bufferSize: 1024, leaveOpen: true) { AutoFlush = true };

        while (!reader.EndOfStream)
        {
            try
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Length > MaxRequestCharacters)
                {
                    Console.Error.WriteLine($"[McpServer] Request rejected (too large: {line.Length} chars)");
                    await SendErrorAsync(writer, null, McpErrorCodes.InvalidRequest, "Request too large", null);
                    continue;
                }

                McpRequest? request;
                try
                {
                    request = JsonSerializer.Deserialize<McpRequest>(line, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"[McpServer] JSON parse error: {ex.Message}");
                    await SendErrorAsync(writer, null, McpErrorCodes.ParseError, "Parse error", ex.Message);
                    continue;
                }

                if (request == null)
                {
                    await SendErrorAsync(writer, null, McpErrorCodes.InvalidRequest, "Invalid request", null);
                    continue;
                }

                var isNotification = request.Id == null;
                Console.Error.WriteLine($"[McpServer] Received request method='{request.Method}' id='{request.Id?.ToString() ?? "notification"}'");

                var response = await ProcessRequestAsync(request);
                if (!isNotification)
                {
                    var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                    await writer.WriteLineAsync(responseJson);

                    Console.Error.WriteLine($"[McpServer] Sent response for request ID: {request.Id}");
                }
                else
                {
                    Console.Error.WriteLine($"[McpServer] Processed notification method='{request.Method}'");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[McpServer] Unexpected error: {ex.Message}");
                if (_verboseLogs)
                {
                    Console.Error.WriteLine($"[McpServer] Stack trace: {ex.StackTrace}");
                }

                await SendErrorAsync(writer, null, McpErrorCodes.InternalError, "Internal error", null);
            }
        }

        Console.Error.WriteLine("[McpServer] Server stopped");
    }

    public async Task<McpResponse> ProcessRequestAsync(McpRequest request)
    {
        try
        {
            if (!string.Equals(request.JsonRpc, JsonRpcVersion, StringComparison.Ordinal))
            {
                return CreateErrorResponse(request.Id, McpErrorCodes.InvalidRequest, "Invalid JSON-RPC version", null);
            }

            if (string.IsNullOrWhiteSpace(request.Method))
            {
                return CreateErrorResponse(request.Id, McpErrorCodes.InvalidRequest, "Method is required", null);
            }

            // Handle standard JSON-RPC methods
            if (request.Method == "initialize")
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        protocolVersion = "2024-11-05",
                        capabilities = new
                        {
                            tools = new { }
                        },
                        serverInfo = new
                        {
                            name = "SphereIntegrationHub.MCP",
                            version = _serverVersion
                        }
                    }
                };
            }

            if (request.Method == "tools/list")
            {
                return new McpResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        tools = _registry.GetAll().Select(t => new
                        {
                            name = t.Name,
                            description = t.Description,
                            inputSchema = t.InputSchema
                        }).ToList()
                    }
                };
            }

            if (request.Method == "tools/call")
            {
                var toolName = request.Params?.GetValueOrDefault("name")?.ToString();
                if (string.IsNullOrEmpty(toolName))
                {
                    return CreateErrorResponse(request.Id, McpErrorCodes.InvalidParams, "Tool name is required", null);
                }

                if (!_registry.TryGet(toolName, out var tool))
                {
                    return CreateErrorResponse(request.Id, McpErrorCodes.MethodNotFound, $"Tool not found: {toolName}", null);
                }

                // Extract arguments from params
                Dictionary<string, object>? arguments = null;
                if (request.Params?.TryGetValue("arguments", out var argsObj) == true &&
                    !TryExtractArguments(argsObj, out arguments, out var argumentError))
                {
                    return CreateErrorResponse(request.Id, McpErrorCodes.InvalidParams, argumentError!, null);
                }

                Console.Error.WriteLine($"[McpServer] Executing tool: {toolName}");
                var result = await tool.ExecuteAsync(arguments);

                return new McpResponse
                {
                    Id = request.Id,
                    Result = new
                    {
                        content = new[]
                        {
                            new
                            {
                                type = "text",
                                text = JsonSerializer.Serialize(result, _jsonOptions)
                            }
                        }
                    }
                };
            }

            return CreateErrorResponse(request.Id, McpErrorCodes.MethodNotFound, $"Method not found: {request.Method}", null);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[McpServer] Error processing request: {ex.Message}");
            Console.Error.WriteLine($"[McpServer] Stack trace: {ex.StackTrace}");
            if (ex is ArgumentException ||
                ex is FileNotFoundException ||
                ex is DirectoryNotFoundException ||
                ex is InvalidOperationException)
            {
                return CreateErrorResponse(request.Id, McpErrorCodes.InvalidParams, ex.Message, null);
            }

            return CreateErrorResponse(request.Id, McpErrorCodes.InternalError, "Internal error", ex.Message);
        }
    }

    private static McpResponse CreateErrorResponse(object? id, int code, string message, string? data)
    {
        return new McpResponse
        {
            Id = id,
            Error = new McpError
            {
                Code = code,
                Message = message,
                Data = data
            }
        };
    }

    private static async Task SendErrorAsync(StreamWriter writer, object? id, int code, string message, string? data)
    {
        var response = CreateErrorResponse(id, code, message, data);
        var responseJson = JsonSerializer.Serialize(response);
        await writer.WriteLineAsync(responseJson);
    }

    private static string ResolveServerVersion()
    {
        var informational = Assembly
            .GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational;
        }

        var assemblyVersion = Assembly.GetExecutingAssembly().GetName().Version?.ToString();
        if (!string.IsNullOrWhiteSpace(assemblyVersion))
        {
            return assemblyVersion;
        }

        return "0.0.0";
    }

    private static string ResolveToolProfile()
    {
        var profile = Environment.GetEnvironmentVariable("SIH_MCP_PROFILE");
        if (string.IsNullOrWhiteSpace(profile))
        {
            return "full";
        }

        return profile.Trim();
    }

    private static bool ResolveVerboseLogsFlag()
    {
        var value = Environment.GetEnvironmentVariable("SIH_MCP_VERBOSE_LOGS");
        return value?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private bool TryExtractArguments(object? argsObj, out Dictionary<string, object>? arguments, out string? errorMessage)
    {
        arguments = null;
        errorMessage = null;

        if (argsObj == null)
        {
            return true;
        }

        if (argsObj is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.Null ||
                jsonElement.ValueKind == JsonValueKind.Undefined)
            {
                return true;
            }

            if (jsonElement.ValueKind != JsonValueKind.Object)
            {
                errorMessage = "Tool arguments must be a JSON object.";
                return false;
            }

            try
            {
                var argsJson = jsonElement.GetRawText();
                arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson, _jsonOptions);
                return true;
            }
            catch (JsonException)
            {
                errorMessage = "Tool arguments payload is not a valid JSON object.";
                return false;
            }
        }

        if (argsObj is Dictionary<string, object> dictionary)
        {
            arguments = dictionary;
            return true;
        }

        errorMessage = "Tool arguments must be an object map.";
        return false;
    }
}
