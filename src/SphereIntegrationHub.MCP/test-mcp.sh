#!/usr/bin/env bash
# MCP resilience/privacy smoke tests

set -euo pipefail

PROJECT_ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
export SIH_PROJECT_ROOT="$PROJECT_ROOT"

MCP_BIN="${PROJECT_ROOT}/dist/mcp/osx-arm64/sih"
if [[ -x "${MCP_BIN}" ]]; then
  MCP_CMD=("${MCP_BIN}" "mcp")
else
  MCP_CMD=("dotnet" "run" "--project" "${PROJECT_ROOT}/src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj")
fi

echo "Testing MCP Server..."
echo "Project Root: ${PROJECT_ROOT}"
echo "MCP Command: ${MCP_CMD[*]}"

assert_contains() {
  local haystack="$1"
  local needle="$2"
  local message="$3"
  if [[ "${haystack}" != *"${needle}"* ]]; then
    echo "FAILED: ${message}" >&2
    echo "Expected to find: ${needle}" >&2
    exit 1
  fi
}

assert_equals() {
  local expected="$1"
  local actual="$2"
  local message="$3"
  if [[ "${expected}" != "${actual}" ]]; then
    echo "FAILED: ${message}" >&2
    echo "Expected: ${expected}" >&2
    echo "Actual:   ${actual}" >&2
    exit 1
  fi
}

run_request() {
  local payload="$1"
  local stderr_file="$2"
  printf '%s\n' "${payload}" | "${MCP_CMD[@]}" 2>"${stderr_file}"
}

echo
echo "Test 1: initialize"
response="$(run_request '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' /tmp/mcp_test_1.err)"
assert_contains "${response}" '"id":1' "initialize response id mismatch"
assert_contains "${response}" '"serverInfo"' "initialize missing serverInfo"
echo "OK"

echo
echo "Test 2: malformed JSON should return parse error and continue"
response="$(cat <<'EOF' | "${MCP_CMD[@]}" 2>/tmp/mcp_test_2.err
{ bad-json }
{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}
EOF
)"
assert_contains "${response}" '"code":-32700' "expected parse error for malformed JSON"
assert_contains "${response}" '"id":2' "second request not processed after parse error"
echo "OK"

echo
echo "Test 3: invalid tool arguments should return invalid params"
response="$(run_request '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"list_api_catalog_versions","arguments":"oops"}}' /tmp/mcp_test_3.err)"
assert_contains "${response}" '"code":-32602' "expected invalid params for non-object arguments"
echo "OK"

echo
echo "Test 4: notification should not produce response"
response="$(run_request '{"jsonrpc":"2.0","method":"initialized","params":{}}' /tmp/mcp_test_4.err)"
size="$(printf '%s' "${response}" | wc -c | tr -d ' ')"
assert_equals "0" "${size}" "notification unexpectedly produced output"
echo "OK"

echo
echo "Test 5: request logs should not expose payload contents"
secret_token="SUPER_SECRET_123"
run_request "{\"jsonrpc\":\"2.0\",\"id\":5,\"method\":\"tools/call\",\"params\":{\"name\":\"generate_workflow_skeleton\",\"arguments\":{\"name\":\"w\",\"description\":\"token=${secret_token}\"}}}" /tmp/mcp_test_5.err >/dev/null
if rg -q "${secret_token}" /tmp/mcp_test_5.err; then
  echo "FAILED: secret token leaked in stderr logs" >&2
  exit 1
fi
echo "OK"

echo
echo "All MCP resilience/privacy smoke tests passed."
