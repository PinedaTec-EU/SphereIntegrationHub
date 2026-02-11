using SphereIntegrationHub.MCP.Core;
using SphereIntegrationHub.MCP.Services.Analysis;
using SphereIntegrationHub.MCP.Services.Integration;

namespace SphereIntegrationHub.MCP.Tools;

/// <summary>
/// Gets available variables at a specific point in workflow
/// </summary>
[McpTool("get_available_variables", "Gets all available variables at a specific point in workflow execution", Category = "Analysis", Level = "L1")]
public sealed class GetAvailableVariablesTool : IMcpTool
{
    private readonly VariableScopeAnalyzer _analyzer;
    private readonly SihServicesAdapter _adapter;

    public GetAvailableVariablesTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
        _analyzer = new VariableScopeAnalyzer(adapter);
    }

    public string Name => "get_available_variables";
    public string Description => "Gets all available variables (inputs, globals, stage outputs, etc.) at a specific point in workflow execution";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new
            {
                type = "string",
                description = "Path to the workflow YAML file"
            },
            atStage = new
            {
                type = "string",
                description = "Optional stage name to get variables available at that stage"
            }
        },
        required = new[] { "workflowPath" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPath = arguments?.GetValueOrDefault("workflowPath")?.ToString()
            ?? throw new ArgumentException("workflowPath is required");
        var atStage = arguments?.GetValueOrDefault("atStage")?.ToString();

        // If path is not absolute, resolve relative to workflows directory
        if (!Path.IsPathRooted(workflowPath))
        {
            workflowPath = Path.Combine(_adapter.WorkflowsPath, workflowPath);
        }

        var scope = await _analyzer.GetAvailableVariablesAsync(workflowPath, atStage);

        // Return as camelCase anonymous object for test compatibility
        return new
        {
            inputs = scope.Inputs.Select(i => new { name = i.Name, type = i.Type, required = i.Required }).ToList(),
            globals = scope.Globals.Select(g => new { name = g.Name, type = g.Type, value = g.Value }).ToList(),
            context = scope.Context.Select(c => new { name = c.Name, source = c.Source }).ToList(),
            env = scope.Env.Select(e => new { name = e.Name, value = e.Value }).ToList(),
            system = scope.System.Select(s => new { token = s.Token, description = s.Description }).ToList(),
            stageOutputs = scope.StageOutputs.Select(so => new {
                stage = so.Stage,
                outputs = so.Outputs.Select(o => new { name = o.Name, type = o.Type }).ToList()
            }).ToList()
        };
    }
}

/// <summary>
/// Resolves a template token to its source
/// </summary>
[McpTool("resolve_template_token", "Resolves a template token to its source and type", Category = "Analysis", Level = "L1")]
public sealed class ResolveTemplateTokenTool : IMcpTool
{
    private readonly VariableScopeAnalyzer _analyzer;
    private readonly SihServicesAdapter _adapter;

    public ResolveTemplateTokenTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
        _analyzer = new VariableScopeAnalyzer(adapter);
    }

    public string Name => "resolve_template_token";
    public string Description => "Resolves a template token (e.g., {{ input.userId }}) to its source, type, and availability";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new
            {
                type = "string",
                description = "Path to the workflow YAML file"
            },
            token = new
            {
                type = "string",
                description = "Template token to resolve (e.g., 'input.userId' or '{{ input.userId }}')"
            }
        },
        required = new[] { "workflowPath", "token" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPath = arguments?.GetValueOrDefault("workflowPath")?.ToString()
            ?? throw new ArgumentException("workflowPath is required");
        var token = arguments?.GetValueOrDefault("token")?.ToString()
            ?? throw new ArgumentException("token is required");

        // If path is not absolute, resolve relative to workflows directory
        if (!Path.IsPathRooted(workflowPath))
        {
            workflowPath = Path.Combine(_adapter.WorkflowsPath, workflowPath);
        }

        var resolution = await _analyzer.ResolveTemplateTokenAsync(workflowPath, token);

        // Parse the token to extract scope and variable
        var cleanToken = token.Trim().TrimStart('{').TrimEnd('}').Trim();
        var parts = cleanToken.Split('.');
        var scope = parts.Length > 0 ? parts[0] : "unknown";
        var variable = parts.Length > 1 ? parts[1] : "";

        // Return as camelCase anonymous object for test compatibility
        return new
        {
            token = resolution.Token,
            scope = scope,
            variable = variable,
            isAvailable = resolution.Valid,
            source = resolution.Source,
            type = resolution.Type,
            required = resolution.Required,
            description = resolution.Description,
            message = resolution.Message
        };
    }
}

/// <summary>
/// Analyzes context flow through workflow stages
/// </summary>
[McpTool("analyze_context_flow", "Analyzes how context flows through workflow stages", Category = "Analysis", Level = "L1")]
public sealed class AnalyzeContextFlowTool : IMcpTool
{
    private readonly VariableScopeAnalyzer _analyzer;
    private readonly SihServicesAdapter _adapter;

    public AnalyzeContextFlowTool(SihServicesAdapter adapter)
    {
        _adapter = adapter;
        _analyzer = new VariableScopeAnalyzer(adapter);
    }

    public string Name => "analyze_context_flow";
    public string Description => "Analyzes how context flows through workflow stages, showing which stages read/write context";

    public object InputSchema => new
    {
        type = "object",
        properties = new
        {
            workflowPath = new
            {
                type = "string",
                description = "Path to the workflow YAML file"
            }
        },
        required = new[] { "workflowPath" }
    };

    public async Task<object> ExecuteAsync(Dictionary<string, object>? arguments)
    {
        var workflowPath = arguments?.GetValueOrDefault("workflowPath")?.ToString()
            ?? throw new ArgumentException("workflowPath is required");

        // If path is not absolute, resolve relative to workflows directory
        if (!Path.IsPathRooted(workflowPath))
        {
            workflowPath = Path.Combine(_adapter.WorkflowsPath, workflowPath);
        }

        var flow = await _analyzer.AnalyzeContextFlowAsync(workflowPath);

        // Separate stages by read/write operations
        var stagesReadingContext = flow.Stages
            .Where(s => s.ContextReads.Count > 0)
            .Select(s => new { stageName = s.StageName, contextReads = s.ContextReads })
            .ToList();

        var stagesWritingContext = flow.Stages
            .Where(s => s.ContextWrites.Count > 0)
            .Select(s => new { stageName = s.StageName, contextWrites = s.ContextWrites })
            .ToList();

        // Return as camelCase anonymous object for test compatibility
        return new
        {
            workflowName = flow.WorkflowName,
            flow = flow.Stages.Select(s => new {
                stageName = s.StageName,
                contextReads = s.ContextReads,
                contextWrites = s.ContextWrites
            }).ToList(),
            stagesReadingContext = stagesReadingContext,
            stagesWritingContext = stagesWritingContext
        };
    }
}
