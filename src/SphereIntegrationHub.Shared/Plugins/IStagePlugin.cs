namespace SphereIntegrationHub.Definitions;

public interface IStagePlugin
{
    StagePluginDescriptor Descriptor { get; }

    void ValidateStage(
        WorkflowStageDefinition stage,
        StagePluginValidationContext context,
        List<string> errors,
        List<string> warnings);

    Task<StagePluginExecutionResult> ExecuteAsync(
        WorkflowStageDefinition stage,
        StagePluginExecutionContext context,
        CancellationToken cancellationToken);
}

public abstract class StagePluginBase : IStagePlugin
{
    protected StagePluginBase(string id, params string[] stageKinds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(stageKinds);

        Descriptor = new StagePluginDescriptor(
            id,
            StagePluginContract.Version,
            GetType().Assembly.GetName().Version?.ToString() ?? StagePluginContract.Version,
            stageKinds);
    }

    public StagePluginDescriptor Descriptor { get; }

    public abstract void ValidateStage(
        WorkflowStageDefinition stage,
        StagePluginValidationContext context,
        List<string> errors,
        List<string> warnings);

    public abstract Task<StagePluginExecutionResult> ExecuteAsync(
        WorkflowStageDefinition stage,
        StagePluginExecutionContext context,
        CancellationToken cancellationToken);
}

public static class StagePluginContract
{
    public const string Version = "1.0";
}

public sealed record StagePluginDescriptor(
    string Id,
    string ContractVersion,
    string RuntimeVersion,
    IReadOnlyCollection<string> StageKinds);

public sealed record StagePluginValidationContext(
    WorkflowDefinition Workflow,
    string WorkflowPath,
    ApiCatalogVersion? CatalogVersion,
    PluginCatalogDefinition? CatalogPlugin,
    bool ValidateRequiredParameters);

public sealed record StagePluginExecutionContext(
    Func<string, string> ResolveTemplate,
    Func<string, string> LoadDataFile,
    Func<StageTransportRequest, CancellationToken, Task<StageTransportResponse>> SendAsync,
    Func<WorkflowStageDefinition, string, CancellationToken, Task<StagePluginExecutionResult>>? InvokeEndpointAsync,
    IReadOnlyDictionary<string, string> ApiBaseUrls,
    string WorkflowPath);

public sealed record StagePluginExecutionResult(
    ResponseContext Response,
    string RequestUri,
    string Operation,
    string? RequestBody);

public sealed record StageTransportRequest(
    string Method,
    string RequestUri,
    IReadOnlyDictionary<string, string>? Headers,
    string? Body,
    string? ContentType);

public sealed record StageTransportResponse(
    int StatusCode,
    string Body,
    IReadOnlyDictionary<string, string> Headers,
    string RequestUri,
    string Method,
    string? RequestBody);
