namespace SphereIntegrationHub.Services;

public sealed class EnvironmentFileLoader
{
    public IReadOnlyDictionary<string, string> Load(string envFilePath)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityEnvironmentLoad);
        activity?.SetTag(TelemetryConstants.TagFilePath, envFilePath);

        if (string.IsNullOrWhiteSpace(envFilePath))
        {
            throw new ArgumentException("Environment file path is required.", nameof(envFilePath));
        }

        if (!File.Exists(envFilePath))
        {
            throw new FileNotFoundException("Environment file was not found.", envFilePath);
        }

        return KeyValueFileLoader.Load(
            envFilePath,
            '=',
            allowExportPrefix: true,
            invalidEntryMessage: "Invalid env file entry at line {0}.");
    }
}
