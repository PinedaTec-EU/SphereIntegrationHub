#!/usr/bin/env node
'use strict';

// Se ejecuta automáticamente después de `npm install`.
// Descarga el binario correcto para la plataforma actual desde GitHub Releases.

const https = require('https');
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');
const { execSync } = require('child_process');

const REPO = 'PinedaTec-EU/SphereIntegrationHub';
const VERSION = require('./package.json').version;
const BIN_DIR = path.join(__dirname, 'bin');

// ─── Mapeo plataforma → nombre de archivo en GitHub Releases ─────────────────
const PLATFORM_MAP = {
  linux:  'linux',
  darwin: 'darwin',
  win32:  'win32',
};

const ARCH_MAP = {
  x64:   'x64',
  arm64: 'arm64',
};

function getPlatformKey() {
  const os   = PLATFORM_MAP[process.platform];
  const arch = ARCH_MAP[process.arch];

  if (!os || !arch) {
    throw new Error(
      `Plataforma no soportada: ${process.platform}/${process.arch}.\n` +
      'Descarga el binario manualmente desde https://github.com/' + REPO + '/releases'
    );
  }

  return { os, arch };
}

function getArchiveName(os, arch) {
  return os === 'win32'
    ? `sphere-integration-hub-${os}-${arch}.zip`
    : `sphere-integration-hub-${os}-${arch}.tar.gz`;
}

function getDownloadUrl(archive) {
  return `https://github.com/${REPO}/releases/download/v${VERSION}/${archive}`;
}

// ─── HTTP redirect-aware download ────────────────────────────────────────────
function download(url, dest) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(dest);

    function get(currentUrl) {
      https.get(currentUrl, { headers: { 'User-Agent': 'sphere-integration-hub-installer' } }, (res) => {
        if (res.statusCode === 301 || res.statusCode === 302) {
          get(res.headers.location);
          return;
        }
        if (res.statusCode !== 200) {
          reject(new Error(`Descarga fallida con HTTP ${res.statusCode}: ${currentUrl}`));
          return;
        }
        res.pipe(file);
        file.on('finish', () => file.close(resolve));
      }).on('error', reject);
    }

    get(url);
  });
}

// ─── Extracción ───────────────────────────────────────────────────────────────
function extractTarGz(archivePath, destDir) {
  // tar está disponible en Linux, macOS y Windows 10+ (v1803+)
  execSync(`tar xzf "${archivePath}" -C "${destDir}"`, { stdio: 'inherit' });
}

function extractZip(archivePath, destDir) {
  // PowerShell disponible en Windows 10+
  execSync(
    `powershell -NoProfile -Command "Expand-Archive -Path '${archivePath}' -DestinationPath '${destDir}' -Force"`,
    { stdio: 'inherit' }
  );
}

// ─── Main ─────────────────────────────────────────────────────────────────────
async function main() {
  const { os, arch } = getPlatformKey();
  const archive = getArchiveName(os, arch);
  const url = getDownloadUrl(archive);
  const tmpArchive = path.join(BIN_DIR, archive);

  console.log(`[sphere-integration-hub] Instalando v${VERSION} para ${os}/${arch}...`);

  fs.mkdirSync(BIN_DIR, { recursive: true });

  // 1. Descargar
  console.log(`[sphere-integration-hub] Descargando ${url}`);
  await download(url, tmpArchive);

  // 2. Extraer
  console.log(`[sphere-integration-hub] Extrayendo...`);
  if (os === 'win32') {
    extractZip(tmpArchive, BIN_DIR);
  } else {
    extractTarGz(tmpArchive, BIN_DIR);
    // Asegurar permisos de ejecución
    for (const bin of ['sih', 'sih-mcp']) {
      const binPath = path.join(BIN_DIR, bin);
      if (fs.existsSync(binPath)) {
        fs.chmodSync(binPath, 0o755);
      }
    }
  }

  // 3. Limpiar archivo temporal
  fs.unlinkSync(tmpArchive);

  console.log('[sphere-integration-hub] Instalación completada.');
}

main().catch((err) => {
  console.error('[sphere-integration-hub] Error durante la instalación:', err.message);
  console.error('Puedes descargar el binario manualmente desde:');
  console.error(`  https://github.com/${REPO}/releases/tag/v${VERSION}`);
  // No fallar con exit 1 para no bloquear proyectos en entornos CI sin soporte
  process.exitCode = 1;
});
