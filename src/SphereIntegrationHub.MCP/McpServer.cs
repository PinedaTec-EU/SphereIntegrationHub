using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Integration;
using SphereIntegrationHub.MCP.Tools;
using System.Text;
using System.Text.Json;

namespace SphereIntegrationHub.MCP;

/// <summary>
/// Main MCP server that handles JSON-RPC requests over stdio
/// </summary>
public sealed class McpServer
{
    private readonly SihServicesAdapter _servicesAdapter;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Dictionary<string, IMcpTool> _tools;

    public McpServer(SihServicesAdapter servicesAdapter, JsonSerializerOptions jsonOptions)
    {
        _servicesAdapter = servicesAdapter ?? throw new ArgumentNullException(nameof(servicesAdapter));
        _jsonOptions = jsonOptions ?? throw new ArgumentNullException(nameof(jsonOptions));
        _tools = new Dictionary<string, IMcpTool>(StringComparer.OrdinalIgnoreCase);

        RegisterTools();
    }

    /// <summary>
    /// Registers all MCP tools organized by levels
    /// </summary>
    private void RegisterTools()
    {
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
        RegisterTool(new GenerateStartupBootstrapTool());
        RegisterTool(new GenerateApiCatalogFileTool(_servicesAdapter));

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

        Console.Error.WriteLine($"[McpServer] Registered {_tools.Count} tools (L1: 22, L2: 5, L3: 1, L4: 2)");
    }

    private void RegisterTool(IMcpTool tool)
    {
        _tools[tool.Name] = tool;
        Console.Error.WriteLine($"[McpServer] Registered tool: {tool.Name}");
    }

    /// <summary>
    /// Starts the MCP server and processes requests from stdin
    /// </summary>
    public async Task StartAsync(Stream inputStream, Stream outputStream)
    {
        Console.Error.WriteLine("[McpServer] Server started, waiting for requests...");

        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        using var reader = new StreamReader(inputStream, utf8NoBom);
        using var writer = new StreamWriter(outputStream, utf8NoBom) { AutoFlush = true };

        while (!reader.EndOfStream)
        {
            try
            {
                var line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Console.Error.WriteLine($"[McpServer] Received request: {line[..Math.Min(line.Length, 200)]}...");

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

                var response = await ProcessRequestAsync(request);
                var responseJson = JsonSerializer.Serialize(response, _jsonOptions);
                await writer.WriteLineAsync(responseJson);

                Console.Error.WriteLine($"[McpServer] Sent response for request ID: {request.Id}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[McpServer] Unexpected error: {ex.Message}");
                Console.Error.WriteLine($"[McpServer] Stack trace: {ex.StackTrace}");
                await SendErrorAsync(writer, null, McpErrorCodes.InternalError, "Internal error", ex.Message);
            }
        }

        Console.Error.WriteLine("[McpServer] Server stopped");
    }

    public async Task<McpResponse> ProcessRequestAsync(McpRequest request)
    {
        try
        {
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
                            version = "0.1.0"
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
                        tools = _tools.Values.Select(t => new
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

                if (!_tools.TryGetValue(toolName, out var tool))
                {
                    return CreateErrorResponse(request.Id, McpErrorCodes.MethodNotFound, $"Tool not found: {toolName}", null);
                }

                // Extract arguments from params
                Dictionary<string, object>? arguments = null;
                if (request.Params?.TryGetValue("arguments", out var argsObj) == true)
                {
                    if (argsObj is JsonElement jsonElement)
                    {
                        var argsJson = jsonElement.GetRawText();
                        arguments = JsonSerializer.Deserialize<Dictionary<string, object>>(argsJson, _jsonOptions);
                    }
                    else if (argsObj is Dictionary<string, object> dict)
                    {
                        arguments = dict;
                    }
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
}
