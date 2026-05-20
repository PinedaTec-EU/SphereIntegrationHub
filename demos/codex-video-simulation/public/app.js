const artifacts = {
  workflow: "/artifacts/create-account-and-cart.workflow",
  wfvars: "/artifacts/create-account-and-cart.wfvars",
  catalog: "/artifacts/api.catalog"
};

const snippets = {};
const chatLog = document.querySelector("#chat-log");
const terminal = document.querySelector("#terminal");
const codeView = document.querySelector("#code-view code");
const codeTitle = document.querySelector("#code-title");
const codeBadge = document.querySelector("#code-badge");
const reportView = document.querySelector("#report-view");
const playButton = document.querySelector("#play-button");
const resetButton = document.querySelector("#reset-button");
const files = document.querySelectorAll(".file");

let playbackRunning = false;

await loadArtifacts();
reset();

playButton.addEventListener("click", () => {
  if (!playbackRunning) {
    play();
  }
});

resetButton.addEventListener("click", reset);

files.forEach((button) => {
  button.addEventListener("click", () => showFile(button.dataset.file));
});

async function loadArtifacts() {
  for (const [key, path] of Object.entries(artifacts)) {
    snippets[key] = await fetch(path).then((response) => response.text());
  }
}

async function reset() {
  playbackRunning = false;
  await fetch("/api/reset", { method: "POST" }).catch(() => {});
  document.body.classList.remove("report-mode");
  chatLog.innerHTML = "";
  terminal.textContent = "$ npm start\nCodex video simulation running at http://localhost:8787\n";
  reportView.className = "report-empty";
  reportView.innerHTML = "<strong>No run yet</strong><span>The workflow report appears here after execution.</span>";
  showFile("workflow");
}

async function play() {
  playbackRunning = true;
  playButton.textContent = "Playing...";
  reset();
  playbackRunning = true;

  await say("user", "Necesito una demo corta: crea una API con dos endpoints, uno para crear una cuenta y otro para añadir un artículo a la cesta. Después genera el workflow de Sphere Integration Hub que sincronice ambos pasos.");
  await wait(800);
  await say("assistant", "Voy a montar una API local de comercio con POST /api/accounts y POST /api/carts/{cartId}/items. El segundo paso usará el accountId y cartId que devuelve el primero para verificar la sincronización.");
  await terminalLine("$ node server.js");
  await terminalLine("API ready: POST /api/accounts");
  await terminalLine("API ready: POST /api/carts/{cartId}/items");

  await say("user", "Ahora crea el workflow en Sphere Integration Hub y sus variables de prueba.");
  await wait(700);
  showFile("workflow");
  await say("assistant", "He generado create-account-and-add-cart-item.workflow. Tiene dos stages Endpoint: create-account publica la cuenta y add-cart-item consume el cartId + accountId devueltos por el primer stage.");
  await terminalLine("$ sih draft --from-openapi commerce-demo --goal \"create account and add cart item\"");
  await terminalLine("created artifacts/create-account-and-cart.workflow");
  await terminalLine("created artifacts/create-account-and-cart.wfvars");

  await wait(600);
  showFile("wfvars");
  await say("user", "Pruébalo con datos de ejemplo y enséñame el report.");
  await terminalLine("$ sih run artifacts/create-account-and-cart.workflow --vars artifacts/create-account-and-cart.wfvars --report-format both");

  const report = await fetch("/api/workflow/run", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      accountName: "Contoso Retail",
      email: "ops@contoso.example",
      sku: "SKU-EDGE-42",
      quantity: 2
    })
  }).then((response) => response.json());

  await terminalLine(`POST /api/accounts -> 201 accountId=${report.summary.accountId}`);
  await terminalLine(`POST /api/carts/${report.summary.cartId}/items -> 201 itemId=${report.summary.itemId}`);
  await terminalLine("workflow status: Succeeded");
  await terminalLine("report written: create-account-and-add-cart-item.workflow.report.html");

  renderReport(report);
  showFile("report");
  await say("assistant", "La ejecución terminó correctamente. El report confirma dos stages OK, accountId sincronizado en el alta de artículo y salida final con accountId, cartId e itemId.");

  playButton.textContent = "Play rehearsal";
  playbackRunning = false;
}

async function say(role, text) {
  const element = document.createElement("div");
  element.className = `message ${role}`;
  chatLog.appendChild(element);
  await typeInto(element, text, 10);
  chatLog.scrollTop = chatLog.scrollHeight;
}

async function terminalLine(text) {
  await wait(220);
  terminal.textContent += `${text}\n`;
  terminal.scrollTop = terminal.scrollHeight;
}

async function typeInto(element, text, speed) {
  for (const char of text) {
    element.textContent += char;
    await wait(speed);
  }
}

function showFile(file) {
  files.forEach((button) => button.classList.toggle("active", button.dataset.file === file));

  if (file === "report") {
    codeTitle.textContent = "create-account-and-add-cart-item.workflow.report.html";
    codeBadge.textContent = "result";
    codeView.textContent = "Open the report panel to inspect stage timing, HTTP statuses, and synchronized outputs.";
    return;
  }

  const titles = {
    workflow: "create-account-and-cart.workflow",
    wfvars: "create-account-and-cart.wfvars",
    catalog: "api.catalog"
  };
  codeTitle.textContent = titles[file];
  codeBadge.textContent = file === "workflow" ? "draft" : "supporting";
  codeView.textContent = snippets[file];
}

function renderReport(report) {
  document.body.classList.add("report-mode");
  reportView.className = "report";
  reportView.innerHTML = `
    <div class="report-header">
      <div>
        <h2>${report.workflow}</h2>
        <div>${report.executionId} · ${report.durationMs} ms</div>
      </div>
      <div class="success-pill">${report.status}</div>
    </div>
    <div class="metrics">
      <div class="metric"><span>Stages</span><strong>${report.summary.stages}</strong></div>
      <div class="metric"><span>Account</span><strong>${report.summary.accountId}</strong></div>
      <div class="metric"><span>Cart</span><strong>${report.summary.cartId}</strong></div>
      <div class="metric"><span>Synced</span><strong>${report.summary.synchronized ? "true" : "false"}</strong></div>
    </div>
    <div class="timeline">
      ${report.stages.map((stage) => `
        <div class="stage-row">
          <strong>${stage.name}</strong>
          <div class="bar"><span style="width: ${Math.max(25, stage.durationMs / 5)}%"></span></div>
          <span>${stage.statusCode}</span>
        </div>
      `).join("")}
    </div>
  `;
}

function wait(ms) {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}
