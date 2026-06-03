using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

using SphereIntegrationHub.cli;
using SphereIntegrationHub.Definitions;

namespace SphereIntegrationHub.Services;

internal sealed class ExecutionReportGenerator
{
    private const string ReportFilePattern = "*.workflow.report.json";
    private const string SnapshotFilePattern = "*.workflow.snapshot.json";
    private readonly ICliOutputProvider _output;
    private readonly ExecutionReportHtmlRenderer _htmlRenderer;

    public ExecutionReportGenerator(
        ICliOutputProvider output,
        ExecutionReportHtmlRenderer? htmlRenderer = null)
    {
        _output = output;
        _htmlRenderer = htmlRenderer ?? new ExecutionReportHtmlRenderer();
    }

    public async Task<int> GenerateAndOpenAsync(InlineArguments args, CancellationToken cancellationToken)
    {
        var path = args.ExecutionReportPath!;
        var fullPath = Path.GetFullPath(path);

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            _output.Error.WriteLine($"Execution report path not found: {path}");

            return 1;
        }

        ReportArtifact[] reports;
        SnapshotArtifact[] snapshots;
        try
        {
            reports = await LoadReportsAsync(fullPath, cancellationToken);
            snapshots = await LoadSnapshotsAsync(fullPath, args.SnapshotPath, args.CatalogPath, reports, cancellationToken);
        }
        catch (Exception ex)
        {
            _output.Error.WriteLine($"Failed to read execution artifacts: {ex.Message}");

            return 1;
        }

        if (reports.Length == 0)
        {
            _output.Error.WriteLine($"No execution reports found in: {path}");

            return 1;
        }

        var selectedReport = reports[^1];
        var outputDir = args.ReportOutputPath ?? GetDefaultOutputDirectory(fullPath, selectedReport.Path);
        Directory.CreateDirectory(outputDir);

        var baseName = BuildOutputBaseName(fullPath, selectedReport.Path);
        var htmlPath = Path.Combine(outputDir, $"{baseName}.workflow.report.html");

        var appVersion = typeof(ExecutionReportGenerator).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion?.Split('+')[0]
            ?? typeof(ExecutionReportGenerator).Assembly.GetName().Version?.ToString()
            ?? string.Empty;
        var html = _htmlRenderer.Render(reports, reports.Length - 1, appVersion, snapshots);
        await File.WriteAllTextAsync(htmlPath, html, cancellationToken);

        _output.Out.WriteLine($"Report: {htmlPath}");

