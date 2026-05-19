namespace SphereIntegrationHub.Definitions;

public sealed class LatencyProfileDefinition
{
    public string Name { get; set; } = string.Empty;
    public List<LatencyBandDefinition> Bands { get; set; } = [];
}

public sealed class LatencyBandDefinition
{
    public string Name { get; set; } = string.Empty;
    public long? MinMs { get; set; }
    public long? MaxMs { get; set; }
    public string? Color { get; set; }
    public string? Label { get; set; }
}

public static class LatencyProfileResolver
{
    public static IReadOnlyList<string> ValidateProfiles(
        IEnumerable<LatencyProfileDefinition>? profiles,
        string ownerLabel)
    {
        var errors = new List<string>();
        if (profiles is null)
        {
            return errors;
        }

        var seenProfileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
            {
                errors.Add($"{ownerLabel} latency profile name is required.");
                continue;
            }

            if (!seenProfileNames.Add(profile.Name))
            {
                errors.Add($"{ownerLabel} latency profile '{profile.Name}' is duplicated.");
                continue;
            }

            ValidateProfile(profile, ownerLabel, errors);
        }

        return errors;
    }

    public static Dictionary<string, LatencyProfileDefinition> BuildLookupOrThrow(
        IEnumerable<LatencyProfileDefinition>? profiles,
        string ownerLabel)
    {
        var errors = ValidateProfiles(profiles, ownerLabel);
        if (errors.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }

        var lookup = new Dictionary<string, LatencyProfileDefinition>(StringComparer.OrdinalIgnoreCase);
        if (profiles is null)
        {
            return lookup;
        }

        foreach (var profile in profiles)
        {
            lookup[profile.Name] = profile;
        }

        return lookup;
    }

    public static LatencyProfileDefinition? ResolveProfile(
        string? profileName,
        IReadOnlyDictionary<string, LatencyProfileDefinition>? workflowProfiles,
        IReadOnlyDictionary<string, LatencyProfileDefinition>? catalogProfiles)
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            return null;
        }

        if (workflowProfiles is not null && workflowProfiles.TryGetValue(profileName, out var workflowProfile))
        {
            return workflowProfile;
        }

        return catalogProfiles is not null && catalogProfiles.TryGetValue(profileName, out var catalogProfile)
            ? catalogProfile
            : null;
    }

    public static LatencyBandDefinition? ResolveBand(LatencyProfileDefinition profile, long durationMs)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return profile.Bands
            .OrderBy(band => band.MinMs ?? 0)
            .ThenBy(band => band.MaxMs ?? long.MaxValue)
            .FirstOrDefault(band =>
                durationMs >= (band.MinMs ?? 0) &&
                durationMs <= (band.MaxMs ?? long.MaxValue));
    }

    private static void ValidateProfile(
        LatencyProfileDefinition profile,
        string ownerLabel,
        List<string> errors)
    {
        if (profile.Bands.Count == 0)
        {
            errors.Add($"{ownerLabel} latency profile '{profile.Name}' must define at least one band.");
            return;
        }

        var seenBandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var band in profile.Bands)
        {
            if (string.IsNullOrWhiteSpace(band.Name))
            {
                errors.Add($"{ownerLabel} latency profile '{profile.Name}' contains a band without name.");
            }
            else if (!seenBandNames.Add(band.Name))
            {
                errors.Add($"{ownerLabel} latency profile '{profile.Name}' contains duplicate band '{band.Name}'.");
            }

            if (band.MinMs is < 0)
            {
                errors.Add($"{ownerLabel} latency profile '{profile.Name}' band '{band.Name}' minMs cannot be negative.");
            }

            if (band.MaxMs is < 0)
            {
                errors.Add($"{ownerLabel} latency profile '{profile.Name}' band '{band.Name}' maxMs cannot be negative.");
            }

            if (band.MinMs is null && band.MaxMs is null)
            {
                errors.Add($"{ownerLabel} latency profile '{profile.Name}' band '{band.Name}' must define minMs, maxMs, or both.");
            }

            if (band.MinMs is not null && band.MaxMs is not null && band.MinMs > band.MaxMs)
            {
                errors.Add($"{ownerLabel} latency profile '{profile.Name}' band '{band.Name}' has minMs greater than maxMs.");
            }
        }

        var orderedBands = profile.Bands
            .OrderBy(band => band.MinMs ?? 0)
            .ThenBy(band => band.MaxMs ?? long.MaxValue)
            .ToArray();

        for (var index = 1; index < orderedBands.Length; index++)
        {
            var previous = orderedBands[index - 1];
            var current = orderedBands[index];
            var previousMax = previous.MaxMs ?? long.MaxValue;
            var currentMin = current.MinMs ?? 0;
            if (currentMin <= previousMax)
            {
                errors.Add($"{ownerLabel} latency profile '{profile.Name}' has overlapping bands '{previous.Name}' and '{current.Name}'.");
            }
        }
    }
}
