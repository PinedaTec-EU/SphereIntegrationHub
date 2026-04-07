using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Text.Json;

using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Services;

internal sealed class ExecutionReportGenerator
{
    private const string ReportFilePattern = "*.workflow.report.json";
    private readonly ICliOutputProvider _output;

    public ExecutionReportGenerator(ICliOutputProvider output)
    {
        _output = output;
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
        try
        {
            reports = await LoadReportsAsync(fullPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _output.Error.WriteLine($"Failed to read execution report: {ex.Message}");
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
        var html = BuildHtml(reports, reports.Length - 1, appVersion);
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

    private static string BuildHtml(IReadOnlyList<ReportArtifact> reports, int initialReportIndex, string appVersion)
    {
        var reportsJson = JsonSerializer.Serialize(reports.Select(report => new
        {
            path = report.Path,
            fileName = report.FileName,
            executionId = report.Report.ExecutionId,
            workflowName = report.Report.WorkflowName,
            result = report.Report.Result,
            startedAtUtc = report.Report.StartedAtUtc,
            json = JsonSerializer.Deserialize<JsonElement>(report.RawJson)
        }));

        return $$"""
<!doctype html>
<html lang="en" data-theme="light">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Workflow Trace</title>
<style>
/* ── Variables ───────────────────────────────────────────────────── */
:root {
  --bg:#f5f7fa;--surface:#ffffff;--border:#e2e8f0;--border-strong:#cbd5e1;
  --text:#1e293b;--text-muted:#64748b;--text-subtle:#94a3b8;
  --banner-bg:#0f172a;--banner-text:#f1f5f9;--banner-muted:#475569;
  --header-bg:#f8fafc;--row-hover:#f0f9ff;--row-selected:#dbeafe;--row-border:#f1f5f9;
  --wf-row-bg:#f0f4ff;--wf-row-hover:#e0ebff;--wf-row-selected:#c7d9ff;
  --detail-bg:#eff6ff;--detail-border:#bfdbfe;
  --tree-guide:rgba(59,130,246,.32);
  --code-bg:#f8fafc;--code-border:#e2e8f0;--code-text:#475569;
  --btn-bg:#1e293b;--btn-c:#94a3b8;--btn-border:#334155;--btn-hover-bg:#334155;--btn-hover-c:#f1f5f9;
  --chip-total-bg:#eef2ff;--chip-total-c:#4338ca;--chip-total-b:#c7d2fe;
  --chip-ok-bg:#f0fdf4;--chip-ok-c:#15803d;--chip-ok-b:#bbf7d0;
  --chip-err-bg:#fff1f2;--chip-err-c:#be123c;--chip-err-b:#fecdd3;
  --chip-skip-bg:#fefce8;--chip-skip-c:#a16207;--chip-skip-b:#fef08a;
  --chip-mock-bg:#faf5ff;--chip-mock-c:#7c3aed;--chip-mock-b:#ddd6fe;
  --chip-retry-bg:#fff7ed;--chip-retry-c:#c2410c;--chip-retry-b:#fed7aa;
  --expand-c:#3b82f6;
}
[data-theme="dark"] {
  --bg:#0f172a;--surface:#1e293b;--border:#334155;--border-strong:#475569;
  --text:#f1f5f9;--text-muted:#94a3b8;--text-subtle:#64748b;
  --banner-bg:#020617;--banner-text:#f1f5f9;--banner-muted:#64748b;
  --header-bg:#1e293b;--row-hover:#172554;--row-selected:#1e3a5f;--row-border:#1e293b;
  --wf-row-bg:#1a2540;--wf-row-hover:#1e3060;--wf-row-selected:#1e3a6e;
  --detail-bg:#172554;--detail-border:#1e40af;
  --tree-guide:rgba(96,165,250,.34);
  --code-bg:#0f172a;--code-border:#334155;--code-text:#94a3b8;
  --btn-bg:#334155;--btn-c:#94a3b8;--btn-border:#475569;--btn-hover-bg:#475569;--btn-hover-c:#f1f5f9;
  --chip-total-bg:#1e1b4b;--chip-total-c:#a5b4fc;--chip-total-b:#312e81;
  --chip-ok-bg:#052e16;--chip-ok-c:#4ade80;--chip-ok-b:#14532d;
  --chip-err-bg:#4c0519;--chip-err-c:#fda4af;--chip-err-b:#881337;
  --chip-skip-bg:#422006;--chip-skip-c:#fcd34d;--chip-skip-b:#713f12;
  --chip-mock-bg:#2e1065;--chip-mock-c:#c4b5fd;--chip-mock-b:#4c1d95;
  --chip-retry-bg:#431407;--chip-retry-c:#fdba74;--chip-retry-b:#7c2d12;
  --expand-c:#60a5fa;
}
/* ── Reset ───────────────────────────────────────────────────────── */
*,*::before,*::after{box-sizing:border-box}
body{margin:0;font-family:ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif;font-size:13px;background:var(--bg);color:var(--text);display:flex;flex-direction:column;height:100vh;overflow:hidden;transition:background .2s,color .2s}
/* ── Banner ──────────────────────────────────────────────────────── */
.banner{background:var(--banner-bg);color:var(--banner-text);padding:10px 16px;display:flex;align-items:center;gap:10px;flex-shrink:0}
.banner-logo{width:28px;height:28px;flex-shrink:0}
.banner-brand{font-size:11px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:var(--banner-muted);white-space:nowrap;flex-shrink:0}
.banner-sep{color:#334155;flex-shrink:0;font-size:16px;line-height:1}
.banner-version{font-size:11px;font-weight:600;color:#4ade80;background:rgba(74,222,128,.12);border:1px solid rgba(74,222,128,.25);border-radius:4px;padding:1px 7px;white-space:nowrap;flex-shrink:0;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;letter-spacing:.02em}
.banner-title{margin:0;font-size:14px;font-weight:600;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.result-ok{color:#4ade80}.result-error{color:#f87171}.result-running{color:#60a5fa}
.btn{background:var(--btn-bg);color:var(--btn-c);border:1px solid var(--btn-border);padding:5px 11px;border-radius:6px;cursor:pointer;font-size:12px;font-family:inherit;white-space:nowrap;transition:background .15s,color .15s;display:inline-flex;align-items:center;gap:5px}
.btn:hover{background:var(--btn-hover-bg);color:var(--btn-hover-c)}
.btn-icon{padding:5px 9px;font-size:14px}
.report-picker{max-width:360px;min-width:180px;background:var(--btn-bg);color:var(--btn-c);border:1px solid var(--btn-border);padding:5px 11px;border-radius:6px;font-size:12px;font-family:inherit}
.report-picker:hover{color:var(--btn-hover-c)}
#file-input{display:none}
/* ── Meta / chips ────────────────────────────────────────────────── */
.meta-bar{background:var(--surface);border-bottom:1px solid var(--border);padding:6px 16px;display:flex;gap:16px;align-items:center;flex-shrink:0;font-size:11.5px;color:var(--text-muted);flex-wrap:wrap;transition:background .2s}
.meta-bar strong{color:var(--text);font-weight:600}
.chips{display:flex;gap:5px;padding:7px 16px;background:var(--surface);border-bottom:1px solid var(--border);flex-shrink:0;flex-wrap:wrap;transition:background .2s}
.chip{padding:2px 9px;border-radius:999px;font-size:11px;font-weight:600;border:1px solid}
.chip-total{background:var(--chip-total-bg);color:var(--chip-total-c);border-color:var(--chip-total-b)}
.chip-ok{background:var(--chip-ok-bg);color:var(--chip-ok-c);border-color:var(--chip-ok-b)}
.chip-error{background:var(--chip-err-bg);color:var(--chip-err-c);border-color:var(--chip-err-b)}
.chip-skipped{background:var(--chip-skip-bg);color:var(--chip-skip-c);border-color:var(--chip-skip-b)}
.chip-mocked{background:var(--chip-mock-bg);color:var(--chip-mock-c);border-color:var(--chip-mock-b)}
.chip-retries{background:var(--chip-retry-bg);color:var(--chip-retry-c);border-color:var(--chip-retry-b)}
/* ── Trace layout ────────────────────────────────────────────────── */
.trace-container{flex:1;display:flex;flex-direction:column;overflow:hidden;min-height:0}
.trace-header{display:flex;background:var(--header-bg);border-bottom:1px solid var(--border-strong);flex-shrink:0;transition:background .2s}
.trace-left-header{width:380px;min-width:380px;padding:6px 12px;font-weight:700;border-right:1px solid var(--border-strong);font-size:10.5px;color:var(--text-subtle);text-transform:uppercase;letter-spacing:.5px}
.trace-status-header{width:64px;min-width:64px;padding:6px 8px;font-weight:700;border-right:1px solid var(--border-strong);font-size:10.5px;color:var(--text-subtle);text-transform:uppercase;letter-spacing:.5px;text-align:center;flex-shrink:0}
.trace-right-header{flex:1;position:relative;padding:6px 0}
.ruler{display:flex;justify-content:space-between;padding:0 8px;font-size:10.5px;color:var(--text-subtle)}
.trace-rows{flex:1;overflow-y:auto;overflow-x:hidden}
.trace-rows::-webkit-scrollbar{width:6px}
.trace-rows::-webkit-scrollbar-track{background:transparent}
.trace-rows::-webkit-scrollbar-thumb{background:var(--border-strong);border-radius:3px}
/* ── Rows ────────────────────────────────────────────────────────── */
.trace-row{display:flex;height:44px;border-bottom:1px solid var(--row-border);cursor:pointer;transition:background .1s}
.trace-row:hover{background:var(--row-hover)}
.trace-row.selected{background:var(--row-selected)}
.trace-row.wf-row{background:var(--wf-row-bg)}
.trace-row.wf-row:hover{background:var(--wf-row-hover)}
.trace-row.wf-row.selected{background:var(--wf-row-selected)}
.trace-row.is-skipped{opacity:.92}
/* left cell */
.trace-left{width:380px;min-width:380px;padding:4px 8px;display:flex;flex-direction:column;justify-content:center;gap:1px;overflow:hidden;border-right:1px solid var(--border);position:relative}
.trace-left-top{display:flex;align-items:center;gap:5px;overflow:hidden;width:100%}
.tree-indent{height:18px;flex-shrink:0;position:relative;pointer-events:none}
.tree-indent::before{content:'';position:absolute;top:-10px;bottom:-10px;left:0;right:0;background-image:repeating-linear-gradient(90deg,transparent 0 17px,var(--tree-guide) 17px 18px);opacity:.9}
.tree-indent::after{content:'';position:absolute;top:50%;right:0;width:12px;border-top:1px solid var(--tree-guide);transform:translateY(-50%)}
/* expand chevron */
.expand-btn{width:18px;min-width:18px;height:18px;display:flex;align-items:center;justify-content:center;flex-shrink:0;color:var(--expand-c);font-size:11px;font-weight:700;transition:transform .18s;line-height:1;border-radius:3px;background:rgba(99,102,241,.12);border:1px solid rgba(99,102,241,.22)}
.expand-btn:hover{background:rgba(99,102,241,.22)}
.expand-btn.open{transform:rotate(90deg)}
.expand-placeholder{width:18px;min-width:18px;flex-shrink:0}
/* wf-tag, method badges */
.wf-tag{font-size:10px;color:var(--text-subtle);background:var(--header-bg);border:1px solid var(--border);border-radius:4px;padding:1px 4px;white-space:nowrap;flex-shrink:0;max-width:80px;overflow:hidden;text-overflow:ellipsis;transition:background .2s}
.http-badge{font-size:10px;font-weight:700;padding:1px 5px;border-radius:4px;flex-shrink:0;letter-spacing:.02em}
.m-get{background:#dcfce7;color:#15803d}
.m-post{background:#dbeafe;color:#1d4ed8}
.m-put{background:#ffedd5;color:#c2410c}
.m-patch{background:#ede9fe;color:#6d28d9}
.m-delete{background:#ffe4e6;color:#be123c}
[data-theme="dark"] .m-get{background:#14532d;color:#4ade80}
[data-theme="dark"] .m-post{background:#1e3a8a;color:#93c5fd}
[data-theme="dark"] .m-put{background:#7c2d12;color:#fdba74}
[data-theme="dark"] .m-patch{background:#4c1d95;color:#c4b5fd}
[data-theme="dark"] .m-delete{background:#881337;color:#fda4af}
.wf-kind-badge{font-size:10px;font-weight:700;padding:1px 5px;border-radius:4px;flex-shrink:0;background:var(--chip-total-bg);color:var(--chip-total-c);letter-spacing:.02em}
/* labels */
.stage-lbl{font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1;color:var(--text);font-weight:500}
.stage-lbl.s-error{color:#ef4444}
.stage-lbl.s-skipped{color:var(--text-subtle)}
.stage-state{font-size:10px;font-weight:700;letter-spacing:.02em;color:var(--chip-skip-c);background:var(--chip-skip-bg);border:1px solid var(--chip-skip-b);border-radius:4px;padding:1px 5px;flex-shrink:0}
.stage-uri{font-size:10.5px;color:var(--text-subtle);overflow:hidden;text-overflow:ellipsis;white-space:nowrap;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;padding-left:var(--uri-indent,22px)}
/* status column */
.trace-status{width:64px;min-width:64px;display:flex;align-items:center;justify-content:center;border-right:1px solid var(--border);flex-shrink:0}
.http-status{font-size:11px;font-weight:700;padding:2px 6px;border-radius:4px;font-family:ui-monospace,SFMono-Regular,Menlo,monospace}
.s-2xx{background:var(--chip-ok-bg);color:var(--chip-ok-c)}
.s-3xx{background:var(--chip-total-bg);color:var(--chip-total-c)}
.s-4xx{background:var(--chip-err-bg);color:var(--chip-err-c)}
.s-5xx{background:#fef2f2;color:#991b1b}
[data-theme="dark"] .s-5xx{background:#4a0d0d;color:#fca5a5}
/* timeline bar */
.trace-right{flex:1;position:relative;overflow:hidden}
.span-bar{position:absolute;height:18px;top:13px;border-radius:4px;min-width:3px;display:flex;align-items:center;padding:0 5px;font-size:10px;color:rgba(255,255,255,.9);white-space:nowrap;overflow:hidden;cursor:pointer;transition:filter .1s}
.span-bar:hover{filter:brightness(1.12)}
.bar-ok{background:#22c55e}.bar-error{background:#ef4444}
.bar-skipped{background:repeating-linear-gradient(135deg,#94a3b8 0 8px,#cbd5e1 8px 16px);color:#334155;border:1px dashed #64748b}
.bar-running{background:#3b82f6}.bar-mocked{background:#8b5cf6}
.bar-workflow{background:linear-gradient(90deg,#6366f1,#8b5cf6)}
/* children group */
.children-group{display:none;border-left:2px solid var(--expand-c);margin-left:0}
.children-group.open{display:block}
/* ── Detail panel ────────────────────────────────────────────────── */
.detail-panel{border-top:2px solid #3b82f6;background:var(--surface);overflow-y:auto;flex-shrink:0;max-height:340px;transition:background .2s}
.detail-panel.hidden{display:none}
.detail-header{background:var(--detail-bg);padding:8px 16px;display:flex;gap:12px;align-items:center;border-bottom:1px solid var(--detail-border);flex-wrap:wrap;transition:background .2s}
.detail-title{font-weight:700;font-size:14px;color:var(--text);flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.detail-meta{color:var(--text-muted);font-size:12px;display:flex;align-items:center;gap:8px;flex-wrap:wrap}
.detail-close{cursor:pointer;color:var(--text-subtle);font-size:18px;line-height:1;padding:0 4px;flex-shrink:0;transition:color .1s}
.detail-close:hover{color:var(--text)}
.detail-body{padding:12px 16px;display:grid;grid-template-columns:1fr 1fr;gap:10px}
.detail-section h3{margin:0 0 6px;font-size:10px;text-transform:uppercase;color:var(--text-subtle);letter-spacing:.6px;border-bottom:1px solid var(--border);padding-bottom:4px;font-weight:700}
.kv{display:flex;flex-direction:column;gap:3px}
.kv-row{display:flex;gap:8px;align-items:baseline}
.kv-k{color:var(--text-muted);min-width:100px;flex-shrink:0;font-size:11px}
.kv-v{color:var(--text);word-break:break-all;font-size:12px}
.code-block{background:var(--code-bg);border:1px solid var(--code-border);border-radius:6px;padding:7px 10px;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:11px;color:var(--code-text);white-space:pre-wrap;word-break:break-word;max-height:140px;overflow-y:auto;margin-top:2px;transition:background .2s,border-color .2s}
.full-width{grid-column:1/-1}
.http-req-bar{display:flex;align-items:center;gap:10px;background:var(--code-bg);border:1px solid var(--code-border);border-radius:8px;padding:9px 14px;flex-wrap:wrap;transition:background .2s,border-color .2s}
.http-req-uri{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:12px;color:var(--text-muted);flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;min-width:80px}
.http-req-status-pill{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:13px;font-weight:700;white-space:nowrap;padding:2px 8px;border-radius:5px}
.http-req-dur{font-size:11px;color:var(--text-subtle);white-space:nowrap}
.badge{padding:2px 8px;border-radius:999px;font-size:10px;font-weight:700;letter-spacing:.02em}
.b-ok{background:var(--chip-ok-bg);color:var(--chip-ok-c)}
.b-error{background:var(--chip-err-bg);color:var(--chip-err-c)}
.b-skipped{background:var(--header-bg);color:var(--text-subtle);border:1px solid var(--border)}
.b-running{background:var(--chip-total-bg);color:var(--chip-total-c)}
.b-mocked{background:var(--chip-mock-bg);color:var(--chip-mock-c)}
.output-kv{display:grid;grid-template-columns:auto 1fr;gap:4px 16px}
.out-k{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:11.5px;color:var(--text-muted);white-space:nowrap}
.out-v{font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:11.5px;color:var(--text);word-break:break-all}
.empty{color:var(--text-subtle);padding:40px;text-align:center;font-size:14px}
</style>
</head>
<body>

<div class="banner">
  {{ReportBranding.HeaderLogoSvg.Replace("class=\"header-logo\"", "class=\"banner-logo\"")}}
  <span class="banner-brand">{{ReportBranding.HeaderTitle}}</span>
  <span class="banner-sep">·</span>
  <span class="banner-version" id="banner-version">v{{appVersion}}</span>
  <h1 class="banner-title" id="banner-title">Loading&hellip;</h1>
  <select class="report-picker" id="report-picker" title="Select execution"></select>
  <label for="file-input" class="btn">&#128193; Load</label>
  <input type="file" id="file-input" accept=".json">
  <button class="btn btn-icon" id="theme-toggle" title="Toggle dark/light mode" onclick="toggleTheme()">🌙</button>
</div>
<div class="meta-bar" id="meta-bar"></div>
<div class="chips" id="chips"></div>

<div class="trace-container">
  <div class="trace-header">
    <div class="trace-left-header">Workflow · Stage</div>
    <div class="trace-status-header">HTTP</div>
    <div class="trace-right-header"><div class="ruler" id="ruler"></div></div>
  </div>
  <div class="trace-rows" id="trace-rows"></div>
</div>

<div class="detail-panel hidden" id="detail-panel">
  <div class="detail-header">
    <span class="detail-title" id="detail-title"></span>
    <span class="detail-meta" id="detail-meta"></span>
    <span class="detail-close" onclick="closeDetail()" title="Close">&#10005;</span>
  </div>
  <div class="detail-body" id="detail-body"></div>
</div>

<script>
const _reports = {{reportsJson}};
const _initialReportIndex = {{initialReportIndex}};
let _report   = null;
let _tree     = null;   // root nodes
let _expanded = new Set();  // indices of expanded workflow rows
let _selected = -1;

/* ── Theme ───────────────────────────────────────────────────────── */
function applyTheme(dark) {
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  document.getElementById('theme-toggle').textContent = dark ? '☀️' : '🌙';
}
function toggleTheme() {
  const dark = document.documentElement.dataset.theme !== 'dark';
  localStorage.setItem('sphere-theme', dark ? 'dark' : 'light');
  applyTheme(dark);
}
(function() {
  const saved = localStorage.getItem('sphere-theme');
  const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  applyTheme(saved ? saved === 'dark' : prefersDark);
})();

/* ── Utilities ───────────────────────────────────────────────────── */
function fmtMs(ms) {
  if (ms == null) return '';
  if (ms >= 1000) return (ms / 1000).toFixed(2) + 's';
  return Math.round(ms) + 'ms';
}
function fmtDate(iso) {
  if (!iso) return '';
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}
function fmtDateShort(iso) {
  if (!iso) return '';
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}
function esc(s) {
  if (s == null) return '';
  return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function statusCls(s) {
  switch ((s||'').toLowerCase()) {
    case 'ok': return 'ok'; case 'error': return 'error';
    case 'skipped': return 'skipped'; default: return 'running';
  }
}
function statusText(s) {
  switch ((s||'').toLowerCase()) {
    case 'skipped': return 'Not executed';
    default: return s || 'Running';
  }
}
function badgeCls(s) { return 'badge b-' + statusCls(s); }
function methodCls(m) { return m ? 'm-' + m.toLowerCase() : ''; }
function httpRangeCls(code) {
  if (code == null) return '';
  if (code >= 200 && code < 300) return 's-2xx';
  if (code >= 300 && code < 400) return 's-3xx';
  if (code >= 400 && code < 500) return 's-4xx';
  if (code >= 500) return 's-5xx';
  return '';
}
function barCls(stage) {
  if (statusCls(stage.Status) === 'skipped') return 'bar-skipped';
  if (stage.Mocked) return 'bar-mocked';
  if (isWfKind(stage)) return 'bar-workflow';
  return 'bar-' + statusCls(stage.Status);
}
function isWfKind(stage) {
  return (stage.StageKind || '').toLowerCase().includes('workflow');
}
function uriPath(uri) {
  if (!uri) return '';
  try { const u = new URL(uri); return u.pathname + (u.search || ''); }
  catch { return uri.length > 60 ? uri.slice(0,60)+'…' : uri; }
}
function tryPrettyJson(s) {
  if (!s) return '';
  try { return JSON.stringify(JSON.parse(s), null, 2); } catch { return s; }
}
function fmtVal(v) {
  if (v == null) return '—';
  if (typeof v === 'object') { const s = JSON.stringify(v); return s.length > 160 ? s.slice(0,160)+'…' : s; }
  const s = String(v); return s.length > 160 ? s.slice(0,160)+'…' : s;
}
function kv(k, v) {
  if (!v && v !== 0) return '';
  return `<div class="kv-row"><span class="kv-k">${k}</span><span class="kv-v">${v}</span></div>`;
}
function reportOptionLabel(entry) {
  const workflow = entry.workflowName || 'Workflow';
  const execution = entry.executionId || entry.fileName || 'Execution';
  const startedAt = fmtDateShort(entry.startedAtUtc);
  return startedAt ? `${workflow} · ${execution} · ${startedAt}` : `${workflow} · ${execution}`;
}

/* ── Tree builder ────────────────────────────────────────────────── */
// A stage's parent is the nearest preceding stage with lower Depth.
function buildTree(stages) {
  const nodes = stages.map((stage, idx) => ({ stage, idx, children: [] }));
  const roots = [];
  for (let i = 0; i < nodes.length; i++) {
    const d = nodes[i].stage.Depth || 0;
    if (d === 0) { roots.push(nodes[i]); continue; }
    for (let j = i - 1; j >= 0; j--) {
      if ((nodes[j].stage.Depth || 0) < d) { nodes[j].children.push(nodes[i]); break; }
    }
  }
  return roots;
}

/* ── Render ──────────────────────────────────────────────────────── */
function render(report) {
  _report   = report;
  _tree     = null;
  _expanded = new Set();
  _selected = -1;
  closeDetail();

  const stages  = report.Stages || [];
  const m       = report.Metrics || {};
  const totalMs = Math.max(report.DurationMs || 1, 1);

  // Banner
  const rCls = 'result-' + statusCls(report.Result);
  document.getElementById('banner-title').innerHTML =
    `<span style="color:var(--banner-muted)">${esc(report.WorkflowName)}</span>`+
    ` <span class="${rCls}">${esc(report.ExecutionId)}</span>`;
  document.title = `${report.WorkflowName} — ${report.ExecutionId}`;

  // Meta bar
  const maxDepth = stages.reduce((d,s) => Math.max(d, s.Depth||0), 0);
  document.getElementById('meta-bar').innerHTML =
    `<span>Start: <strong>${fmtDate(report.StartedAtUtc)}</strong></span>`+
    `<span>Duration: <strong>${fmtMs(totalMs)}</strong></span>`+
    `<span>Environment: <strong>${esc(report.Environment)}</strong></span>`+
    `<span>Version: <strong>${esc(report.WorkflowVersion)}</strong></span>`+
    `<span>Stages: <strong>${m.TotalStages ?? stages.length}</strong></span>`+
    `<span>Depth: <strong>${maxDepth}</strong></span>`+
    `<span>Status: <strong><span class="${badgeCls(report.Result)}">${esc(report.Result)}</span></strong></span>`;

  // Chips
  let chips =
    `<span class="chip chip-total">Total: ${m.TotalStages ?? stages.length}</span>`+
    `<span class="chip chip-ok">Executed: ${m.ExecutedStages ?? 0}</span>`;
  if (m.FailedStages)  chips += `<span class="chip chip-error">Failed: ${m.FailedStages}</span>`;
  if (m.SkippedStages) chips += `<span class="chip chip-skipped">Not executed: ${m.SkippedStages}</span>`;
  if (m.MockedStages)  chips += `<span class="chip chip-mocked">Mocked: ${m.MockedStages}</span>`;
  if (m.TotalRetries)  chips += `<span class="chip chip-retries">Retries: ${m.TotalRetries}</span>`;
  if (m.HttpStages)    chips += `<span class="chip chip-total">HTTP: ${m.HttpStages}</span>`;
  document.getElementById('chips').innerHTML = chips;

  // Ruler
  const ticks = 6;
  let ruler = '';
  for (let i = 0; i <= ticks; i++) ruler += `<span>${fmtMs(totalMs * i / ticks)}</span>`;
  document.getElementById('ruler').innerHTML = ruler;

  // Build tree and render
  const startTs = new Date(report.StartedAtUtc).getTime();
  _tree = buildTree(stages);
  renderTree(_tree, totalMs, startTs);
}

function loadReportByIndex(index) {
  const safeIndex = Math.max(0, Math.min(index, _reports.length - 1));
  const entry = _reports[safeIndex];
  if (!entry) return;
  const picker = document.getElementById('report-picker');
  if (picker) picker.value = String(safeIndex);
  render(entry.json);
}

function initReportPicker() {
  const picker = document.getElementById('report-picker');
  if (!picker) return;
  if (_reports.length <= 1) {
    picker.style.display = 'none';
    return;
  }

  picker.innerHTML = _reports.map((entry, index) =>
    `<option value="${index}">${esc(reportOptionLabel(entry))}</option>`).join('');
  picker.addEventListener('change', e => loadReportByIndex(Number(e.target.value)));
}

/* ── Tree renderer ───────────────────────────────────────────────── */
function renderTree(roots, totalMs, startTs) {
  if (!roots || roots.length === 0) {
    document.getElementById('trace-rows').innerHTML = '<div class="empty">No stages recorded.</div>';
    return;
  }
  document.getElementById('trace-rows').innerHTML = buildRows(roots, totalMs, startTs);
}

function buildRows(nodes, totalMs, startTs) {
  let html = '';
  nodes.forEach(node => {
    html += buildRow(node, totalMs, startTs);
    if (node.children.length > 0) {
      const open = _expanded.has(node.idx);
      html += `<div class="children-group${open?' open':''}" id="cg-${node.idx}">`;
      html += buildRows(node.children, totalMs, startTs);
      html += `</div>`;
    }
  });
  return html;
}

function buildRow(node, totalMs, startTs) {
  const { stage, idx } = node;
  const hasChildren = node.children.length > 0;
  const isWf        = isWfKind(stage);
  const isOpen      = _expanded.has(idx);
  const depth       = Math.max(0, stage.Depth || 0);
  const indentPx    = depth * 18;
  const uriIndentPx = 22 + indentPx;

  const stageTs   = new Date(stage.StartedAtUtc).getTime();
  const offsetMs  = Math.max(0, stageTs - startTs);
  const durMs     = stage.DurationMs || 0;
  const offsetPct = Math.min((offsetMs / totalMs) * 100, 99.5).toFixed(2);
  const widthPct  = Math.max((durMs   / totalMs) * 100, 0.25).toFixed(2);
  const method    = stage.HttpMethod || '';
  const path      = uriPath(stage.RequestUri);
  const sCls      = httpRangeCls(stage.HttpStatusCode);
  const lbl       = stage.Status === 'Error' ? 's-error' : stage.Status === 'Skipped' ? 's-skipped' : '';
  const bCls      = barCls(stage);
  const wfCls     = isWf ? ' wf-row' : '';
  const skippedCls = statusCls(stage.Status) === 'skipped' ? ' is-skipped' : '';
  const selCls    = _selected === idx ? ' selected' : '';

  let html = `<div class="trace-row${wfCls}${skippedCls}${selCls}" data-idx="${idx}" onclick="rowClick(${idx})">`;

  // Left cell
  html += `<div class="trace-left" style="--uri-indent:${uriIndentPx}px">`;
  html += `<div class="trace-left-top">`;
  if (depth > 0) {
    html += `<span class="tree-indent" style="width:${indentPx}px"></span>`;
  }
  // expand/collapse button or placeholder
  if (hasChildren) {
    html += `<span class="expand-btn${isOpen?' open':''}">&#9654;</span>`;
  } else if (isWf) {
    html += `<span class="expand-btn" style="opacity:.4;cursor:default">&#9654;</span>`;
  } else {
    html += `<span class="expand-placeholder"></span>`;
  }
  html += `<span class="wf-tag" title="${esc(stage.WorkflowName)}">${esc(stage.WorkflowName)}</span>`;
  if (isWf) {
    html += `<span class="wf-kind-badge">WF</span>`;
  } else if (method) {
    html += `<span class="http-badge ${methodCls(method)}">${esc(method)}</span>`;
  }
  html += `<span class="stage-lbl ${lbl}" title="${esc(stage.StageName)}">${esc(stage.StageName)}</span>`;
  if (statusCls(stage.Status) === 'skipped') {
    html += `<span class="stage-state">Not executed</span>`;
  }
  html += `</div>`;
  if (path) html += `<span class="stage-uri" title="${esc(stage.RequestUri)}">${esc(path)}</span>`;
  else if (statusCls(stage.Status) === 'skipped') html += `<span class="stage-uri">Condition not met or skipped before execution.</span>`;
  html += `</div>`;

  // Status cell
  html += `<div class="trace-status">`;
  if (stage.HttpStatusCode != null) {
    html += `<span class="http-status ${sCls}">${stage.HttpStatusCode}</span>`;
  } else if (isWf || statusCls(stage.Status) === 'skipped') {
    html += `<span class="${badgeCls(stage.Status)}" style="font-size:9px">${esc(statusText(stage.Status))}</span>`;
  }
  html += `</div>`;

  // Timeline bar
  html += `<div class="trace-right">`;
  html += `<div class="span-bar ${bCls}" style="left:${offsetPct}%;width:${widthPct}%">`;
  if (statusCls(stage.Status) === 'skipped') html += `Not executed`;
  else if (durMs >= 8) html += fmtMs(durMs);
  html += `</div></div>`;

  html += `</div>`;
  return html;
}

/* ── Row click: expand workflow or show/hide detail ─────────────── */
function rowClick(idx) {
  if (!_report) return;
  const node = findNode(_tree, idx);
  if (!node) return;
  const hasChildren = node.children.length > 0;

  // Workflow rows keep their detail open; repeated clicks only toggle children.
  if (_selected === idx && !hasChildren) {
    closeDetail();
    return;
  }

  if (hasChildren) {
    // Toggle expand children
    const wasOpen = _expanded.has(idx);
    if (wasOpen) _expanded.delete(idx); else _expanded.add(idx);
    const cg = document.getElementById('cg-' + idx);
    if (cg) cg.classList.toggle('open', !wasOpen);
    const domRow = document.querySelector(`.trace-row[data-idx="${idx}"]`);
    if (domRow) {
      const btn = domRow.querySelector('.expand-btn');
      if (btn) btn.classList.toggle('open', !wasOpen);
    }

    if (_selected === idx) {
      return;
    }
  }

  showDetail(idx);
}

function findNode(nodes, idx) {
  for (const n of nodes) {
    if (n.idx === idx) return n;
    const found = findNode(n.children, idx);
    if (found) return found;
  }
  return null;
}

/* ── Detail panel ────────────────────────────────────────────────── */
function showDetail(idx) {
  const stage = (_report.Stages || [])[idx];
  if (!stage) return;

  // Highlight selected row
  document.querySelectorAll('.trace-row').forEach(r => r.classList.remove('selected'));
  const row = document.querySelector(`.trace-row[data-idx="${idx}"]`);
  if (row) { row.classList.add('selected'); row.scrollIntoView({block:'nearest'}); }
  _selected = idx;

  const startTs  = new Date(_report.StartedAtUtc).getTime();
  const stageTs  = new Date(stage.StartedAtUtc).getTime();
  const offsetMs = Math.max(0, stageTs - startTs);
  const sCls     = httpRangeCls(stage.HttpStatusCode);
  const method   = stage.HttpMethod || '';

  document.getElementById('detail-title').textContent = stage.StageName;
  // Meta line
  let meta = `<span class="${badgeCls(stage.Status)}">${esc(statusText(stage.Status))}</span>`;
  meta += ` <span style="color:var(--text-subtle)">·</span> <strong>${esc(stage.WorkflowName)}</strong>`;
  meta += ` <span style="color:var(--text-subtle)">·</span> ${fmtMs(stage.DurationMs)}`;
  if (stage.RetryCount) meta += ` <span style="color:var(--chip-retry-c)">↺ ${stage.RetryCount}</span>`;
  if (stage.Mocked) meta += ` <span class="badge b-mocked">mock</span>`;
  document.getElementById('detail-meta').innerHTML = meta;

  const hasHttp   = !!(stage.RequestUri || stage.HttpMethod || stage.HttpStatusCode != null);
  const hasReq    = !!(stage.RequestHeaders || stage.RequestBody);
  const hasRes    = !!(stage.ResponseHeaders || stage.ResponseBody);
  const hasOutput = !!(stage.Output && Object.keys(stage.Output).length > 0);
  const hasWorkflowInputs = !!(stage.WorkflowInputs && Object.keys(stage.WorkflowInputs).length > 0);
  const hasWorkflowOutput = !!(stage.WorkflowOutput && Object.keys(stage.WorkflowOutput).length > 0);
  const hasWorkflowResult = !!(stage.WorkflowResult && Object.keys(stage.WorkflowResult).length > 0);

  let body = '';

  // HTTP request bar (full width)
  if (hasHttp) {
    body += `<div class="detail-section full-width"><h3>HTTP Request</h3>`;
    body += `<div class="http-req-bar">`;
    if (method) body += `<span class="http-badge ${methodCls(method)}">${esc(method)}</span>`;
    body += `<span class="http-req-uri" title="${esc(stage.RequestUri)}">${esc(stage.RequestUri||'—')}</span>`;
    if (stage.HttpStatusCode != null) body += `<span class="http-req-status-pill ${sCls}">${stage.HttpStatusCode}</span>`;
    if (stage.DurationMs) body += `<span class="http-req-dur">${fmtMs(stage.DurationMs)}</span>`;
    if (stage.EnsureMode) body += `<span class="http-req-dur">ensure: ${esc(stage.EnsureMode)}${stage.EnsureStatus?' → '+esc(stage.EnsureStatus):''}</span>`;
    body += `</div></div>`;
  }

  // Stage info + Execution side by side
  body += `<div class="detail-section"><h3>Stage</h3><div class="kv">`;
  body += kv('Kind', esc(stage.StageKind));
  if (stage.RunIf)       body += kv('Run if',  esc(stage.RunIf));
  if (stage.JumpTarget)  body += kv('Jump to', esc(stage.JumpTarget));
  if (stage.DelaySeconds)body += kv('Delay',   stage.DelaySeconds+'s');
  if (stage.ErrorMessage)body += kv('Error', `<span style="color:#ef4444">${esc(stage.ErrorMessage)}</span>`);
  body += `</div></div>`;

  body += `<div class="detail-section"><h3>Execution</h3><div class="kv">`;
  body += kv('Executed', statusCls(stage.Status) === 'skipped' ? 'No' : 'Yes');
  body += kv('Start offset', fmtMs(offsetMs));
  body += kv('Duration', fmtMs(stage.DurationMs));
  if (stage.RetryCount) body += kv('Retries', stage.RetryCount);
  body += `</div></div>`;

  if (hasWorkflowResult) {
    body += `<div class="detail-section"><h3>Workflow Result</h3><div class="output-kv">`;
    Object.entries(stage.WorkflowResult).forEach(([k,v]) => {
      body += `<span class="out-k">${esc(k)}</span><span class="out-v">${esc(fmtVal(v))}</span>`;
    });
    body += `</div></div>`;
  }

  // Request / Response
  if (hasReq) {
    body += `<div class="detail-section"><h3>Request Headers</h3><div class="code-block">${esc(stage.RequestHeaders ? JSON.stringify(stage.RequestHeaders,null,2):'')}</div></div>`;
    body += `<div class="detail-section"><h3>Request Body</h3><div class="code-block">${esc(tryPrettyJson(stage.RequestBody))}</div></div>`;
  }
  if (hasRes) {
    body += `<div class="detail-section"><h3>Response Headers</h3><div class="code-block">${esc(stage.ResponseHeaders ? JSON.stringify(stage.ResponseHeaders,null,2):'')}</div></div>`;
    body += `<div class="detail-section"><h3>Response Body</h3><div class="code-block">${esc(tryPrettyJson(stage.ResponseBody))}</div></div>`;
  }

  // Output (key-value, full width)
  if (hasOutput) {
    body += `<div class="detail-section full-width"><h3>Stage Output</h3><div class="output-kv">`;
    Object.entries(stage.Output).forEach(([k,v]) => {
      body += `<span class="out-k">${esc(k)}</span><span class="out-v">${esc(fmtVal(v))}</span>`;
    });
    body += `</div></div>`;
  }

  if (hasWorkflowInputs) {
    body += `<div class="detail-section full-width"><h3>Workflow Inputs</h3><div class="output-kv">`;
    Object.entries(stage.WorkflowInputs).forEach(([k,v]) => {
      body += `<span class="out-k">${esc(k)}</span><span class="out-v">${esc(fmtVal(v))}</span>`;
    });
    body += `</div></div>`;
  }

  if (hasWorkflowOutput) {
    body += `<div class="detail-section full-width"><h3>Workflow Output</h3><div class="output-kv">`;
    Object.entries(stage.WorkflowOutput).forEach(([k,v]) => {
      body += `<span class="out-k">${esc(k)}</span><span class="out-v">${esc(fmtVal(v))}</span>`;
    });
    body += `</div></div>`;
  }

  document.getElementById('detail-body').innerHTML = body;
  document.getElementById('detail-panel').classList.remove('hidden');
}

function closeDetail() {
  document.getElementById('detail-panel').classList.add('hidden');
  document.querySelectorAll('.trace-row').forEach(r => r.classList.remove('selected'));
  _selected = -1;
}

/* ── Load file ───────────────────────────────────────────────────── */
document.getElementById('file-input').addEventListener('change', function(e) {
  const file = e.target.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = ev => {
    try {
      _report = null;
      render(JSON.parse(ev.target.result));
    }
    catch(err) { alert('Invalid JSON: ' + err.message); }
  };
  reader.readAsText(file);
  this.value = '';
});

/* ── Boot ────────────────────────────────────────────────────────── */
initReportPicker();
loadReportByIndex(_initialReportIndex);
</script>
</body>
</html>
""";
    }

    private sealed record ReportArtifact(
        string Path,
        string FileName,
        string RawJson,
        WorkflowExecutionReport Report);
}