        if (args.OpenAfterGenerate)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = htmlPath, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _output.Error.WriteLine($"Could not open browser: {ex.Message}");
            }
        }

        return 0;
    }

    private static async Task<ReportArtifact[]> LoadReportsAsync(string path, CancellationToken cancellationToken)
    {
        if (File.Exists(path))
        {
            return [await LoadReportAsync(path, cancellationToken)];
        }

        var reportFiles = Directory.GetFiles(path, ReportFilePattern, SearchOption.TopDirectoryOnly)
            .OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal)
            .ToArray();

        var reports = new List<ReportArtifact>(reportFiles.Length);
        foreach (var reportFile in reportFiles)
        {
            reports.Add(await LoadReportAsync(reportFile, cancellationToken));
        }

        return reports.ToArray();
    }

    private static async Task<SnapshotArtifact[]> LoadSnapshotsAsync(
        string reportPath,
        string? requestedSnapshotPath,
        string? catalogPath,
        IReadOnlyList<ReportArtifact> reports,
        CancellationToken cancellationToken)
    {
        var snapshotFiles = new SortedSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(requestedSnapshotPath))
        {
            AddSnapshotFiles(Path.GetFullPath(requestedSnapshotPath), required: true, snapshotFiles);
        }

        foreach (var catalogSnapshotPath in ResolveCatalogSnapshotPaths(catalogPath, reports))
        {
            AddSnapshotFiles(catalogSnapshotPath, required: true, snapshotFiles);
        }

        foreach (var candidateDirectory in GetImplicitSnapshotDirectories(reportPath))
        {
            AddSnapshotFiles(candidateDirectory, required: false, snapshotFiles);
        }

        var snapshots = new List<SnapshotArtifact>(snapshotFiles.Count);
        foreach (var snapshotFile in snapshotFiles)
        {
            snapshots.Add(await LoadSnapshotAsync(snapshotFile, cancellationToken));
        }

        return snapshots.ToArray();
    }

    private static IEnumerable<string> ResolveCatalogSnapshotPaths(
        string? catalogPath,
        IReadOnlyList<ReportArtifact> reports)
    {
        var catalogPaths = ResolveCandidateCatalogPaths(catalogPath, reports)
            .Where(File.Exists)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (catalogPaths.Length == 0)
        {
            yield break;
        }

        var reportVersions = reports
            .Select(report => report.Report.WorkflowVersion)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var resolvedCatalogPath in catalogPaths)
        {
            var catalog = ApiCatalogFile.Load(resolvedCatalogPath);
            var catalogDirectory = Path.GetDirectoryName(resolvedCatalogPath) ?? Directory.GetCurrentDirectory();
            var matchingVersions = catalog
                .Where(version => reportVersions.Count == 0 || reportVersions.Contains(version.Version))
                .ToArray();
            var configuredVersions = matchingVersions.Any(version => !string.IsNullOrWhiteSpace(version.BaselineSnapshot))
                ? matchingVersions
                : catalog;

            foreach (var version in configuredVersions)
            {
                if (string.IsNullOrWhiteSpace(version.BaselineSnapshot))
                {
                    continue;
                }

                yield return Path.IsPathRooted(version.BaselineSnapshot)
                    ? version.BaselineSnapshot
                    : Path.GetFullPath(Path.Combine(catalogDirectory, version.BaselineSnapshot));
            }
        }
    }

    private static IEnumerable<string> ResolveCandidateCatalogPaths(
        string? catalogPath,
        IReadOnlyList<ReportArtifact> reports)
    {
        if (!string.IsNullOrWhiteSpace(catalogPath))
        {
            yield return Path.GetFullPath(catalogPath);
            yield break;
        }

        var pathResolver = new CliPathResolver();
        foreach (var report in reports)
        {
            if (string.IsNullOrWhiteSpace(report.Report.WorkflowPath))
            {
                continue;
            }

            yield return pathResolver.ResolveDefaultCatalogPath(report.Report.WorkflowPath);
        }
    }

    private static IEnumerable<string> GetImplicitSnapshotDirectories(string reportPath)
    {
        var reportDirectory = Directory.Exists(reportPath)
            ? reportPath
            : Path.GetDirectoryName(reportPath);
        if (string.IsNullOrWhiteSpace(reportDirectory))
        {
            yield break;
        }

        yield return reportDirectory;

        var parentDirectory = Path.GetDirectoryName(reportDirectory);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            yield return Path.Combine(parentDirectory, "snapshots");
        }
    }

    private static void AddSnapshotFiles(string path, bool required, ISet<string> snapshotFiles)
    {
        if (File.Exists(path))
        {
            snapshotFiles.Add(path);

            return;
        }

        if (Directory.Exists(path))
        {
            foreach (var snapshotFile in Directory.GetFiles(path, SnapshotFilePattern, SearchOption.TopDirectoryOnly)
                         .OrderBy(file => Path.GetFileName(file), StringComparer.Ordinal))
            {
                snapshotFiles.Add(snapshotFile);
            }

            return;
        }

        if (required)
        {
            throw new FileNotFoundException("Snapshot path was not found.", path);
        }
    }

    private static async Task<ReportArtifact> LoadReportAsync(string path, CancellationToken cancellationToken)
    {
        var rawJson = await File.ReadAllTextAsync(path, cancellationToken);
        var report = JsonSerializer.Deserialize<WorkflowExecutionReport>(
                         rawJson,
                         new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidOperationException($"Deserialization returned null for '{path}'.");

        return new ReportArtifact(
            Path.GetFullPath(path),
            Path.GetFileName(path),
            rawJson,
            report);
    }

    private static async Task<SnapshotArtifact> LoadSnapshotAsync(string path, CancellationToken cancellationToken)
    {
        var rawJson = await File.ReadAllTextAsync(path, cancellationToken);
        var snapshot = JsonSerializer.Deserialize<WorkflowExecutionSnapshot>(
                           rawJson,
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                       ?? throw new InvalidOperationException($"Deserialization returned null for '{path}'.");

        return new SnapshotArtifact(
            Path.GetFullPath(path),
            Path.GetFileName(path),
            rawJson,
            snapshot);
    }

    private static string GetDefaultOutputDirectory(string requestedPath, string selectedReportPath)
    {
        if (Directory.Exists(requestedPath))
        {
            return requestedPath;
        }

        return Path.GetDirectoryName(selectedReportPath) ?? ".";
    }

    private static string BuildOutputBaseName(string requestedPath, string selectedReportPath)
    {
        if (Directory.Exists(requestedPath))
        {
            return $"{Path.GetFileName(requestedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))}.reports";
        }

        var baseName = Path.GetFileNameWithoutExtension(selectedReportPath);
        if (baseName.EndsWith(".workflow.report", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^".workflow.report".Length];
        }

        return baseName;
    }

    private sealed record ReportArtifact(
        string Path,
        string FileName,
        string RawJson,
        WorkflowExecutionReport Report) : ExecutionReportHtmlArtifact(Path, FileName, RawJson, Report);

    private sealed record SnapshotArtifact(
        string Path,
        string FileName,
        string RawJson,
        WorkflowExecutionSnapshot Snapshot) : ExecutionSnapshotHtmlArtifact(Path, FileName, RawJson, Snapshot);
}

internal record ExecutionReportHtmlArtifact(
    string Path,
    string FileName,
    string RawJson,
    WorkflowExecutionReport Report);

internal record ExecutionSnapshotHtmlArtifact(
    string Path,
    string FileName,
    string RawJson,
    WorkflowExecutionSnapshot Snapshot);
