#!/usr/bin/env bash

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
PROJECT_FILE="${PROJECT_ROOT}/src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj"
OUTPUT_ROOT="${PROJECT_ROOT}/dist/mcp"

if [[ ! -f "${PROJECT_FILE}" ]]; then
  echo "Project file not found: ${PROJECT_FILE}" >&2
  exit 1
fi

# Defaults: Apple Silicon + Windows x64
if [[ $# -gt 0 ]]; then
  RIDS=("$@")
else
  RIDS=("osx-arm64" "win-x64")
fi

echo "Project root: ${PROJECT_ROOT}"
echo "Project file: ${PROJECT_FILE}"
echo "Output root: ${OUTPUT_ROOT}"
echo "RIDs: ${RIDS[*]}"

for rid in "${RIDS[@]}"; do
  out_dir="${OUTPUT_ROOT}/${rid}"
  echo ""
  echo "Publishing MCP for ${rid}..."
  rm -rf "${out_dir}"
  mkdir -p "${out_dir}"

  dotnet publish "${PROJECT_FILE}" \
    -c Release \
    -r "${rid}" \
    --self-contained true \
    /p:PublishSingleFile=true \
    /p:PublishTrimmed=false \
    /p:DebugType=None \
    -o "${out_dir}"

  echo "Done: ${out_dir}"
done

echo ""
echo "Build complete."
