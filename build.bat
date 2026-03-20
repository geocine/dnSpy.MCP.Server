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

if "%FIRST_ARG%"=="" (
  set "NUKE_ARGS=--target All"
  goto :invoke
)
if /i "%FIRST_ARG%"=="all" goto :run_all
if /i "%FIRST_ARG%"=="dnspy" goto :run_dnspy
if /i "%FIRST_ARG%"=="mcp" goto :run_mcp
if /i "%FIRST_ARG%"=="holly" goto :run_holly
if /i "%FIRST_ARG%"=="sync-holly" goto :run_sync_holly
if /i "%FIRST_ARG%"=="nuke" goto :run_passthrough_after_keyword
if /i "%FIRST_ARG%"=="help" goto :usage_ok
if /i "%FIRST_ARG%"=="-h" goto :usage_ok
if /i "%FIRST_ARG%"=="--help" goto :usage_ok
if /i "%FIRST_ARG%"=="/?" goto :usage_ok

set "FIRST_CHAR=%FIRST_ARG:~0,1%"
if "%FIRST_CHAR%"=="-" goto :run_passthrough
if "%FIRST_CHAR%"=="/" goto :run_passthrough

goto :run_named_target

:run_all
set "NUKE_ARGS=--target All %REST_ARGS%"
goto :invoke

:run_dnspy
set "NUKE_ARGS=--target DnSpy %REST_ARGS%"
goto :invoke

:run_mcp
set "NUKE_ARGS=--target Mcp %REST_ARGS%"
goto :invoke

:run_holly
set "NUKE_ARGS=--target Holly %REST_ARGS%"
goto :invoke

:run_sync_holly
set "NUKE_ARGS=--target SyncHolly %REST_ARGS%"
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
echo Usage: build.bat [all^|dnspy^|mcp^|holly^|sync-holly^|nuke ...]
echo.
echo   all        Repair/build dnSpy, then build and install both MCP and HoLLy into the shared dnSpy output. Default.
echo   dnspy      Repair/build only the shared dnSpy host and ensure submodules are available.
echo   mcp        Build and install only the MCP extension for net10.0-windows.
echo   holly      Build and install only the HoLLy extension for net10.0-windows.
echo   sync-holly Update the HoLLy submodule to the latest origin/master and refresh its nested submodules.
echo   nuke ...   Pass raw arguments through to the underlying NUKE build.
echo.
echo Examples:
echo   build.bat
echo   build.bat dnspy
echo   build.bat holly
echo   build.bat sync-holly
echo   build.bat nuke --target Mcp --verbosity verbose
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
