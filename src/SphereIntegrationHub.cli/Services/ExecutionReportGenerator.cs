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
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Workflow Trace</title>
<style>
*,*::before,*::after{box-sizing:border-box}
body{margin:0;font-family:ui-monospace,SFMono-Regular,Menlo,monospace;font-size:13px;background:#fafafa;color:#111;display:flex;flex-direction:column;height:100vh;overflow:hidden}

/* Banner */
.banner{background:#111;color:#fff;padding:10px 16px;display:flex;align-items:center;gap:12px;flex-shrink:0}
.banner-title{margin:0;font-size:15px;font-weight:600;flex:1;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}
.banner-id{color:#888;font-weight:400}
.result-ok{color:#4caf50}
.result-error{color:#f44336}
.result-running{color:#64b5f6}
.btn{background:#2a2a2a;color:#ccc;border:1px solid #444;padding:5px 12px;border-radius:4px;cursor:pointer;font-size:12px;font-family:inherit;white-space:nowrap}
.btn:hover{background:#3a3a3a;color:#fff}
#file-input{display:none}

/* Meta bar */
.meta-bar{background:#fff;border-bottom:1px solid #ddd;padding:7px 16px;display:flex;gap:20px;align-items:center;flex-shrink:0;font-size:12px;color:#666;flex-wrap:wrap}
.meta-bar strong{color:#111}

/* Metrics chips */
.chips{display:flex;gap:6px;padding:7px 16px;background:#fff;border-bottom:1px solid #eee;flex-shrink:0;flex-wrap:wrap}
.chip{padding:2px 10px;border-radius:12px;font-size:11px;font-weight:600;border:1px solid}
.chip-total{background:#e8eaf6;color:#3949ab;border-color:#9fa8da}
.chip-ok{background:#e8f5e9;color:#2e7d32;border-color:#a5d6a7}
.chip-error{background:#ffebee;color:#c62828;border-color:#ef9a9a}
.chip-skipped{background:#fff8e1;color:#f57f17;border-color:#ffe082}
.chip-mocked{background:#f3e5f5;color:#6a1b9a;border-color:#ce93d8}
.chip-retries{background:#fbe9e7;color:#bf360c;border-color:#ffab91}

/* Trace area */
.trace-container{flex:1;display:flex;flex-direction:column;overflow:hidden;min-height:0}
.trace-header{display:flex;background:#f0f0f0;border-bottom:2px solid #ccc;flex-shrink:0}
.trace-left-header{width:320px;min-width:320px;padding:6px 12px;font-weight:600;border-right:1px solid #ccc;font-size:11px;color:#555;text-transform:uppercase;letter-spacing:.4px}
.trace-right-header{flex:1;position:relative;padding:6px 0}
.ruler{display:flex;justify-content:space-between;padding:0 8px;font-size:11px;color:#888}
.trace-rows{flex:1;overflow-y:auto;overflow-x:hidden}

/* Rows */
.trace-row{display:flex;height:34px;border-bottom:1px solid #f0f0f0;cursor:pointer;transition:background .1s}
.trace-row:hover{background:#f5f8ff}
.trace-row.selected{background:#e3f2fd}
.trace-left{width:320px;min-width:320px;padding:0 8px;display:flex;align-items:center;gap:5px;overflow:hidden;border-right:1px solid #eee}
.wf-tag{font-size:10px;color:#999;background:#f5f5f5;border:1px solid #e0e0e0;border-radius:3px;padding:1px 4px;white-space:nowrap;flex-shrink:0;max-width:70px;overflow:hidden;text-overflow:ellipsis}
.http-badge{font-size:10px;font-weight:700;padding:1px 4px;border-radius:3px;flex-shrink:0}
.m-get{background:#e8f5e9;color:#2e7d32}
.m-post{background:#e3f2fd;color:#1565c0}
.m-put{background:#fff3e0;color:#e65100}
.m-patch{background:#f3e5f5;color:#6a1b9a}
.m-delete{background:#ffebee;color:#c62828}
.stage-lbl{font-size:12px;overflow:hidden;text-overflow:ellipsis;white-space:nowrap;flex:1}
.stage-lbl.s-error{color:#c62828}
.stage-lbl.s-skipped{color:#bbb}
.trace-right{flex:1;position:relative;overflow:hidden}
.span-bar{position:absolute;height:20px;top:7px;border-radius:3px;min-width:3px;display:flex;align-items:center;padding:0 5px;font-size:10px;color:rgba(255,255,255,.9);white-space:nowrap;overflow:hidden;cursor:pointer;transition:opacity .1s}
.span-bar:hover{opacity:.85}
.bar-ok{background:#43a047}
.bar-error{background:#e53935}
.bar-skipped{background:#bdbdbd;color:#555}
.bar-running{background:#1e88e5}
.bar-mocked{background:#8e24aa}

/* Detail panel */
.detail-panel{border-top:2px solid #1976d2;background:#fff;overflow-y:auto;flex-shrink:0;transition:max-height .2s}
.detail-panel.hidden{display:none}
.detail-header{background:#e3f2fd;padding:8px 16px;display:flex;gap:16px;align-items:baseline;border-bottom:1px solid #bbdefb;flex-wrap:wrap}
.detail-title{font-weight:700;font-size:14px}
.detail-meta{color:#555;font-size:12px}
.detail-close{margin-left:auto;cursor:pointer;color:#777;font-size:18px;line-height:1;padding:0 4px;flex-shrink:0}
.detail-close:hover{color:#111}
.detail-body{padding:12px 16px;display:grid;grid-template-columns:1fr 1fr;gap:12px}
.detail-section{}
.detail-section h3{margin:0 0 6px;font-size:10px;text-transform:uppercase;color:#999;letter-spacing:.5px;border-bottom:1px solid #eee;padding-bottom:3px}
.kv{display:flex;flex-direction:column;gap:3px}
.kv-row{display:flex;gap:8px;align-items:baseline}
.kv-k{color:#888;min-width:110px;flex-shrink:0;font-size:11px}
.kv-v{color:#111;word-break:break-all;font-size:12px}
.code-block{background:#f5f5f5;border:1px solid #e8e8e8;border-radius:4px;padding:6px 8px;font-size:11px;white-space:pre-wrap;word-break:break-word;max-height:130px;overflow-y:auto;margin-top:2px}
.full-width{grid-column:1/-1}
.badge{padding:2px 7px;border-radius:3px;font-size:10px;font-weight:700}
.b-ok{background:#e8f5e9;color:#2e7d32}
.b-error{background:#ffebee;color:#c62828}
.b-skipped{background:#fafafa;color:#999;border:1px solid #ddd}
.b-running{background:#e3f2fd;color:#1565c0}
.b-mocked{background:#f3e5f5;color:#6a1b9a}
.empty{color:#bbb;padding:40px;text-align:center;font-size:14px}
</style>
</head>
<body>

<div class="banner">
  <h1 class="banner-title" id="banner-title">Loading&hellip;</h1>
  <label for="file-input" class="btn">&#128193; Load execution</label>
  <input type="file" id="file-input" accept=".json">
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
    `<span style="color:#777">${esc(report.WorkflowName)}</span>`+
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
        (stage.ErrorMessage ? kv('Error', `<span style="color:#c62828">${esc(stage.ErrorMessage)}</span>`) : '')+
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

