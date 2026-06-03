#!/usr/bin/env node
'use strict';

// Se ejecuta automáticamente después de `npm install`.
// Descarga el binario correcto para la plataforma actual desde GitHub Releases.

const https = require('https');
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const REPO = 'PinedaTec-EU/SphereIntegrationHub';
const PACKAGE = require('./package.json');
const VERSION = PACKAGE.version;
const RELEASE_VERSION = PACKAGE.sihReleaseVersion || null;
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
    ? `sih-${os}-${arch}.zip`
    : `sih-${os}-${arch}.tar.gz`;
}

function getDownloadUrl(releaseVersion, archive) {
  return `https://github.com/${REPO}/releases/download/v${releaseVersion}/${archive}`;
}

function parseBuildTag(tagName, packageVersion) {
  const match = new RegExp(`^v${packageVersion}\\.(\\d+)$`).exec(tagName);

  if (!match) {
    return null;
  }

  return {
    build: Number.parseInt(match[1], 10),
    tagName,
  };
}

function readJson(url) {
  return new Promise((resolve, reject) => {
    function get(currentUrl) {
      https.get(currentUrl, { headers: { 'User-Agent': '@pinedatec.eu/sphere-integration-hub-installer' } }, (res) => {
        if (res.statusCode === 301 || res.statusCode === 302) {
          get(res.headers.location);
          return;
        }

        if (res.statusCode !== 200) {
          reject(new Error(`Solicitud JSON fallida con HTTP ${res.statusCode}: ${currentUrl}`));
          return;
        }

        const chunks = [];
        res.on('data', (chunk) => chunks.push(chunk));
        res.on('end', () => {
          try {
            resolve(JSON.parse(Buffer.concat(chunks).toString('utf8')));
          } catch (error) {
            reject(error);
          }
        });
      }).on('error', reject);
    }

    get(url);
  });
}

async function resolveReleaseVersionAsync(packageVersion, explicitReleaseVersion, fetchJson = readJson) {
  if (explicitReleaseVersion) {
    return explicitReleaseVersion;
  }

  const releases = await fetchJson(`https://api.github.com/repos/${REPO}/releases?per_page=100`);
  const matchingTag = releases
    .map((release) => parseBuildTag(release.tag_name, packageVersion))
    .filter(Boolean)
    .sort((left, right) => right.build - left.build)[0];

  return matchingTag ? matchingTag.tagName.slice(1) : packageVersion;
}

// ─── HTTP redirect-aware download ────────────────────────────────────────────
function download(url, dest) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(dest);

    function get(currentUrl) {
      https.get(currentUrl, { headers: { 'User-Agent': '@pinedatec.eu/sphere-integration-hub-installer' } }, (res) => {
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
  const releaseVersion = await resolveReleaseVersionAsync(VERSION, RELEASE_VERSION);
  const url = getDownloadUrl(releaseVersion, archive);
  const tmpArchive = path.join(BIN_DIR, archive);

  console.log(`[@pinedatec.eu/sphere-integration-hub] Instalando npm v${VERSION} desde release v${releaseVersion} para ${os}/${arch}...`);

  fs.mkdirSync(BIN_DIR, { recursive: true });

  // 1. Descargar
  console.log(`[@pinedatec.eu/sphere-integration-hub] Descargando ${url}`);
  await download(url, tmpArchive);

  // 2. Extraer
  console.log(`[@pinedatec.eu/sphere-integration-hub] Extrayendo...`);
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

  console.log('[@pinedatec.eu/sphere-integration-hub] Instalación completada.');
}

if (require.main === module) {
  main().catch((err) => {
    console.error('[@pinedatec.eu/sphere-integration-hub] Error durante la instalación:', err.message);
    console.error('Puedes descargar el binario manualmente desde:');
    console.error(`  https://github.com/${REPO}/releases`);
    // No fallar con exit 1 para no bloquear proyectos en entornos CI sin soporte
    process.exitCode = 1;
  });
}

module.exports = {
  getArchiveName,
  getDownloadUrl,
  parseBuildTag,
  resolveReleaseVersionAsync,
};
