namespace SphereIntegrationHub.Services;

internal static class ExecutionLogFormatter
{
    public static string FormatWorkflowTag(string name) => $"[{name}]";

    public static string FormatStageTag(string workflowName, string stageName)
        => $"{FormatWorkflowTag(workflowName)}#{stageName}";

    public static string GetIndent(int indentLevel)
        => indentLevel <= 0 ? string.Empty : new string(' ', indentLevel);

    public static string GetIndent(ExecutionContext context) => GetIndent(context.IndentLevel);
}
