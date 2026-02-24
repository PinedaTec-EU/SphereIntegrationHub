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
