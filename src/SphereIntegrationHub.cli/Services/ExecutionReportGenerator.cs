using System.Diagnostics;
using System.Text.Json;

using SphereIntegrationHub.cli;

namespace SphereIntegrationHub.Services;

internal sealed class ExecutionReportGenerator
{
    private readonly ICliOutputProvider _output;

    public ExecutionReportGenerator(ICliOutputProvider output)
    {
        _output = output;
    }

    public async Task<int> GenerateAndOpenAsync(InlineArguments args, CancellationToken cancellationToken)
    {
        var path = args.ExecutionReportPath!;

        if (!File.Exists(path))
        {
            _output.Error.WriteLine($"Execution report not found: {path}");
            return 1;
        }

        string rawJson;
        WorkflowExecutionReport report;
        try
        {
            rawJson = await File.ReadAllTextAsync(path, cancellationToken);
            report = JsonSerializer.Deserialize<WorkflowExecutionReport>(
                         rawJson,
                         new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                     ?? throw new InvalidOperationException("Deserialization returned null.");
        }
        catch (Exception ex)
        {
            _output.Error.WriteLine($"Failed to read execution report: {ex.Message}");
            return 1;
        }

        var outputDir = args.ReportOutputPath ?? Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        Directory.CreateDirectory(outputDir);

        var baseName = Path.GetFileNameWithoutExtension(path);
        if (baseName.EndsWith(".workflow.report", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^".workflow.report".Length];
        var htmlPath = Path.Combine(outputDir, $"{baseName}.workflow.report.html");

        var html = BuildHtml(rawJson);
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

    private static string BuildHtml(string reportJson)
    {
        return $$"""
<!doctype html>
<html lang="en" data-theme="light">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Workflow Trace</title>
<style>
/* ── CSS Variables ─────────────────────────────────────────────── */
:root {
  --bg:#f5f7fa;--surface:#ffffff;--border:#e2e8f0;--border-strong:#cbd5e1;
  --text:#1e293b;--text-muted:#64748b;--text-subtle:#94a3b8;
  --banner-bg:#0f172a;--banner-text:#f1f5f9;--banner-muted:#475569;--banner-sep:#334155;
  --header-bg:#f8fafc;--row-hover:#f0f9ff;--row-selected:#dbeafe;--row-border:#f1f5f9;
  --detail-bg:#eff6ff;--detail-border:#bfdbfe;
  --code-bg:#f8fafc;--code-border:#e2e8f0;--code-text:#475569;
  --btn-bg:#1e293b;--btn-c:#94a3b8;--btn-border:#334155;--btn-hover-bg:#334155;--btn-hover-c:#f1f5f9;
  --chip-total-bg:#eef2ff;--chip-total-c:#4338ca;--chip-total-b:#c7d2fe;
  --chip-ok-bg:#f0fdf4;--chip-ok-c:#15803d;--chip-ok-b:#bbf7d0;
  --chip-err-bg:#fff1f2;--chip-err-c:#be123c;--chip-err-b:#fecdd3;
  --chip-skip-bg:#fefce8;--chip-skip-c:#a16207;--chip-skip-b:#fef08a;
  --chip-mock-bg:#faf5ff;--chip-mock-c:#7c3aed;--chip-mock-b:#ddd6fe;
  --chip-retry-bg:#fff7ed;--chip-retry-c:#c2410c;--chip-retry-b:#fed7aa;
}
[data-theme="dark"] {
  --bg:#0f172a;--surface:#1e293b;--border:#334155;--border-strong:#475569;
  --text:#f1f5f9;--text-muted:#94a3b8;--text-subtle:#64748b;
  --banner-bg:#020617;--banner-text:#f1f5f9;--banner-muted:#64748b;--banner-sep:#1e293b;
  --header-bg:#1e293b;--row-hover:#172554;--row-selected:#1e3a5f;--row-border:#1e293b;
  --detail-bg:#172554;--detail-border:#1e40af;
  --code-bg:#0f172a;--code-border:#334155;--code-text:#94a3b8;
  --btn-bg:#334155;--btn-c:#94a3b8;--btn-border:#475569;--btn-hover-bg:#475569;--btn-hover-c:#f1f5f9;
  --chip-total-bg:#1e1b4b;--chip-total-c:#a5b4fc;--chip-total-b:#312e81;
  --chip-ok-bg:#052e16;--chip-ok-c:#4ade80;--chip-ok-b:#14532d;
  --chip-err-bg:#4c0519;--chip-err-c:#fda4af;--chip-err-b:#881337;
  --chip-skip-bg:#422006;--chip-skip-c:#fcd34d;--chip-skip-b:#713f12;
  --chip-mock-bg:#2e1065;--chip-mock-c:#c4b5fd;--chip-mock-b:#4c1d95;
  --chip-retry-bg:#431407;--chip-retry-c:#fdba74;--chip-retry-b:#7c2d12;
}
/* ── Reset ─────────────────────────────────────────────────────── */
*,*::before,*::after{box-sizing:border-box}
body{margin:0;font-family:ui-sans-serif,system-ui,-apple-system,BlinkMacSystemFont,"Segoe UI",Roboto,sans-serif;font-size:13px;background:var(--bg);color:var(--text);display:flex;flex-direction:column;height:100vh;overflow:hidden;transition:background .2s,color .2s}
/* ── Banner ─────────────────────────────────────────────────────── */
.banner{background:var(--banner-bg);color:var(--banner-text);padding:10px 16px;display:flex;align-items:center;gap:10px;flex-shrink:0}
.banner-brand{font-size:11px;font-weight:700;letter-spacing:.1em;text-transform:uppercase;color:var(--banner-muted);white-space:nowrap;flex-shrink:0}
.banner-sep{color:var(--banner-sep);flex-shrink:0;font-size:16px;line-height:1}
.banner-title{margin:0;font-size:14px;font-weight:600;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.result-ok{color:#4ade80}
.result-error{color:#f87171}
.result-running{color:#60a5fa}
.btn{background:var(--btn-bg);color:var(--btn-c);border:1px solid var(--btn-border);padding:5px 11px;border-radius:6px;cursor:pointer;font-size:12px;font-family:inherit;white-space:nowrap;transition:background .15s,color .15s;display:inline-flex;align-items:center;gap:5px}
.btn:hover{background:var(--btn-hover-bg);color:var(--btn-hover-c)}
.btn-icon{padding:5px 9px;font-size:14px}
#file-input{display:none}
/* ── Meta bar ────────────────────────────────────────────────────── */
.meta-bar{background:var(--surface);border-bottom:1px solid var(--border);padding:6px 16px;display:flex;gap:16px;align-items:center;flex-shrink:0;font-size:11.5px;color:var(--text-muted);flex-wrap:wrap;transition:background .2s}
.meta-bar strong{color:var(--text);font-weight:600}
/* ── Chips ───────────────────────────────────────────────────────── */
.chips{display:flex;gap:5px;padding:7px 16px;background:var(--surface);border-bottom:1px solid var(--border);flex-shrink:0;flex-wrap:wrap;transition:background .2s}
.chip{padding:2px 9px;border-radius:999px;font-size:11px;font-weight:600;border:1px solid}
.chip-total{background:var(--chip-total-bg);color:var(--chip-total-c);border-color:var(--chip-total-b)}
.chip-ok{background:var(--chip-ok-bg);color:var(--chip-ok-c);border-color:var(--chip-ok-b)}
.chip-error{background:var(--chip-err-bg);color:var(--chip-err-c);border-color:var(--chip-err-b)}
.chip-skipped{background:var(--chip-skip-bg);color:var(--chip-skip-c);border-color:var(--chip-skip-b)}
.chip-mocked{background:var(--chip-mock-bg);color:var(--chip-mock-c);border-color:var(--chip-mock-b)}
.chip-retries{background:var(--chip-retry-bg);color:var(--chip-retry-c);border-color:var(--chip-retry-b)}
/* ── Trace ───────────────────────────────────────────────────────── */
.trace-container{flex:1;display:flex;flex-direction:column;overflow:hidden;min-height:0}
.trace-header{display:flex;background:var(--header-bg);border-bottom:1px solid var(--border-strong);flex-shrink:0;transition:background .2s}
.trace-left-header{width:320px;min-width:320px;padding:6px 12px;font-weight:700;border-right:1px solid var(--border-strong);font-size:10.5px;color:var(--text-subtle);text-transform:uppercase;letter-spacing:.5px}
.trace-right-header{flex:1;position:relative;padding:6px 0}
.ruler{display:flex;justify-content:space-between;padding:0 8px;font-size:10.5px;color:var(--text-subtle)}
.trace-rows{flex:1;overflow-y:auto;overflow-x:hidden}
.trace-rows::-webkit-scrollbar{width:6px}
.trace-rows::-webkit-scrollbar-track{background:transparent}
.trace-rows::-webkit-scrollbar-thumb{background:var(--border-strong);border-radius:3px}
/* ── Rows ────────────────────────────────────────────────────────── */
.trace-row{display:flex;height:34px;border-bottom:1px solid var(--row-border);cursor:pointer;transition:background .1s}
.trace-row:hover{background:var(--row-hover)}
.trace-row.selected{background:var(--row-selected)}
.trace-left{width:320px;min-width:320px;padding:0 8px;display:flex;align-items:center;gap:5px;overflow:hidden;border-right:1px solid var(--border)}
.wf-tag{font-size:10px;color:var(--text-subtle);background:var(--header-bg);border:1px solid var(--border);border-radius:4px;padding:1px 4px;white-space:nowrap;flex-shrink:0;max-width:70px;overflow:hidden;text-overflow:ellipsis;transition:background .2s}
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
.stage-lbl{font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1;color:var(--text)}
.stage-lbl.s-error{color:#ef4444}
.stage-lbl.s-skipped{color:var(--text-subtle)}
.trace-right{flex:1;position:relative;overflow:hidden}
.span-bar{position:absolute;height:18px;top:8px;border-radius:4px;min-width:3px;display:flex;align-items:center;padding:0 5px;font-size:10px;color:rgba(255,255,255,.9);white-space:nowrap;overflow:hidden;cursor:pointer;transition:filter .1s}
.span-bar:hover{filter:brightness(1.12)}
.bar-ok{background:#22c55e}
.bar-error{background:#ef4444}
.bar-skipped{background:#94a3b8;color:var(--text-muted)}
.bar-running{background:#3b82f6}
.bar-mocked{background:#8b5cf6}
/* ── Detail panel ────────────────────────────────────────────────── */
.detail-panel{border-top:2px solid #3b82f6;background:var(--surface);overflow-y:auto;flex-shrink:0;transition:background .2s}
.detail-panel.hidden{display:none}
.detail-header{background:var(--detail-bg);padding:8px 16px;display:flex;gap:16px;align-items:baseline;border-bottom:1px solid var(--detail-border);flex-wrap:wrap;transition:background .2s}
.detail-title{font-weight:700;font-size:14px;color:var(--text)}
.detail-meta{color:var(--text-muted);font-size:12px}
.detail-close{margin-left:auto;cursor:pointer;color:var(--text-subtle);font-size:18px;line-height:1;padding:0 4px;flex-shrink:0;transition:color .1s}
.detail-close:hover{color:var(--text)}
.detail-body{padding:12px 16px;display:grid;grid-template-columns:1fr 1fr;gap:12px}
.detail-section h3{margin:0 0 6px;font-size:10px;text-transform:uppercase;color:var(--text-subtle);letter-spacing:.6px;border-bottom:1px solid var(--border);padding-bottom:4px;font-weight:700}
.kv{display:flex;flex-direction:column;gap:3px}
.kv-row{display:flex;gap:8px;align-items:baseline}
.kv-k{color:var(--text-muted);min-width:110px;flex-shrink:0;font-size:11px}
.kv-v{color:var(--text);word-break:break-all;font-size:12px}
.code-block{background:var(--code-bg);border:1px solid var(--code-border);border-radius:6px;padding:7px 10px;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:11px;color:var(--code-text);white-space:pre-wrap;word-break:break-word;max-height:130px;overflow-y:auto;margin-top:2px;transition:background .2s,border-color .2s}
.full-width{grid-column:1/-1}
.badge{padding:2px 8px;border-radius:999px;font-size:10px;font-weight:700;letter-spacing:.02em}
.b-ok{background:var(--chip-ok-bg);color:var(--chip-ok-c)}
.b-error{background:var(--chip-err-bg);color:var(--chip-err-c)}
.b-skipped{background:var(--header-bg);color:var(--text-subtle);border:1px solid var(--border)}
.b-running{background:var(--chip-total-bg);color:var(--chip-total-c)}
.b-mocked{background:var(--chip-mock-bg);color:var(--chip-mock-c)}
.empty{color:var(--text-subtle);padding:40px;text-align:center;font-size:14px}
</style>
</head>
<body>

<div class="banner">
  <span class="banner-brand">Sphere</span>
  <span class="banner-sep">·</span>
  <h1 class="banner-title" id="banner-title">Loading&hellip;</h1>
  <label for="file-input" class="btn">&#128193; Load</label>
  <input type="file" id="file-input" accept=".json">
  <button class="btn btn-icon" id="theme-toggle" title="Toggle dark/light mode" onclick="toggleTheme()">🌙</button>
</div>
<div class="meta-bar" id="meta-bar"></div>
<div class="chips" id="chips"></div>

<div class="trace-container">
  <div class="trace-header">
    <div class="trace-left-header">Service &amp; Operation</div>
    <div class="trace-right-header">
      <div class="ruler" id="ruler"></div>
    </div>
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
const _initialData = {{reportJson}};
let _report = null;
let _selectedIdx = -1;

/* ── Theme ─────────────────────────────────────────────────────── */
function applyTheme(dark) {
  document.documentElement.dataset.theme = dark ? 'dark' : 'light';
  document.getElementById('theme-toggle').textContent = dark ? '☀️' : '🌙';
}
function toggleTheme() {
  const dark = document.documentElement.dataset.theme !== 'dark';
  localStorage.setItem('sphere-theme', dark ? 'dark' : 'light');
  applyTheme(dark);
}
(function initTheme() {
  const saved = localStorage.getItem('sphere-theme');
  const prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
  applyTheme(saved ? saved === 'dark' : prefersDark);
})();

/* ── Utilities ─────────────────────────────────────────────────── */
function fmtMs(ms) {
  if (ms == null) return '';
  if (ms >= 1000) return (ms / 1000).toFixed(2) + 's';
  return Math.round(ms) + 'ms';
}
function fmtDate(iso) {
  if (!iso) return '';
  try { return new Date(iso).toLocaleString(); } catch { return iso; }
}
function esc(s) {
  if (s == null) return '';
  return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;');
}
function statusCls(s) {
  switch ((s||'').toLowerCase()) {
    case 'ok': return 'ok';
    case 'error': return 'error';
    case 'skipped': return 'skipped';
    default: return 'running';
  }
}
function barCls(stage) {
  if (stage.Mocked) return 'bar-mocked';
  return 'bar-' + statusCls(stage.Status);
}
function badgeCls(s) { return 'badge b-' + statusCls(s); }
function methodCls(m) {
  if (!m) return '';
  return 'm-' + m.toLowerCase();
}
function kv(k, v) {
  if (!v && v !== 0) return '';
  return `<div class="kv-row"><span class="kv-k">${k}</span><span class="kv-v">${v}</span></div>`;
}
function tryPrettyJson(s) {
  if (!s) return '';
  try { return JSON.stringify(JSON.parse(s), null, 2); } catch { return s; }
}

/* ── Render ────────────────────────────────────────────────────── */
function render(report) {
  _report = report;
  _selectedIdx = -1;
  closeDetail();

  const totalMs = Math.max(report.DurationMs || 1, 1);
  const startTs = new Date(report.StartedAtUtc).getTime();
  const stages  = report.Stages || [];
  const m       = report.Metrics || {};

  // Banner
  const rCls = 'result-' + statusCls(report.Result);
  document.getElementById('banner-title').innerHTML =
    `<span style="color:var(--banner-muted)">${esc(report.WorkflowName)}</span>`+
    ` <span class="${rCls}">${esc(report.ExecutionId)}</span>`;
  document.title = `${report.WorkflowName} — ${report.ExecutionId}`;

  // Meta bar
  const maxDepth = stages.reduce((d, s) => Math.max(d, s.Depth || 0), 0);
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
  if (m.SkippedStages) chips += `<span class="chip chip-skipped">Skipped: ${m.SkippedStages}</span>`;
  if (m.MockedStages)  chips += `<span class="chip chip-mocked">Mocked: ${m.MockedStages}</span>`;
  if (m.TotalRetries)  chips += `<span class="chip chip-retries">Retries: ${m.TotalRetries}</span>`;
  if (m.HttpStages)    chips += `<span class="chip chip-total">HTTP: ${m.HttpStages}</span>`;
  document.getElementById('chips').innerHTML = chips;

  // Ruler (6 ticks)
  const ticks = 6;
  let ruler = '';
  for (let i = 0; i <= ticks; i++) ruler += `<span>${fmtMs(totalMs * i / ticks)}</span>`;
  document.getElementById('ruler').innerHTML = ruler;

  // Rows
  if (stages.length === 0) {
    document.getElementById('trace-rows').innerHTML = '<div class="empty">No stages recorded.</div>';
    return;
  }

  let rows = '';
  stages.forEach((stage, i) => {
    const stageTs  = new Date(stage.StartedAtUtc).getTime();
    const offsetMs = Math.max(0, stageTs - startTs);
    const durMs    = stage.DurationMs || 0;
    const offsetPct = Math.min((offsetMs / totalMs) * 100, 99.7).toFixed(2);
    const widthPct  = Math.max((durMs / totalMs) * 100, 0.25).toFixed(2);
    const indent    = (stage.Depth || 0) * 18;
    const method    = stage.HttpMethod || '';
    const lbl       = stage.Status === 'Error' ? 's-error' : stage.Status === 'Skipped' ? 's-skipped' : '';
    const bCls      = barCls(stage);

    rows +=
      `<div class="trace-row" data-idx="${i}" onclick="selectStage(${i})">`+
        `<div class="trace-left" style="padding-left:${8+indent}px">`+
          `<span class="wf-tag" title="${esc(stage.WorkflowName)}">${esc(stage.WorkflowName)}</span>`+
          (method ? `<span class="http-badge ${methodCls(method)}">${esc(method)}</span>` : '')+
          `<span class="stage-lbl ${lbl}" title="${esc(stage.StageName)}">${esc(stage.StageName)}</span>`+
        `</div>`+
        `<div class="trace-right">`+
          `<div class="span-bar ${bCls}" style="left:${offsetPct}%;width:${widthPct}%">`+
            (durMs >= 8 ? fmtMs(durMs) : '')+
          `</div>`+
        `</div>`+
      `</div>`;
  });

  document.getElementById('trace-rows').innerHTML = rows;
}

/* ── Stage detail ──────────────────────────────────────────────── */
function selectStage(idx) {
  if (!_report) return;
  const stage = (_report.Stages || [])[idx];
  if (!stage) return;

  document.querySelectorAll('.trace-row').forEach(r => r.classList.remove('selected'));
  const row = document.querySelector(`.trace-row[data-idx="${idx}"]`);
  if (row) { row.classList.add('selected'); row.scrollIntoView({block:'nearest'}); }
  _selectedIdx = idx;

  const startTs  = new Date(_report.StartedAtUtc).getTime();
  const stageTs  = new Date(stage.StartedAtUtc).getTime();
  const offsetMs = Math.max(0, stageTs - startTs);

  document.getElementById('detail-title').textContent = stage.StageName;
  document.getElementById('detail-meta').innerHTML =
    `Service: <strong>${esc(stage.WorkflowName)}</strong> &nbsp;|&nbsp; `+
    `Duration: <strong>${fmtMs(stage.DurationMs)}</strong> &nbsp;|&nbsp; `+
    `Start&nbsp;offset: <strong>${fmtMs(offsetMs)}</strong>`;

  const hasHttp = stage.RequestUri || stage.HttpStatusCode != null;
  const hasReqDetail = stage.RequestHeaders || stage.RequestBody;
  const hasResDetail = stage.ResponseHeaders || stage.ResponseBody;
  const hasOutput    = stage.Output && Object.keys(stage.Output).length > 0;

  let body =
    // Left: stage info
    `<div class="detail-section">`+
      `<h3>Stage</h3>`+
      `<div class="kv">`+
        kv('Status', `<span class="${badgeCls(stage.Status)}">${esc(stage.Status)}</span>`)+
        kv('Kind', esc(stage.StageKind))+
        kv('Workflow', esc(stage.WorkflowName))+
        (stage.RunIf    ? kv('Run if', esc(stage.RunIf)) : '')+
        (stage.JumpTarget ? kv('Jump to', esc(stage.JumpTarget)) : '')+
        (stage.RetryCount  ? kv('Retries', stage.RetryCount) : '')+
        (stage.DelaySeconds ? kv('Delay', stage.DelaySeconds + 's') : '')+
        (stage.Mocked ? kv('Mocked', '&#10003;') : '')+
        (stage.ErrorMessage ? kv('Error', `<span style="color:#ef4444">${esc(stage.ErrorMessage)}</span>`) : '')+
      `</div>`+
    `</div>`+
    // Right: HTTP
    (hasHttp
      ? `<div class="detail-section">`+
          `<h3>HTTP</h3>`+
          `<div class="kv">`+
            (stage.HttpMethod ? kv('Method', `<span class="http-badge ${methodCls(stage.HttpMethod)}">${esc(stage.HttpMethod)}</span>`) : '')+
            kv('URI', `<span style="word-break:break-all">${esc(stage.RequestUri)}</span>`)+
            (stage.HttpStatusCode != null ? kv('Status', stage.HttpStatusCode) : '')+
            (stage.EnsureMode ? kv('Ensure', esc(stage.EnsureMode) + (stage.EnsureStatus ? ' ' + esc(stage.EnsureStatus) : '')) : '')+
          `</div>`+
        `</div>`
      : `<div class="detail-section"></div>`);

  if (hasReqDetail) {
    body +=
      `<div class="detail-section">`+
        `<h3>Request Headers</h3>`+
        `<div class="code-block">${esc(stage.RequestHeaders ? JSON.stringify(stage.RequestHeaders, null, 2) : '')}</div>`+
      `</div>`+
      `<div class="detail-section">`+
        `<h3>Request Body</h3>`+
        `<div class="code-block">${esc(tryPrettyJson(stage.RequestBody))}</div>`+
      `</div>`;
  }

  if (hasResDetail) {
    body +=
      `<div class="detail-section">`+
        `<h3>Response Headers</h3>`+
        `<div class="code-block">${esc(stage.ResponseHeaders ? JSON.stringify(stage.ResponseHeaders, null, 2) : '')}</div>`+
      `</div>`+
      `<div class="detail-section">`+
        `<h3>Response Body</h3>`+
        `<div class="code-block">${esc(tryPrettyJson(stage.ResponseBody))}</div>`+
      `</div>`;
  }

  if (hasOutput) {
    body +=
      `<div class="detail-section full-width">`+
        `<h3>Stage Output</h3>`+
        `<div class="code-block">${esc(JSON.stringify(stage.Output, null, 2))}</div>`+
      `</div>`;
  }

  document.getElementById('detail-body').innerHTML = body;
  document.getElementById('detail-panel').classList.remove('hidden');
}

function closeDetail() {
  document.getElementById('detail-panel').classList.add('hidden');
  document.querySelectorAll('.trace-row').forEach(r => r.classList.remove('selected'));
  _selectedIdx = -1;
}

/* ── Load another file ─────────────────────────────────────────── */
document.getElementById('file-input').addEventListener('change', function(e) {
  const file = e.target.files[0];
  if (!file) return;
  const reader = new FileReader();
  reader.onload = function(ev) {
    try { render(JSON.parse(ev.target.result)); }
    catch(err) { alert('Invalid JSON: ' + err.message); }
  };
  reader.readAsText(file);
  this.value = '';
});

/* ── Boot ──────────────────────────────────────────────────────── */
render(_initialData);
</script>
</body>
</html>
""";
    }
}

