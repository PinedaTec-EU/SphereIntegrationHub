using SphereIntegrationHub.MCP.Models;
using SphereIntegrationHub.MCP.Services.Integration;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.MCP.Services.Analysis;

/// <summary>
/// Analyzes variable scope and availability at different points in workflow execution
/// </summary>
public sealed class VariableScopeAnalyzer
{
    private readonly SihServicesAdapter _adapter;
    private readonly IDeserializer _yamlDeserializer;

    public VariableScopeAnalyzer(SihServicesAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _yamlDeserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
    }

    /// <summary>
    /// Gets all available variables at a specific stage in the workflow
    /// </summary>
    public async Task<VariableScope> GetAvailableVariablesAsync(string workflowPath, string? atStage = null)
    {
        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException($"Workflow file not found: {workflowPath}");
        }

        var yamlContent = await File.ReadAllTextAsync(workflowPath);
        var workflow = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);

        var scope = new VariableScope();

        // Add input variables
        if (workflow.TryGetValue("input", out var inputObj) && inputObj is List<object> inputs)
        {
            foreach (var input in inputs)
            {
                if (input is Dictionary<object, object> inputDict)
                {
                    var inputData = inputDict.ToDictionary(
                        kvp => kvp.Key.ToString() ?? "",
                        kvp => kvp.Value);

                    scope.Inputs.Add(new InputVariable
                    {
                        Name = inputData.GetValueOrDefault("name")?.ToString() ?? "",
                        Type = inputData.GetValueOrDefault("type")?.ToString() ?? "string",
                        Required = inputData.GetValueOrDefault("required")?.ToString()?.ToLowerInvariant() == "true"
                    });
                }
            }
        }

        // Add global variables from init-stage
        if (workflow.TryGetValue("init-stage", out var initStageObj) && initStageObj is Dictionary<object, object> initStageDict)
        {
            var initStage = initStageDict.ToDictionary(
                kvp => kvp.Key.ToString() ?? "",
                kvp => kvp.Value);

            if (initStage.TryGetValue("variables", out var varsObj) && varsObj is List<object> variables)
            {
                foreach (var variable in variables)
                {
                    if (variable is Dictionary<object, object> varDict)
                    {
                        var varData = varDict.ToDictionary(
                            kvp => kvp.Key.ToString() ?? "",
                            kvp => kvp.Value);

                        scope.Globals.Add(new GlobalVariable
                        {
                            Name = varData.GetValueOrDefault("name")?.ToString() ?? "",
                            Type = varData.GetValueOrDefault("type")?.ToString() ?? "string",
                            Value = varData.GetValueOrDefault("value")?.ToString() ?? ""
                        });
                    }
                }
            }
        }

        // Add system variables
        scope.System.AddRange(GetSystemVariables());

        // Add environment variables
        scope.Env.AddRange(GetEnvironmentVariables());

        // Add stage outputs up to the specified stage
        var stageFound = false;
        if (workflow.TryGetValue("stages", out var stagesObj) && stagesObj is List<object> stages)
        {
            foreach (var stageObj in stages)
            {
                if (stageObj is Dictionary<object, object> stageDict)
                {
                    var stage = stageDict.ToDictionary(
                        kvp => kvp.Key.ToString() ?? "",
                        kvp => kvp.Value);

                    var stageName = stage.GetValueOrDefault("name")?.ToString() ?? "";

                    // If we've reached the target stage, stop here
                    if (!string.IsNullOrEmpty(atStage) && stageName.Equals(atStage, StringComparison.OrdinalIgnoreCase))
                    {
                        stageFound = true;
                        break;
                    }

                    // Add this stage's outputs
                    scope.StageOutputs.Add(new StageOutputVariable
                    {
                        Stage = stageName,
                        Outputs = InferStageOutputs(stage)
                    });
                }
            }
        }

        // If a specific stage was requested but not found, throw exception
        if (!string.IsNullOrEmpty(atStage) && !stageFound)
        {
            throw new InvalidOperationException($"Stage '{atStage}' not found in workflow");
        }

        return scope;
    }

    /// <summary>
    /// Resolves a template token to its source and type
    /// </summary>
    public async Task<TokenResolution> ResolveTemplateTokenAsync(string workflowPath, string token)
    {
        var scope = await GetAvailableVariablesAsync(workflowPath);

        // Remove {{ }} if present
        token = token.Trim().TrimStart('{').TrimEnd('}').Trim();

        // Parse token parts
        var parts = token.Split('.');

        if (parts.Length == 0)
        {
            return new TokenResolution
            {
                Token = token,
                Valid = false,
                Message = "Empty token"
            };
        }

        var prefix = parts[0].ToLowerInvariant();

        return prefix switch
        {
            "input" when parts.Length > 1 => ResolveInputToken(parts[1], scope.Inputs),
            "global" when parts.Length > 1 => ResolveGlobalToken(parts[1], scope.Globals),
            "env" when parts.Length > 1 => ResolveEnvToken(parts[1], scope.Env),
            "system" when parts.Length > 1 => ResolveSystemToken(parts[1], scope.System),
            "stages" when parts.Length > 2 => ResolveStageToken(parts[1], parts[2], scope.StageOutputs),
            "context" when parts.Length > 1 => ResolveContextToken(parts[1], scope.Context),
            _ => new TokenResolution
            {
                Token = token,
                Valid = false,
                Message = $"Unknown token prefix: {prefix}"
            }
        };
    }

    /// <summary>
    /// Analyzes context flow through the workflow
    /// </summary>
    public async Task<ContextFlow> AnalyzeContextFlowAsync(string workflowPath)
    {
        if (!File.Exists(workflowPath))
        {
            throw new FileNotFoundException($"Workflow file not found: {workflowPath}");
        }

        var yamlContent = await File.ReadAllTextAsync(workflowPath);
        var workflow = _yamlDeserializer.Deserialize<Dictionary<string, object>>(yamlContent);

        var flow = new ContextFlow
        {
            WorkflowName = workflow.GetValueOrDefault("name")?.ToString() ?? "unknown",
            Stages = []
        };

        if (workflow.TryGetValue("stages", out var stagesObj) && stagesObj is List<object> stages)
        {
            foreach (var stageObj in stages)
            {
                if (stageObj is Dictionary<object, object> stageDict)
                {
                    var stage = stageDict.ToDictionary(
                        kvp => kvp.Key.ToString() ?? "",
                        kvp => kvp.Value);

                    var stageName = stage.GetValueOrDefault("name")?.ToString() ?? "";
                    var contextReads = ExtractContextReads(stage);
                    var contextWrites = ExtractContextWrites(stage);

                    flow.Stages.Add(new StageContextInfo
                    {
                        StageName = stageName,
                        ContextReads = contextReads,
                        ContextWrites = contextWrites
                    });
                }
            }
        }

        return flow;
    }

    private static List<SystemVariable> GetSystemVariables()
    {
        return
        [
            new SystemVariable { Token = "system.timestamp", Description = "Current timestamp" },
            new SystemVariable { Token = "system.date", Description = "Current date" },
            new SystemVariable { Token = "system.time", Description = "Current time" },
            new SystemVariable { Token = "system.guid", Description = "Random GUID" },
            new SystemVariable { Token = "system.random", Description = "Random number" }
        ];
    }

    private static List<EnvironmentVariable> GetEnvironmentVariables()
    {
        return
        [
            new EnvironmentVariable { Name = "ENVIRONMENT", Value = "local" },
            new EnvironmentVariable { Name = "API_BASE_URL", Value = "http://localhost" }
        ];
    }

    private static List<OutputField> InferStageOutputs(Dictionary<string, object> stage)
    {
        var outputs = new List<OutputField>();

        // If stage has explicit output configuration
        if (stage.TryGetValue("output", out var outputObj) && outputObj is Dictionary<object, object> outputDict)
        {
            var output = outputDict.ToDictionary(
                kvp => kvp.Key.ToString() ?? "",
                kvp => kvp.Value);

            // Standard API response fields
            outputs.Add(new OutputField { Name = "status", Type = "integer" });
            outputs.Add(new OutputField { Name = "body", Type = "object" });
            outputs.Add(new OutputField { Name = "headers", Type = "object" });
        }

        return outputs;
    }

    private static TokenResolution ResolveInputToken(string name, List<InputVariable> inputs)
    {
        var input = inputs.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (input != null)
        {
            return new TokenResolution
            {
                Token = $"input.{name}",
                Valid = true,
                Source = "Workflow input parameter",
                Type = input.Type,
                Required = input.Required
            };
        }

        return new TokenResolution
        {
            Token = $"input.{name}",
            Valid = false,
            Message = $"Input parameter '{name}' not found"
        };
    }

    private static TokenResolution ResolveGlobalToken(string name, List<GlobalVariable> globals)
    {
        var global = globals.FirstOrDefault(g => g.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (global != null)
        {
            return new TokenResolution
            {
                Token = $"global.{name}",
                Valid = true,
                Source = "Global variable (init-stage)",
                Type = global.Type
            };
        }

        return new TokenResolution
        {
            Token = $"global.{name}",
            Valid = false,
            Message = $"Global variable '{name}' not found"
        };
    }

    private static TokenResolution ResolveEnvToken(string name, List<EnvironmentVariable> envVars)
    {
        return new TokenResolution
        {
            Token = $"env.{name}",
            Valid = true,
            Source = "Environment variable",
            Type = "string"
        };
    }

    private static TokenResolution ResolveSystemToken(string name, List<SystemVariable> systemVars)
    {
        var sysVar = systemVars.FirstOrDefault(s => s.Token.EndsWith($".{name}", StringComparison.OrdinalIgnoreCase));
        if (sysVar != null)
        {
            return new TokenResolution
            {
                Token = $"system.{name}",
                Valid = true,
                Source = "System variable",
                Type = "string",
                Description = sysVar.Description
            };
        }

        return new TokenResolution
        {
            Token = $"system.{name}",
            Valid = false,
            Message = $"System variable '{name}' not found"
        };
    }

    private static TokenResolution ResolveStageToken(string stageName, string field, List<StageOutputVariable> stageOutputs)
    {
        var stage = stageOutputs.FirstOrDefault(s => s.Stage.Equals(stageName, StringComparison.OrdinalIgnoreCase));
        if (stage != null)
        {
            return new TokenResolution
            {
                Token = $"stages.{stageName}.{field}",
                Valid = true,
                Source = $"Output from stage '{stageName}'",
                Type = "dynamic"
            };
        }

        return new TokenResolution
        {
            Token = $"stages.{stageName}.{field}",
            Valid = false,
            Message = $"Stage '{stageName}' not found or not yet executed"
        };
    }

    private static TokenResolution ResolveContextToken(string name, List<ContextVariable> context)
    {
        return new TokenResolution
        {
            Token = $"context.{name}",
            Valid = true,
            Source = "Workflow context",
            Type = "dynamic"
        };
    }

    private static List<string> ExtractContextReads(Dictionary<string, object> stage)
    {
        var reads = new List<string>();
        foreach (var (key, value) in stage)
        {
            if (value is string strValue)
            {
                var tokens = ExtractTemplateTokens(strValue);
                reads.AddRange(tokens.Where(t => t.StartsWith("context.")));
            }
        }
        return reads.Distinct().ToList();
    }

    private static List<string> ExtractContextWrites(Dictionary<string, object> stage)
    {
        var writes = new List<string>();
        if (stage.TryGetValue("output", out var outputObj) && outputObj is Dictionary<object, object> outputDict)
        {
            var output = outputDict.ToDictionary(
                kvp => kvp.Key.ToString() ?? "",
                kvp => kvp.Value);

            if (output.TryGetValue("context", out var contextObj))
            {
                writes.Add(contextObj.ToString() ?? "");
            }
        }
        return writes;
    }

    private static List<string> ExtractTemplateTokens(string value)
    {
        var tokens = new List<string>();
        var pattern = @"\{\{([^}]+)\}\}";
        var matches = System.Text.RegularExpressions.Regex.Matches(value, pattern);

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Groups.Count > 1)
            {
                tokens.Add(match.Groups[1].Value.Trim());
            }
        }

        return tokens;
    }
}

public sealed class TokenResolution
{
    public required string Token { get; init; }
    public required bool Valid { get; init; }
    public string? Source { get; init; }
    public string? Type { get; init; }
    public bool Required { get; init; }
    public string? Description { get; init; }
    public string? Message { get; init; }
}

public sealed class ContextFlow
{
    public required string WorkflowName { get; init; }
    public required List<StageContextInfo> Stages { get; init; }
}

public sealed class StageContextInfo
{
    public required string StageName { get; init; }
    public List<string> ContextReads { get; init; } = [];
    public List<string> ContextWrites { get; init; } = [];
}
