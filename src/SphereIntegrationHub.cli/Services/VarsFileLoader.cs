namespace SphereIntegrationHub.Services;

public sealed class VarsFileLoader
{
    public IReadOnlyDictionary<string, string> Load(string varsFilePath, string? environment = null, string? version = null)
        => LoadWithDetails(varsFilePath, environment, version).Values;

    public VarsFileResolution LoadWithDetails(string varsFilePath, string? environment = null, string? version = null)
    {
        using var activity = Telemetry.ActivitySource.StartActivity(TelemetryConstants.ActivityVarsLoad);
        activity?.SetTag(TelemetryConstants.TagFilePath, varsFilePath);

        if (string.IsNullOrWhiteSpace(varsFilePath))
        {
            throw new ArgumentException("Vars file path is required.", nameof(varsFilePath));
        }

        if (!File.Exists(varsFilePath))
        {
            throw new FileNotFoundException("Vars file was not found.", varsFilePath);
        }

        var content = Parse(varsFilePath);
        return content.ResolveWithDetails(environment, version);
    }

    private static VarsFileContent Parse(string filePath)
    {
        var content = new VarsFileContent();
        var lines = File.ReadAllLines(filePath);
        string? currentEnvironment = null;
        string? currentVersion = null;

        for (var lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            var line = lines[lineNumber].Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                throw new InvalidOperationException(string.Format("Invalid vars file entry at line {0}.", lineNumber + 1));
            }

            var key = line[..separatorIndex].Trim();
            var value = line[(separatorIndex + 1)..].Trim();
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new InvalidOperationException(string.Format("Invalid vars file entry at line {0}.", lineNumber + 1));
            }

            if (string.IsNullOrEmpty(value))
            {
                if (currentEnvironment is not null &&
                    !string.Equals(currentEnvironment, VarsFileContent.GlobalEnvironment, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(key, "version", StringComparison.OrdinalIgnoreCase))
                {
                    currentVersion = null;
                    continue;
                }

                if (string.Equals(key, "global", StringComparison.OrdinalIgnoreCase))
                {
                    currentEnvironment = VarsFileContent.GlobalEnvironment;
                    currentVersion = null;
                }
                else
                {
                    currentEnvironment = key;
                    currentVersion = null;
                    content.RegisterEnvironment(key);
                }

                continue;
            }

            if (currentEnvironment is not null &&
                !string.Equals(currentEnvironment, VarsFileContent.GlobalEnvironment, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(key, "version", StringComparison.OrdinalIgnoreCase))
            {
                currentVersion = Unquote(value);
                content.RegisterEnvironment(currentEnvironment);
                continue;
            }

            var target = content.ResolveTarget(currentEnvironment, currentVersion);
            target[key] = Unquote(value);
        }

        return content;
    }

    private static string Unquote(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private sealed class VarsFileContent
    {
        public const string GlobalEnvironment = "global";

        private readonly Dictionary<string, string> _global = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, string>> _environmentValues = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> _versionValues = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _environments = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterEnvironment(string environment)
        {
            if (!string.IsNullOrWhiteSpace(environment))
            {
                _environments.Add(environment);
            }
        }

        public Dictionary<string, string> ResolveTarget(string? environment, string? version)
        {
            if (string.IsNullOrWhiteSpace(environment) ||
                string.Equals(environment, GlobalEnvironment, StringComparison.OrdinalIgnoreCase))
            {
                return _global;
            }

            RegisterEnvironment(environment);

            if (!string.IsNullOrWhiteSpace(version))
            {
                if (!_versionValues.TryGetValue(environment, out var versions))
                {
                    versions = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                    _versionValues[environment] = versions;
                }

                if (!versions.TryGetValue(version, out var values))
                {
                    values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    versions[version] = values;
                }

                return values;
            }

            if (!_environmentValues.TryGetValue(environment, out var envValues))
            {
                envValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _environmentValues[environment] = envValues;
            }

            return envValues;
        }

        public IReadOnlyDictionary<string, string> Resolve(string? environment, string? version)
            => ResolveWithDetails(environment, version).Values;

        public VarsFileResolution ResolveWithDetails(string? environment, string? version)
        {
            var resolved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var sources = new Dictionary<string, VarsFileSource>(StringComparer.OrdinalIgnoreCase);

            foreach (var pair in _global)
            {
                resolved[pair.Key] = pair.Value;
                sources[pair.Key] = VarsFileSource.Global;
            }

            if (string.IsNullOrWhiteSpace(environment))
            {
                return new VarsFileResolution(resolved, sources);
            }

            var hasEnvironments = _environments.Count > 0;
            var hasEnvironment = _environments.Contains(environment);
            if (!hasEnvironment && hasEnvironments && _global.Count == 0)
            {
                throw new InvalidOperationException($"Vars file does not define environment '{environment}' and has no global variables.");
            }

            if (!hasEnvironment)
            {
                return new VarsFileResolution(resolved, sources);
            }

            if (_environmentValues.TryGetValue(environment, out var envValues))
            {
                foreach (var pair in envValues)
                {
                    resolved[pair.Key] = pair.Value;
                    sources[pair.Key] = VarsFileSource.ForEnvironment(environment);
                }
            }

            if (!string.IsNullOrWhiteSpace(version) &&
                _versionValues.TryGetValue(environment, out var versions) &&
                versions.TryGetValue(version, out var versionValues))
            {
                foreach (var pair in versionValues)
                {
                    resolved[pair.Key] = pair.Value;
                    sources[pair.Key] = VarsFileSource.ForVersion(environment, version);
                }
            }

            return new VarsFileResolution(resolved, sources);
        }
    }
}

public sealed record VarsFileResolution(
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, VarsFileSource> Sources);

public sealed record VarsFileSource(string Scope, string? Environment, string? Version)
{
    public static VarsFileSource Global { get; } = new("global", null, null);

    public static VarsFileSource ForEnvironment(string environment)
        => new("environment", environment, null);

    public static VarsFileSource ForVersion(string environment, string version)
        => new("version", environment, version);
}
