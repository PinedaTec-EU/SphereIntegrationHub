#!/usr/bin/env node
'use strict';

// Punto de entrada para el MCP server.
// Claude Desktop u otros clientes MCP lo invocan así:
//   npx @pinedatec.eu/sphere-integration-hub   →  ejecuta sih-mcp (stdin/stdout JSON-RPC)

const path = require('path');
const { spawnSync } = require('child_process');

const bin = process.platform === 'win32'
  ? path.join(__dirname, 'bin', 'sih-mcp.exe')
  : path.join(__dirname, 'bin', 'sih-mcp');

const result = spawnSync(bin, process.argv.slice(2), { stdio: 'inherit' });

if (result.error) {
  if (result.error.code === 'ENOENT') {
    console.error(
      '[@pinedatec.eu/sphere-integration-hub] Binario "sih-mcp" no encontrado.\n' +
      'Reinstala el paquete: npm install @pinedatec.eu/sphere-integration-hub'
    );
  } else {
    console.error('[@pinedatec.eu/sphere-integration-hub]', result.error.message);
  }
  process.exit(1);
}

process.exit(result.status ?? 0);
