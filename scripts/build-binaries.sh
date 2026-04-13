#!/usr/bin/env bash
# ──────────────────────────────────────────────────────────────────────────────
# build-binaries.sh
#
# Genera binarios self-contained para una o varias plataformas y los deja
# listos en dist/ para probar el flujo npm localmente.
#
# Uso:
#   ./scripts/build-binaries.sh                   # solo la plataforma actual
#   ./scripts/build-binaries.sh all               # todas las plataformas
#   ./scripts/build-binaries.sh linux-x64 osx-arm64
# ──────────────────────────────────────────────────────────────────────────────
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST_DIR="$REPO_ROOT/dist"
CLI_PROJ="$REPO_ROOT/src/SphereIntegrationHub.cli/SphereIntegrationHub.cli.csproj"
MCP_PROJ="$REPO_ROOT/src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj"

ALL_RIDS=(linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64)

# ─── Detectar RID de la máquina actual ────────────────────────────────────────
detect_local_rid() {
  local os arch
  case "$(uname -s)" in
    Linux)  os="linux"  ;;
    Darwin) os="osx"    ;;
    *)      echo "Sistema no soportado: $(uname -s)" >&2; exit 1 ;;
  esac
  case "$(uname -m)" in
    x86_64)  arch="x64"   ;;
    arm64|aarch64) arch="arm64" ;;
    *)       echo "Arquitectura no soportada: $(uname -m)" >&2; exit 1 ;;
  esac
  echo "${os}-${arch}"
}

# ─── Construir un RID ─────────────────────────────────────────────────────────
build_rid() {
  local rid="$1"
  local out_cli="$REPO_ROOT/publish/$rid/cli"
  local out_mcp="$REPO_ROOT/publish/$rid/mcp"

  echo ""
  echo "▸ Compilando $rid..."

  dotnet publish "$CLI_PROJ" \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -c Release \
    -o "$out_cli" \
    --nologo -v quiet

  dotnet publish "$MCP_PROJ" \
    -r "$rid" \
    --self-contained true \
    -p:PublishSingleFile=true \
    -p:EnableCompressionInSingleFile=true \
    -c Release \
    -o "$out_mcp" \
    --nologo -v quiet

  # Empaquetar
  mkdir -p "$DIST_DIR"

  if [[ "$rid" == win-* ]]; then
    local archive="$DIST_DIR/sphere-integration-hub-win32-${rid##win-}.zip"
    # zip requiere la herramienta zip o se puede usar PowerShell en CI
    zip -j "$archive" "$out_cli/sih.exe" "$out_mcp/sih-mcp.exe"
    echo "  → $archive"
  else
    local npm_os
    case "$rid" in
      linux-*)  npm_os="linux"  ;;
      osx-*)    npm_os="darwin" ;;
    esac
    local npm_arch="${rid##*-}"
    local archive="$DIST_DIR/sphere-integration-hub-${npm_os}-${npm_arch}.tar.gz"
    tar czf "$archive" -C "$(realpath "$out_cli")" sih -C "$(realpath "$out_mcp")" sih-mcp
    echo "  → $archive"
  fi
}

# ─── Copiar binario local al npm/bin para prueba rápida ───────────────────────
install_local() {
  local rid
  rid="$(detect_local_rid)"
  local npm_bin_dir="$REPO_ROOT/npm/sphere-integration-hub/bin"
  local out_cli="$REPO_ROOT/publish/$rid/cli"
  local out_mcp="$REPO_ROOT/publish/$rid/mcp"

  mkdir -p "$npm_bin_dir"
  cp "$out_cli/sih"     "$npm_bin_dir/sih"
  cp "$out_mcp/sih-mcp" "$npm_bin_dir/sih-mcp"
  chmod +x "$npm_bin_dir/sih" "$npm_bin_dir/sih-mcp"

  echo ""
  echo "✓ Binarios copiados a npm/sphere-integration-hub/bin/"
  echo "  Prueba: node npm/sphere-integration-hub/run-cli.js --help"
  echo "  Prueba: node npm/sphere-integration-hub/run-mcp.js"
}

# ─── Main ─────────────────────────────────────────────────────────────────────
cd "$REPO_ROOT"

if [[ $# -eq 0 ]]; then
  # Por defecto: solo la plataforma actual
  LOCAL_RID="$(detect_local_rid)"
  build_rid "$LOCAL_RID"
  install_local
elif [[ "$1" == "all" ]]; then
  for rid in "${ALL_RIDS[@]}"; do
    build_rid "$rid"
  done
else
  for rid in "$@"; do
    build_rid "$rid"
  done
fi

echo ""
echo "Archivos generados en dist/:"
ls -lh "$DIST_DIR" 2>/dev/null || true
