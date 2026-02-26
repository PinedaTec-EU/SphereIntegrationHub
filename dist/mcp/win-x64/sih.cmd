@echo off
setlocal
set "BASE_DIR=%~dp0"

if /I "%~1"=="-h" goto :help
if /I "%~1"=="--help" goto :help
if /I "%~1"=="help" goto :help
if "%~1"=="" goto :help

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
echo.
call :help
exit /b 1

:help
echo API Orchestrator Sphere Launcher
echo --------------------------------
echo Usage:
echo   sih ^<command^> [args]
echo.
echo Commands:
echo   run       Execute a workflow using SphereIntegrationHub CLI runtime.
echo   mcp       Start SphereIntegrationHub MCP server.
echo.
echo Examples:
echo   sih run --workflow .\.sphere\workflows\create_tier.workflow --env local --refresh-cache
echo   sih run --workflow .\.sphere\workflows\create_tier.workflow --env local --dry-run
echo   sih mcp
echo.
echo Help:
echo   sih --help
echo   sih run --help
echo   sih mcp --help
exit /b 0
