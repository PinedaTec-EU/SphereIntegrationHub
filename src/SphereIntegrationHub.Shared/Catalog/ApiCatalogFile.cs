using System.Text.Json;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace SphereIntegrationHub.Definitions;

public enum ApiCatalogFormat
{
    Json,
    Yaml
}

public static class ApiCatalogFile
{
    public const string CanonicalFileName = "api.catalog";
    public const string JsonFileName = "api-catalog.json";
    public const string YamlFileName = "api-catalog.yaml";
    public const string YmlFileName = "api-catalog.yml";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static string ResolvePath(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        var canonicalPath = Path.Combine(directory, CanonicalFileName);
        if (File.Exists(canonicalPath))
        {
            return canonicalPath;
        }

        var yamlPath = Path.Combine(directory, YamlFileName);
        if (File.Exists(yamlPath))
        {
            return yamlPath;
        }

        var ymlPath = Path.Combine(directory, YmlFileName);
        if (File.Exists(ymlPath))
        {
            return ymlPath;
        }

        var jsonPath = Path.Combine(directory, JsonFileName);
        if (File.Exists(jsonPath))
        {
            return jsonPath;
        }

        return canonicalPath;
    }

    public static IReadOnlyList<ApiCatalogVersion> Load(string catalogPath)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            throw new ArgumentException("Catalog path is required.", nameof(catalogPath));
        }

        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("Catalog file was not found.", catalogPath);
        }

        var content = File.ReadAllText(catalogPath);
        return Deserialize(content, GetFormat(catalogPath));
    }

    public static async Task<IReadOnlyList<ApiCatalogVersion>> LoadAsync(string catalogPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(catalogPath))
        {
            throw new ArgumentException("Catalog path is required.", nameof(catalogPath));
        }

        if (!File.Exists(catalogPath))
        {
            throw new FileNotFoundException("Catalog file was not found.", catalogPath);
        }

        var content = await File.ReadAllTextAsync(catalogPath, cancellationToken);
        return Deserialize(content, GetFormat(catalogPath));
    }

    public static List<ApiCatalogVersion> Deserialize(string content, ApiCatalogFormat format)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Catalog file is empty or invalid.");
        }

        List<ApiCatalogVersion>? catalog = format switch
        {
            ApiCatalogFormat.Json => JsonSerializer.Deserialize<List<ApiCatalogVersion>>(content, JsonOptions),
            ApiCatalogFormat.Yaml => YamlDeserializer.Deserialize<List<ApiCatalogVersion>>(content),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported catalog format.")
        };

        if (catalog is null || catalog.Count == 0)
        {
            throw new InvalidOperationException("Catalog file is empty or invalid.");
        }

        return catalog;
    }

    public static string Serialize(IEnumerable<ApiCatalogVersion> catalog, string outputPath)
        => Serialize(catalog, GetFormat(outputPath));

    public static string Serialize(IEnumerable<ApiCatalogVersion> catalog, ApiCatalogFormat format)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        var catalogList = catalog.ToList();
        return format switch
        {
            ApiCatalogFormat.Json => JsonSerializer.Serialize(catalogList, JsonOptions),
            ApiCatalogFormat.Yaml => YamlSerializer.Serialize(catalogList),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported catalog format.")
        };
    }

    public static async Task SaveAsync(string catalogPath, IEnumerable<ApiCatalogVersion> catalog, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(catalogPath);
        ArgumentNullException.ThrowIfNull(catalog);

        var directory = Path.GetDirectoryName(catalogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = Serialize(catalog, catalogPath);
        await File.WriteAllTextAsync(catalogPath, content, cancellationToken);
    }

    public static ApiCatalogFormat GetFormat(string catalogPath)
    {
        var extension = Path.GetExtension(catalogPath);
        return extension.ToLowerInvariant() switch
        {
            ".catalog" or ".yaml" or ".yml" => ApiCatalogFormat.Yaml,
            _ => ApiCatalogFormat.Json
        };
    }
}
