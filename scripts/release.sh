#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# release.sh
#
# Release local completo: compila binarios, crea GitHub Release y publica npm.
# Requiere: dotnet, gh (GitHub CLI), node/npm, zip
#
# Uso:
#   ./scripts/release.sh                  # usa versión de version.nfo
#   ./scripts/release.sh 1.7.15.258       # versión explícita
#   ./scripts/release.sh --build-only     # solo genera dist/, sin publicar
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST_DIR="$REPO_ROOT/dist"
CLI_PROJ="$REPO_ROOT/src/SphereIntegrationHub.cli/SphereIntegrationHub.cli.csproj"
MCP_PROJ="$REPO_ROOT/src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj"
NPM_PKG_DIR="$REPO_ROOT/npm/sphere-integration-hub"
VERSION_FILE="$REPO_ROOT/version.nfo"

RIDS=(linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64)

BUILD_ONLY=false
VERSION=""

# ─── Args ─────────────────────────────────────────────────────────────────────
for arg in "$@"; do
  case "$arg" in
    --build-only) BUILD_ONLY=true ;;
    --help|-h)
      echo "Uso: $0 [version] [--build-only]"
      echo "  version     Versión a publicar (ej: 1.7.15.258). Por defecto lee version.nfo"
      echo "  --build-only  Solo genera los archivos en dist/, sin GitHub Release ni npm"
      exit 0
      ;;
    *) VERSION="$arg" ;;
  esac
done

# ─── Resolver versión ─────────────────────────────────────────────────────────
if [ -z "$VERSION" ]; then
  [ -f "$VERSION_FILE" ] || { echo "Error: no se encontró version.nfo"; exit 1; }
  VERSION=$(tr -d '[:space:]' < "$VERSION_FILE")
fi
[ -n "$VERSION" ] || { echo "Error: versión vacía"; exit 1; }

# npm solo acepta semver de 3 partes: 1.7.15.258 → 1.7.15
NPM_VERSION=$(echo "$VERSION" | cut -d. -f1-3)
TAG="v${VERSION}"

echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Versión:     $VERSION"
echo "  npm version: $NPM_VERSION"
echo "  Tag:         $TAG"
echo "  Build only:  $BUILD_ONLY"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"

# ─── Prerrequisitos ───────────────────────────────────────────────────────────
check_cmd() { command -v "$1" &>/dev/null || { echo "Error: '$1' no encontrado en PATH"; exit 1; }; }
check_cmd dotnet
check_cmd zip
if [ "$BUILD_ONLY" = false ]; then
  check_cmd gh
  check_cmd npm
fi

cd "$REPO_ROOT"

# ─── Build ────────────────────────────────────────────────────────────────────
rm -rf "$DIST_DIR" "$REPO_ROOT/publish" "$REPO_ROOT/staging"
mkdir -p "$DIST_DIR"

for rid in "${RIDS[@]}"; do
  echo ""
  echo "▸ Compilando $rid..."

  dotnet publish "$CLI_PROJ" \
    -r "$rid" --self-contained true \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -c Release -o "publish/$rid/cli" \
    --nologo -v quiet

  dotnet publish "$MCP_PROJ" \
    -r "$rid" --self-contained true \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -c Release -o "publish/$rid/mcp" \
    --nologo -v quiet
done

# ─── Empaquetar ───────────────────────────────────────────────────────────────
echo ""
echo "▸ Empaquetando..."

for entry in \
  "linux-x64:linux:x64" \
  "linux-arm64:linux:arm64" \
  "osx-x64:darwin:x64" \
  "osx-arm64:darwin:arm64"
do
  rid="${entry%%:*}"; rest="${entry#*:}"
  npm_os="${rest%%:*}"; npm_arch="${rest##*:}"
  archive="sih-${npm_os}-${npm_arch}.tar.gz"

  mkdir -p "staging/$rid"
  cp "publish/$rid/cli/SphereIntegrationHub.cli"  "staging/$rid/sih"
  cp "publish/$rid/mcp/SphereIntegrationHub.MCP"  "staging/$rid/sih-mcp"
  chmod +x "staging/$rid/sih" "staging/$rid/sih-mcp"
  tar czf "$DIST_DIR/$archive" -C "staging/$rid" sih sih-mcp
  echo "  → $archive"
done

mkdir -p staging/win-x64
cp publish/win-x64/cli/SphereIntegrationHub.cli.exe staging/win-x64/sih.exe
cp publish/win-x64/mcp/SphereIntegrationHub.MCP.exe staging/win-x64/sih-mcp.exe
zip -j "$DIST_DIR/sih-win32-x64.zip" \
  staging/win-x64/sih.exe staging/win-x64/sih-mcp.exe
echo "  → sih-win32-x64.zip"

echo ""
echo "Archivos en dist/:"
ls -lh "$DIST_DIR"

if [ "$BUILD_ONLY" = true ]; then
  echo ""
  echo "✓ Build completado (--build-only, sin publicar)"
  exit 0
fi

# ─── GitHub Release ───────────────────────────────────────────────────────────
echo ""
echo "▸ Creando GitHub Release $TAG..."

gh release create "$TAG" \
  --title "$TAG" \
  --generate-notes \
  "$DIST_DIR"/*

echo "  ✓ GitHub Release creado"

# ─── npm publish ──────────────────────────────────────────────────────────────
echo ""
echo "▸ Publicando en npm como $NPM_VERSION..."

cd "$NPM_PKG_DIR"
npm version "$NPM_VERSION" --no-git-tag-version --allow-same-version
npm publish --access public

echo "  ✓ Publicado en npm"

echo ""
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "  Release $TAG completado"
echo "  https://github.com/PinedaTec-EU/SphereIntegrationHub/releases/tag/$TAG"
echo "  https://www.npmjs.com/package/@pinedatec.eu/sphere-integration-hub"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
