#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
MCP_PROJECT_FILE="${PROJECT_ROOT}/src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj"
CLI_PROJECT_FILE="${PROJECT_ROOT}/src/SphereIntegrationHub.cli/SphereIntegrationHub.cli.csproj"
AGENTS_FILE="${PROJECT_ROOT}/src/SphereIntegrationHub.MCP/AGENTS.md"
PACKAGE_README_FILE="${SCRIPT_DIR}/PACKAGE_README.md"
OUTPUT_ROOT="${PROJECT_ROOT}/dist/mcp"
CONFIGURATION="Release"

if [[ ! -f "${MCP_PROJECT_FILE}" ]]; then
  echo "MCP project file not found: ${MCP_PROJECT_FILE}" >&2
  exit 1
fi

if [[ ! -f "${CLI_PROJECT_FILE}" ]]; then
  echo "CLI project file not found: ${CLI_PROJECT_FILE}" >&2
  exit 1
fi

if [[ ! -f "${AGENTS_FILE}" ]]; then
  echo "Agent instructions file not found: ${AGENTS_FILE}" >&2
  exit 1
fi

if [[ ! -f "${PACKAGE_README_FILE}" ]]; then
  echo "Package README file not found: ${PACKAGE_README_FILE}" >&2
  exit 1
fi

print_usage() {
  cat <<EOF
Usage: $(basename "$0") [options] [rid...]

Builds a portable SphereIntegrationHub package with:
  - MCP server binary
  - CLI runtime binary
  - \`sih\` wrapper (or \`sih.cmd\` on Windows)
  - \`mcp\` shortcut (or \`mcp.cmd\` on Windows)

Options:
  -r, --rid <rid>               Runtime identifier. Can be repeated.
  -o, --output <dir>            Output root directory (default: dist/mcp)
  -c, --configuration <name>    Build configuration (default: Release)
  -h, --help                    Show this help and exit

Examples:
  $(basename "$0")
  $(basename "$0") osx-arm64 win-x64
  $(basename "$0") --rid linux-x64 --rid osx-arm64
EOF
}

RIDS=()

while [[ $# -gt 0 ]]; do
  case "$1" in
    -h|--help)
      print_usage
      exit 0
      ;;
    -r|--rid)
      if [[ $# -lt 2 ]]; then
        echo "Missing value for $1" >&2
        exit 1
      fi

      RIDS+=("$2")
      shift 2
      ;;
    -o|--output)
      if [[ $# -lt 2 ]]; then
        echo "Missing value for $1" >&2
        exit 1
      fi

      OUTPUT_ROOT="$2"
      shift 2
      ;;
    -c|--configuration)
      if [[ $# -lt 2 ]]; then
        echo "Missing value for $1" >&2
        exit 1
      fi

      CONFIGURATION="$2"
      shift 2
      ;;
    *)
      RIDS+=("$1")
      shift
      ;;
  esac
done

# Defaults: Apple Silicon + Windows x64
if [[ ${#RIDS[@]} -eq 0 ]]; then
  RIDS=("osx-arm64" "win-x64")
fi

publish_project() {
  local project_file="$1"
  local rid="$2"
  local out_dir="$3"

  dotnet publish "${project_file}" \
    -c "${CONFIGURATION}" \
    -r "${rid}" \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:PublishTrimmed=false \
    /p:DebugType=None \
    -o "${out_dir}"
}

write_unix_launchers() {
  local out_dir="$1"

  cat > "${out_dir}/sih" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

BASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if [[ $# -lt 1 ]]; then
  echo "Usage: sih <run|mcp> [args]" >&2
  exit 1
fi

command="$1"
shift || true

case "${command}" in
  run)
    exec "${BASE_DIR}/SphereIntegrationHub.cli" "$@"
    ;;
  mcp)
    exec "${BASE_DIR}/SphereIntegrationHub.MCP" "$@"
    ;;
  *)
    echo "Unknown command: ${command}" >&2
    echo "Usage: sih <run|mcp> [args]" >&2
    exit 1
    ;;
esac
EOF

  cat > "${out_dir}/mcp" <<'EOF'
#!/usr/bin/env bash
set -euo pipefail

BASE_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
exec "${BASE_DIR}/SphereIntegrationHub.MCP" "$@"
EOF

  chmod +x "${out_dir}/sih"
  chmod +x "${out_dir}/mcp"
}

write_windows_launchers() {
  local out_dir="$1"

  cat > "${out_dir}/sih.cmd" <<'EOF'
@echo off
setlocal
set "BASE_DIR=%~dp0"

if "%~1"=="" (
  echo Usage: sih ^<run^|mcp^> [args]
  exit /b 1
)

set "COMMAND=%~1"
shift

if /I "%COMMAND%"=="run" (
  "%BASE_DIR%SphereIntegrationHub.cli.exe" %*
  exit /b %errorlevel%
)

if /I "%COMMAND%"=="mcp" (
  "%BASE_DIR%SphereIntegrationHub.MCP.exe" %*
  exit /b %errorlevel%
)

echo Unknown command: %COMMAND%
echo Usage: sih ^<run^|mcp^> [args]
exit /b 1
EOF

  cat > "${out_dir}/mcp.cmd" <<'EOF'
@echo off
setlocal
set "BASE_DIR=%~dp0"
"%BASE_DIR%SphereIntegrationHub.MCP.exe" %*
EOF
}

echo "Project root: ${PROJECT_ROOT}"
echo "MCP project file: ${MCP_PROJECT_FILE}"
echo "CLI project file: ${CLI_PROJECT_FILE}"
echo "Output root: ${OUTPUT_ROOT}"
echo "Configuration: ${CONFIGURATION}"
echo "RIDs: ${RIDS[*]}"

for rid in "${RIDS[@]}"; do
  out_dir="${OUTPUT_ROOT}/${rid}"
  echo ""
  echo "Packaging SIH for ${rid}..."
  rm -rf "${out_dir}"
  mkdir -p "${out_dir}"

  publish_project "${MCP_PROJECT_FILE}" "${rid}" "${out_dir}"
  publish_project "${CLI_PROJECT_FILE}" "${rid}" "${out_dir}"

  cp "${AGENTS_FILE}" "${out_dir}/AGENTS.md"
  cp "${PACKAGE_README_FILE}" "${out_dir}/README.md"

  if [[ "${rid}" == win-* ]]; then
    write_windows_launchers "${out_dir}"
  else
    write_unix_launchers "${out_dir}"
  fi

  echo "Done: ${out_dir}"
done

echo ""
echo "Build complete. Use the generated 'sih' (or 'sih.cmd') wrapper to run CLI and MCP."
