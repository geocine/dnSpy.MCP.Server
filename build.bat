@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "ROOT=%CD%"
set "BUILD_PROJECT=%ROOT%\build\_build.csproj"
set "FIRST_ARG=%~1"
set "REST_ARGS=%~2 %~3 %~4 %~5 %~6 %~7 %~8 %~9"
set "NUKE_ARGS="

if not exist "%BUILD_PROJECT%" (
  echo ERROR: NUKE build project was not found at "%BUILD_PROJECT%".
  exit /b 1
)

if "%FIRST_ARG%"=="" goto :run_default
if /i "%FIRST_ARG%"=="all" goto :run_all
if /i "%FIRST_ARG%"=="dnspy" goto :run_dnspy
if /i "%FIRST_ARG%"=="mcp" goto :run_mcp
if /i "%FIRST_ARG%"=="mcp-net10" goto :run_mcp_net10
if /i "%FIRST_ARG%"=="mcp-net48" goto :run_mcp_net48
if /i "%FIRST_ARG%"=="mcp-all" goto :run_mcp_all
if /i "%FIRST_ARG%"=="nuke" goto :run_passthrough_after_keyword
if /i "%FIRST_ARG%"=="help" goto :usage_ok
if /i "%FIRST_ARG%"=="-h" goto :usage_ok
if /i "%FIRST_ARG%"=="--help" goto :usage_ok
if /i "%FIRST_ARG%"=="/?" goto :usage_ok

set "FIRST_CHAR=%FIRST_ARG:~0,1%"
if "%FIRST_CHAR%"=="-" goto :run_passthrough
if "%FIRST_CHAR%"=="/" goto :run_passthrough

goto :run_named_target

:run_default
set "NUKE_ARGS=--target All"
goto :invoke

:run_all
set "NUKE_ARGS=--target All %REST_ARGS%"
goto :invoke

:run_dnspy
set "NUKE_ARGS=--target DnSpy %REST_ARGS%"
goto :invoke

:run_mcp
set "NUKE_ARGS=--target McpNet10 %REST_ARGS%"
goto :invoke

:run_mcp_net10
set "NUKE_ARGS=--target McpNet10 %REST_ARGS%"
goto :invoke

:run_mcp_net48
set "NUKE_ARGS=--target McpNet48 %REST_ARGS%"
goto :invoke

:run_mcp_all
set "NUKE_ARGS=--target McpAll %REST_ARGS%"
goto :invoke

:run_passthrough
set "NUKE_ARGS=%*"
goto :invoke

:run_passthrough_after_keyword
set "NUKE_ARGS=%REST_ARGS%"
goto :invoke

:run_named_target
set "NUKE_ARGS=--target %FIRST_ARG% %REST_ARGS%"
goto :invoke

:invoke
call :require_dotnet
if errorlevel 1 exit /b 1

dotnet run --project "%BUILD_PROJECT%" -- %NUKE_ARGS%
exit /b %errorlevel%

:usage_ok
echo Usage: build.bat [all^|dnspy^|mcp^|mcp-net10^|mcp-net48^|mcp-all^|nuke ...]
echo.
echo   all        Repair/build dnSpy, fix known output quirks, then build the MCP extension for net10.0-windows. Default.
echo   dnspy      Repair/build only the dnSpy host and known output quirks.
echo   mcp        Alias for mcp-net10.
echo   mcp-net10  Build only the MCP extension for net10.0-windows.
echo   mcp-net48  Attempt to build only the MCP extension for net48.
echo   mcp-all    Attempt to build the MCP extension for all target frameworks.
echo   nuke ...   Pass raw arguments through to the underlying NUKE build.
echo.
echo Examples:
echo   build.bat
echo   build.bat dnspy
echo   build.bat nuke --target McpNet10 --verbosity verbose
exit /b 0

:require_dotnet
where dotnet >nul 2>nul
if not errorlevel 1 exit /b 0

echo ERROR: dotnet was not found in PATH.
echo.
echo Install the .NET 10 SDK with:
echo   winget install Microsoft.DotNet.SDK.10
echo.
echo Then reopen your terminal and run this command again.
exit /b 1
