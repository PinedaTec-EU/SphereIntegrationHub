namespace SphereIntegrationHub.Definitions;

public sealed class WorkflowVariableDefinition
{
    public string Name { get; set; } = string.Empty;
    public RandomValueType Type { get; set; }
    public string? Value { get; set; }
    public int? Min { get; set; }
    public int? Max { get; set; }
    public int? Padding { get; set; }
    public int? Length { get; set; }
    public DateTimeOffset? FromDateTime { get; set; }
    public DateTimeOffset? ToDateTime { get; set; }
    public DateOnly? FromDate { get; set; }
    public DateOnly? ToDate { get; set; }
    public TimeOnly? FromTime { get; set; }
    public TimeOnly? ToTime { get; set; }
    public string? Format { get; set; }
    public int? Start { get; set; }
    public int? Step { get; set; }
}
