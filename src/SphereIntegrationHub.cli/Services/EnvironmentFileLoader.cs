namespace SphereIntegrationHub.Services;

public sealed class EnvironmentFileLoader
{
    private static readonly IReadOnlyDictionary<string, string> EmptyEnvironmentVariables =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyDictionary<string, string> Load(
        string envFilePath,
        IReadOnlyDictionary<string, string>? inheritedVariables = null)
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

        var rawValues = KeyValueFileLoader.Load(
            envFilePath,
            '=',
            allowExportPrefix: true,
            invalidEntryMessage: "Invalid env file entry at line {0}.");

        return ResolveValues(rawValues, inheritedVariables ?? EmptyEnvironmentVariables);
    }

    private static IReadOnlyDictionary<string, string> ResolveValues(
        IReadOnlyDictionary<string, string> rawValues,
        IReadOnlyDictionary<string, string> inheritedVariables)
    {
        var templateResolver = new TemplateResolver();
        var resolvedValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pendingKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DeferredEnvironmentVariables envVariables = null!;
        envVariables = new DeferredEnvironmentVariables(rawValues, inheritedVariables, ResolveValue);

        foreach (var key in rawValues.Keys)
        {
            ResolveValue(key);
        }

        return resolvedValues;

        string ResolveValue(string key)
        {
            if (resolvedValues.TryGetValue(key, out var existingValue))
            {
                return existingValue;
            }

            if (!rawValues.TryGetValue(key, out var rawValue))
            {
                if (inheritedVariables.TryGetValue(key, out var inheritedValue))
                {
                    return inheritedValue;
                }

                var processValue = Environment.GetEnvironmentVariable(key);
                if (processValue is not null)
                {
                    return processValue;
                }

                throw new InvalidOperationException($"Environment variable '{key}' was not found.");
            }

            if (!pendingKeys.Add(key))
            {
                throw new InvalidOperationException($"Environment variable '{key}' has a circular reference.");
            }

            try
            {
                var resolvedValue = rawValue.Contains("{{", StringComparison.Ordinal)
                    ? templateResolver.ResolveTemplate(rawValue, BuildTemplateContext(envVariables))
                    : rawValue;
                resolvedValues[key] = resolvedValue;
                return resolvedValue;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Environment variable '{key}' could not be resolved: {ex.Message}", ex);
            }
            finally
            {
                pendingKeys.Remove(key);
            }
        }
    }

    private static TemplateContext BuildTemplateContext(IReadOnlyDictionary<string, string> environmentVariables)
    {
        return new TemplateContext(
            EmptyEnvironmentVariables,
            EmptyEnvironmentVariables,
            EmptyEnvironmentVariables,
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase),
            environmentVariables);
    }

    private sealed class DeferredEnvironmentVariables(
        IReadOnlyDictionary<string, string> rawValues,
        IReadOnlyDictionary<string, string> inheritedVariables,
        Func<string, string> resolveValue) : IReadOnlyDictionary<string, string>
    {
        public IEnumerable<string> Keys => rawValues.Keys.Concat(inheritedVariables.Keys).Distinct(StringComparer.OrdinalIgnoreCase);

        public IEnumerable<string> Values => Keys.Select(key => this[key]);

        public int Count => Keys.Count();

        public string this[string key] => resolveValue(key);

        public bool ContainsKey(string key)
            => rawValues.ContainsKey(key) ||
               inheritedVariables.ContainsKey(key) ||
               Environment.GetEnvironmentVariable(key) is not null;

        public bool TryGetValue(string key, out string value)
        {
            if (!ContainsKey(key))
            {
                value = string.Empty;
                return false;
            }

            value = resolveValue(key);
            return true;
        }

        public IEnumerator<KeyValuePair<string, string>> GetEnumerator()
            => Keys.Select(key => new KeyValuePair<string, string>(key, this[key])).GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
