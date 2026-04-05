namespace SphereIntegrationHub.Definitions;

public sealed record RandomValueDefinition(
    RandomValueType Type,
    string? Value = null,
    int? Min = null,
    int? Max = null,
    int? Padding = null,
    int? Length = null,
    DateTimeOffset? FromDateTime = null,
    DateTimeOffset? ToDateTime = null,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    TimeOnly? FromTime = null,
    TimeOnly? ToTime = null,
    string? Format = null,
    int? Start = null,
    int? Step = null,
    bool Update = false);
