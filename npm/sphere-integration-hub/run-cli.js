#!/usr/bin/env node
'use strict';

const path = require('path');
const { spawnSync } = require('child_process');

const bin = process.platform === 'win32'
  ? path.join(__dirname, 'bin', 'sih.exe')
  : path.join(__dirname, 'bin', 'sih');

const result = spawnSync(bin, process.argv.slice(2), { stdio: 'inherit' });

if (result.error) {
  if (result.error.code === 'ENOENT') {
    console.error(
      '[@pinedatec.eu/sphere-integration-hub] Binario "sih" no encontrado.\n' +
      'Reinstala el paquete: npm install @pinedatec.eu/sphere-integration-hub'
    );
  } else {
    console.error('[@pinedatec.eu/sphere-integration-hub]', result.error.message);
  }
  process.exit(1);
}

process.exit(result.status ?? 0);
