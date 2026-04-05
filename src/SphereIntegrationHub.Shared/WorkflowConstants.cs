namespace SphereIntegrationHub.Definitions;

public static class WorkflowConstants
{
    // File extensions
    public const string ExtWorkflow = ".workflow";
    public const string ExtWfvars = ".wfvars";
    public const string ExtYaml = ".yaml";
    public const string ExtYml = ".yml";

    public const string GlobWorkflow = "*.workflow";
    public const string GlobYaml = "*.yaml";
    public const string GlobYml = "*.yml";

    // Control flow reserved keyword
    public const string EndStage = "endStage";

    // Ensure modes
    public const string EnsureModeCreateIfMissing = "CreateIfMissing";
    public const string EnsureModeUpsert = "Upsert";
}
