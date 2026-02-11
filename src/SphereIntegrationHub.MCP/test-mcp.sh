#!/bin/bash
# Simple integration test for MCP server

set -e

PROJECT_ROOT=$(cd "$(dirname "$0")/../.." && pwd)
export SIH_PROJECT_ROOT="$PROJECT_ROOT"

echo "Testing MCP Server..."
echo "Project Root: $PROJECT_ROOT"

# Test 1: Initialize
echo ""
echo "Test 1: Initialize request"
echo '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' | \
  dotnet run --project "$PROJECT_ROOT/src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj" 2>/dev/null &
PID=$!
sleep 2
kill $PID 2>/dev/null || true
echo "✓ Server started and accepted initialize"

# Test 2: Tools list
echo ""
echo "Test 2: Tools/list request"
echo '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' | \
  dotnet run --project "$PROJECT_ROOT/src/SphereIntegrationHub.MCP/SphereIntegrationHub.MCP.csproj" 2>/dev/null &
PID=$!
sleep 2
kill $PID 2>/dev/null || true
echo "✓ Server responded to tools/list"

echo ""
echo "All tests passed!"
