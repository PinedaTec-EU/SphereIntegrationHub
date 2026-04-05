namespace SphereIntegrationHub.Definitions;

public sealed record RandomValueFormattingOptions(string DateFormat, string TimeFormat, string DateTimeFormat)
{
    public static RandomValueFormattingOptions Default { get; } = new("yyyy-MM-dd", "HH:mm:ss", "O");
}
