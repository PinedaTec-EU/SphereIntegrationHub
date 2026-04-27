#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat <<'EOF'
Usage:
  ./scripts/local/publish-nuget-local.sh [--api-key <key>] [--source <url>]

This script discovers and packs all packable .csproj files under src/
and publishes the generated NuGet packages.

Options:
  --api-key <key>   NuGet API key. If omitted, uses NUGET_API_KEY.
  --source <url>    NuGet source URL. Defaults to https://api.nuget.org/v3/index.json.
  --help, -h        Show this help.
EOF
}

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"

if [[ -f "${ROOT_DIR}/.env.local" ]]; then
  # shellcheck source=/dev/null
  source "${ROOT_DIR}/.env.local"
fi

PROJECTS=()
CONFIGURATION="Release"
SOURCE_URL="https://api.nuget.org/v3/index.json"
OUTPUT_DIR="${ROOT_DIR}/.local/nuget-packages"
API_KEY="${NUGET_API_KEY:-}"

discover_packable_projects() {
  local csproj
  local is_packable

  while IFS= read -r csproj; do
    if [[ -z "${csproj}" ]]; then
      continue
    fi

    is_packable="$(dotnet msbuild "${csproj}" -nologo -getProperty:IsPackable | tr -d '\r' | tail -n 1)"
    if [[ "${is_packable}" == "true" ]]; then
      PROJECTS+=("${csproj}")
    fi
  done < <(find "${ROOT_DIR}/src" -type f -name '*.csproj' | sort)
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --api-key)
      if [[ $# -lt 2 ]]; then
        echo "Error: --api-key requires a value." >&1
        usage
        exit 1
      fi
      API_KEY="$2"
      shift 2
      ;;
    --source)
      if [[ $# -lt 2 ]]; then
        echo "Error: --source requires a value." >&2
        usage
        exit 1
      fi
      SOURCE_URL="$2"
      shift 2
      ;;
    --help|-h)
      usage
      exit 0
      ;;
    *)
      echo "Error: unsupported argument: $1" >&2
      usage
      exit 1
      ;;
  esac
done

discover_packable_projects
if [[ ${#PROJECTS[@]} -eq 0 ]]; then
  echo "Error: no packable .csproj files found under ${ROOT_DIR}/src" >&2
  exit 1
fi

if [[ -z "${API_KEY}" ]]; then
  echo "Error: missing API key. Pass --api-key or set NUGET_API_KEY." >&2
  exit 1
fi

mkdir -p "${OUTPUT_DIR}"
find "${OUTPUT_DIR}" -maxdepth 1 -type f \( -name '*.nupkg' -o -name '*.snupkg' \) -delete

echo "Discovered ${#PROJECTS[@]} packable project(s):"
for project_path in "${PROJECTS[@]}"; do
  echo "- ${project_path}"
done

for project_path in "${PROJECTS[@]}"; do
  if [[ ! -f "${project_path}" ]]; then
    echo "Error: project not found: ${project_path}" >&2
    exit 1
  fi

  echo "Packing project: ${project_path}"
  dotnet pack "${project_path}" -c "${CONFIGURATION}" -o "${OUTPUT_DIR}" --nologo
done

PACKAGES=()
while IFS= read -r pkg; do
  PACKAGES+=("${pkg}")
done < <(find "${OUTPUT_DIR}" -maxdepth 1 -type f -name '*.nupkg' ! -name '*.symbols.nupkg' | sort)
if [[ ${#PACKAGES[@]} -eq 0 ]]; then
  echo "Error: no .nupkg generated in ${OUTPUT_DIR}" >&2
  exit 1
fi

echo "Publishing ${#PACKAGES[@]} package(s) to ${SOURCE_URL}"
for pkg in "${PACKAGES[@]}"; do
  echo "- pushing $(basename "${pkg}")"
  dotnet nuget push "${pkg}" \
    --api-key "${API_KEY}" \
    --source "${SOURCE_URL}" \
    --skip-duplicate
done

echo "Publish completed successfully."
