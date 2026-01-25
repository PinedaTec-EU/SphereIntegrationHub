namespace SphereIntegrationHub.cli;

internal interface IOpenTelemetryBootstrapper
{
    OpenTelemetryHandle Start(WorkflowConfig config);
}
