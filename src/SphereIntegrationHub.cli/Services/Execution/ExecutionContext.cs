using SphereIntegrationHub.Definitions;
using System;
using System.Collections.Generic;

namespace SphereIntegrationHub.Services;

public sealed class ExecutionContext
{
    public ExecutionContext(
        IReadOnlyDictionary<string, string> inputs,
        IReadOnlyDictionary<string, string> environmentVariables,
        IDictionary<string, string>? parentContext = null,
        int indentLevel = 0)
    {
        Inputs = new Dictionary<string, string>(inputs, StringComparer.OrdinalIgnoreCase);
        EnvironmentVariables = new Dictionary<string, string>(environmentVariables, StringComparer.OrdinalIgnoreCase);
        Globals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        EndpointOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        WorkflowOutputs = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        WorkflowResults = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        CircuitBreakers = new Dictionary<string, CircuitBreakerState>(StringComparer.OrdinalIgnoreCase);
        Context = parentContext is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(parentContext, StringComparer.OrdinalIgnoreCase);
        IndentLevel = Math.Max(0, indentLevel);
    }

    public Dictionary<string, string> Inputs { get; }
    public Dictionary<string, string> EnvironmentVariables { get; }
    public Dictionary<string, string> Globals { get; }
    public Dictionary<string, string> Context { get; }
    public Dictionary<string, IReadOnlyDictionary<string, string>> EndpointOutputs { get; }
    public Dictionary<string, IReadOnlyDictionary<string, string>> WorkflowOutputs { get; }
    public Dictionary<string, IReadOnlyDictionary<string, string>> WorkflowResults { get; }
    public Dictionary<string, CircuitBreakerState> CircuitBreakers { get; }
    public string? OutputFilePath { get; set; }
    public int IndentLevel { get; }

    public TemplateContext BuildTemplateContext()
    {
        return new TemplateContext(Inputs, Globals, Context, EndpointOutputs, WorkflowOutputs, WorkflowResults, EnvironmentVariables);
    }
}
