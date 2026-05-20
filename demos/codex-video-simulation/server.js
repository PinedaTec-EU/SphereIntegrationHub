import { createServer } from "node:http";
import { readFile } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL(".", import.meta.url));
const publicRoot = join(root, "public");
const artifactsRoot = join(root, "artifacts");
const port = Number(process.env.PORT ?? 8787);

const state = {
  accounts: new Map(),
  carts: new Map()
};

const mimeTypes = {
  ".html": "text/html; charset=utf-8",
  ".css": "text/css; charset=utf-8",
  ".js": "application/javascript; charset=utf-8",
  ".json": "application/json; charset=utf-8",
  ".workflow": "text/yaml; charset=utf-8",
  ".wfvars": "text/yaml; charset=utf-8",
  ".catalog": "text/yaml; charset=utf-8"
};

createServer(async (request, response) => {
  try {
    const url = new URL(request.url ?? "/", `http://${request.headers.host}`);

    if (url.pathname === "/health") {
      return sendJson(response, 200, { status: "ok" });
    }

    if (request.method === "POST" && url.pathname === "/api/accounts") {
      return createAccount(request, response);
    }

    const cartItemMatch = url.pathname.match(/^\/api\/carts\/([^/]+)\/items$/);
    if (request.method === "POST" && cartItemMatch) {
      return addCartItem(request, response, cartItemMatch[1]);
    }

    if (request.method === "POST" && url.pathname === "/api/workflow/run") {
      return runWorkflow(request, response);
    }

    if (url.pathname === "/api/state") {
      return sendJson(response, 200, snapshot());
    }

    if (request.method === "POST" && url.pathname === "/api/reset") {
      state.accounts.clear();
      state.carts.clear();
      return sendJson(response, 200, { status: "reset" });
    }

    const filePath = resolveStaticPath(url.pathname);
    const content = await readFile(filePath);
    response.writeHead(200, { "Content-Type": mimeTypes[extname(filePath)] ?? "text/plain; charset=utf-8" });
    response.end(content);
  } catch (error) {
    if (error.code === "ENOENT") {
      sendJson(response, 404, { error: "Not found" });
      return;
    }

    sendJson(response, 500, { error: error.message });
  }
}).listen(port, () => {
  console.log(`Codex video simulation running at http://localhost:${port}`);
});

function resolveStaticPath(pathname) {
  if (pathname === "/") {
    return join(publicRoot, "index.html");
  }

  if (pathname.startsWith("/artifacts/")) {
    const artifact = normalize(pathname.replace("/artifacts/", ""));
    return join(artifactsRoot, artifact);
  }

  return join(publicRoot, normalize(pathname));
}

async function createAccount(request, response) {
  const body = await readJson(request);
  const suffix = String(state.accounts.size + 1).padStart(4, "0");
  const account = {
    accountId: `acc_${suffix}`,
    cartId: `cart_${suffix}`,
    name: body.name,
    email: body.email,
    status: "active"
  };

  state.accounts.set(account.accountId, account);
  state.carts.set(account.cartId, { cartId: account.cartId, accountId: account.accountId, items: [] });
  sendJson(response, 201, account);
}

async function addCartItem(request, response, cartId) {
  const cart = state.carts.get(cartId);
  if (!cart) {
    sendJson(response, 404, { error: "Cart not found" });
    return;
  }

  const body = await readJson(request);
  if (body.accountId !== cart.accountId) {
    sendJson(response, 409, { error: "Cart does not belong to account" });
    return;
  }

  const item = {
    itemId: `item_${String(cart.items.length + 1).padStart(4, "0")}`,
    accountId: body.accountId,
    cartId,
    sku: body.sku,
    quantity: body.quantity,
    syncStatus: "linked"
  };

  cart.items.push(item);
  sendJson(response, 201, item);
}

async function runWorkflow(request, response) {
  const input = await readJson(request);
  const startedAt = new Date();
  const accountResponse = await simulatePost("/api/accounts", {
    name: input.accountName,
    email: input.email
  });
  const itemResponse = await simulatePost(`/api/carts/${accountResponse.body.cartId}/items`, {
    accountId: accountResponse.body.accountId,
    sku: input.sku,
    quantity: input.quantity
  });

  const finishedAt = new Date(startedAt.getTime() + 612);
  const report = {
    workflow: "create-account-and-add-cart-item",
    executionId: "01HVDEMOREPORT0000000001",
    status: "Succeeded",
    startedAt: startedAt.toISOString(),
    finishedAt: finishedAt.toISOString(),
    durationMs: 612,
    summary: {
      stages: 2,
      succeeded: 2,
      failed: 0,
      accountId: accountResponse.body.accountId,
      cartId: accountResponse.body.cartId,
      itemId: itemResponse.body.itemId,
      synchronized: accountResponse.body.accountId === itemResponse.body.accountId
    },
    stages: [
      {
        name: "create-account",
        kind: "Endpoint",
        method: "POST",
        endpoint: "/api/accounts",
        statusCode: accountResponse.status,
        durationMs: 238,
        output: {
          accountId: accountResponse.body.accountId,
          cartId: accountResponse.body.cartId
        }
      },
      {
        name: "add-cart-item",
        kind: "Endpoint",
        method: "POST",
        endpoint: `/api/carts/${accountResponse.body.cartId}/items`,
        statusCode: itemResponse.status,
        durationMs: 374,
        output: {
          itemId: itemResponse.body.itemId,
          synchronizedAccountId: itemResponse.body.accountId
        }
      }
    ]
  };

  sendJson(response, 200, report);
}

async function simulatePost(path, body) {
  if (path === "/api/accounts") {
    const suffix = String(state.accounts.size + 1).padStart(4, "0");
    const account = {
      accountId: `acc_${suffix}`,
      cartId: `cart_${suffix}`,
      name: body.name,
      email: body.email,
      status: "active"
    };
    state.accounts.set(account.accountId, account);
    state.carts.set(account.cartId, { cartId: account.cartId, accountId: account.accountId, items: [] });
    return { status: 201, body: account };
  }

  const cartId = path.match(/^\/api\/carts\/([^/]+)\/items$/)?.[1];
  const cart = state.carts.get(cartId);
  const item = {
    itemId: `item_${String(cart.items.length + 1).padStart(4, "0")}`,
    accountId: body.accountId,
    cartId,
    sku: body.sku,
    quantity: body.quantity,
    syncStatus: "linked"
  };
  cart.items.push(item);
  return { status: 201, body: item };
}

function snapshot() {
  return {
    accounts: Array.from(state.accounts.values()),
    carts: Array.from(state.carts.values())
  };
}

async function readJson(request) {
  const chunks = [];
  for await (const chunk of request) {
    chunks.push(chunk);
  }

  const rawBody = Buffer.concat(chunks).toString("utf8");
  return rawBody ? JSON.parse(rawBody) : {};
}

function sendJson(response, statusCode, payload) {
  response.writeHead(statusCode, { "Content-Type": "application/json; charset=utf-8" });
  response.end(JSON.stringify(payload, null, 2));
}
