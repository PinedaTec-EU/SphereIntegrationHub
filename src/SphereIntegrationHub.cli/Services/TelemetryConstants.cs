namespace SphereIntegrationHub.Services;

internal static class TelemetryConstants
{
    internal const string ActivityCliRun = "cli.run";
    internal const string ActivityWorkflowExecute = "workflow.execute";
    internal const string ActivityWorkflowStage = "workflow.stage";
    internal const string ActivityWorkflowLoad = "workflow.load";
    internal const string ActivityWorkflowValidate = "workflow.validate";
    internal const string ActivityCatalogLoad = "catalog.load";
    internal const string ActivitySwaggerCache = "swagger.cache";
    internal const string ActivityEndpointValidate = "endpoint.validate";
    internal const string ActivityHttpRequest = "http.request";
    internal const string ActivityWorkflowOutputWrite = "workflow.output.write";
    internal const string ActivityWorkflowPlan = "workflow.plan";
    internal const string ActivityWorkflowConfigLoad = "workflow.config.load";
    internal const string ActivityEnvironmentLoad = "environment.load";
    internal const string ActivityVarsLoad = "vars.load";
    internal const string ActivityKeyValueLoad = "keyvalue.load";
    internal const string ActivityTemplateResolve = "template.resolve";
    internal const string ActivityTemplateTokenResolve = "template.token.resolve";
    internal const string ActivityRunIfParse = "runif.parse";
    internal const string ActivityRandomValueGenerate = "random.generate";
    internal const string ActivityMockPayloadLoad = "mock.payload.load";
    internal const string ActivityMockPayloadLoadFromFile = "mock.payload.load.file";
    internal const string ActivityMockPayloadValidate = "mock.payload.validate";
    internal const string ActivityApiBaseUrlResolve = "api.baseurl.resolve";

    internal const string TagWorkflowName = "workflow.name";
    internal const string TagWorkflowId = "workflow.id";
    internal const string TagWorkflowVersion = "workflow.version";
    internal const string TagWorkflowPath = "workflow.path";
    internal const string TagCatalogPath = "catalog.path";
    internal const string TagCatalogVersion = "catalog.version";
    internal const string TagApiDefinition = "api.definition";
    internal const string TagStageName = "stage.name";
    internal const string TagStageKind = "stage.kind";
    internal const string TagHttpMethod = "http.method";
    internal const string TagHttpBaseUrl = "http.url.base";
    internal const string TagHttpPath = "http.url.path";
    internal const string TagHttpStatusCode = "http.status_code";
    internal const string TagFilePath = "file.path";
    internal const string TagFileSeparator = "file.separator";
    internal const string TagFileAllowExport = "file.allow_export";
    internal const string TagTemplateLength = "template.length";
    internal const string TagTemplateTokenRoot = "template.token.root";
    internal const string TagExpressionLength = "expression.length";
    internal const string TagRandomType = "random.type";
    internal const string TagEnvironment = "environment";
}
