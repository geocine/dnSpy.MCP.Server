@echo off
setlocal EnableExtensions

cd /d "%~dp0"

set "ROOT=%CD%"
set "MODE=%~1"
if /i "%MODE%"=="" set "MODE=all"

set "SUBMODULE=dnSpy"
set "SUBMODULE_PATH=%ROOT%\%SUBMODULE%"
set "SUBMODULE_GIT_CONFIG=%ROOT%\.git\modules\%SUBMODULE%\config"
set "SOLUTION_PATH=%SUBMODULE_PATH%\dnSpy.sln"
set "MCP_PROJECT=%ROOT%\dnSpy.MCP.Server.csproj"
set "HOST_NET48_DIR=%SUBMODULE_PATH%\dnSpy\dnSpy\bin\Release\net48"
set "THEMES_DST=%HOST_NET48_DIR%\Themes"
set "THEMES_SRC=%HOST_NET48_DIR%\bin\Themes"

if /i "%MODE%"=="all" goto :build_all
if /i "%MODE%"=="dnspy" goto :build_dnspy
if /i "%MODE%"=="mcp" goto :build_mcp_net10
if /i "%MODE%"=="mcp-net10" goto :build_mcp_net10
if /i "%MODE%"=="mcp-net48" goto :build_mcp_net48
if /i "%MODE%"=="mcp-all" goto :build_mcp_all
if /i "%MODE%"=="help" goto :usage_ok
if /i "%MODE%"=="-h" goto :usage_ok
if /i "%MODE%"=="--help" goto :usage_ok

echo ERROR: Unknown mode "%MODE%".
goto :usage_fail

:build_all
echo [build] Repairing and building dnSpy host...
call :build_dnspy_flow
if errorlevel 1 goto :fail

echo [build] Building MCP extension for net10.0-windows...
call :build_mcp_net10_flow
if errorlevel 1 goto :fail

echo.
echo Build completed successfully.
exit /b 0

:build_dnspy
echo [build] Repairing and building dnSpy host...
call :build_dnspy_flow
if errorlevel 1 goto :fail

echo.
echo dnSpy host build completed successfully.
exit /b 0

:build_mcp_net10
echo [build] Building MCP extension for net10.0-windows...
call :build_mcp_net10_flow
if errorlevel 1 goto :fail

echo.
echo MCP extension net10.0-windows build completed successfully.
exit /b 0

:build_mcp_net48
echo [build] Building MCP extension for net48...
dotnet build "%MCP_PROJECT%" -c Release -f net48 --nologo
if errorlevel 1 goto :fail

echo.
echo MCP extension net48 build completed successfully.
exit /b 0

:build_mcp_all
echo [build] Building MCP extension for all target frameworks...
dotnet build "%MCP_PROJECT%" -c Release --nologo
if errorlevel 1 goto :fail

echo.
echo MCP extension multi-target build completed successfully.
exit /b 0

:build_dnspy_flow
call :require_tool git
if errorlevel 1 exit /b 1

call :require_tool dotnet
if errorlevel 1 exit /b 1

if not exist "%ROOT%\.gitmodules" (
  echo ERROR: .gitmodules was not found in "%ROOT%".
  exit /b 1
)

echo [1/4] Ensuring dnSpy submodule metadata...
if exist "%SUBMODULE_GIT_CONFIG%" (
  echo Submodule metadata already exists. Skipping sync.
) else (
  git submodule sync --recursive
  if errorlevel 1 exit /b 1
)

echo [2/4] Ensuring dnSpy submodule checkout...
call :submodule_is_aligned
if not errorlevel 1 (
  echo Submodule checkout already matches the pinned commits. Skipping update.
) else (
  git submodule update --init --recursive --force "%SUBMODULE%"
  if errorlevel 1 (
    echo Initial submodule update failed. Attempting clean recovery...
    call :recover_submodule
    if errorlevel 1 exit /b 1
  )
)

echo [3/4] Building dnSpy solution without a global TargetFramework override...
dotnet build "%SOLUTION_PATH%" -c Release --nologo
if errorlevel 1 exit /b 1

echo [4/4] Normalizing net48 theme files...
call :sync_themes
if errorlevel 1 exit /b 1

exit /b 0

:build_mcp_net10_flow
call :require_tool dotnet
if errorlevel 1 exit /b 1

dotnet build "%MCP_PROJECT%" -c Release -f net10.0-windows --nologo
if errorlevel 1 exit /b 1

exit /b 0

:recover_submodule
git submodule deinit -f -- "%SUBMODULE%"
if errorlevel 1 exit /b 1

if exist "%SUBMODULE_PATH%" (
  cmd /c rmdir /s /q "%SUBMODULE_PATH%"
  if exist "%SUBMODULE_PATH%" exit /b 1
)

git submodule sync --recursive
if errorlevel 1 exit /b 1

git submodule update --init --recursive --force "%SUBMODULE%"
if errorlevel 1 exit /b 1

exit /b 0

:submodule_is_aligned
set "SUBMODULE_STATUS_FILE=%TEMP%\dnspy-submodule-status-%RANDOM%-%RANDOM%.txt"
git submodule status --recursive > "%SUBMODULE_STATUS_FILE%"
if errorlevel 1 (
  del "%SUBMODULE_STATUS_FILE%" >nul 2>nul
  exit /b 1
)

findstr /r "^[+-U]" "%SUBMODULE_STATUS_FILE%" >nul
if not errorlevel 1 (
  del "%SUBMODULE_STATUS_FILE%" >nul 2>nul
  exit /b 1
)

findstr /c:"-dirty" "%SUBMODULE_STATUS_FILE%" >nul
if not errorlevel 1 (
  del "%SUBMODULE_STATUS_FILE%" >nul 2>nul
  exit /b 1
)

del "%SUBMODULE_STATUS_FILE%" >nul 2>nul
exit /b 0

:sync_themes
if not exist "%THEMES_DST%" mkdir "%THEMES_DST%"

if exist "%THEMES_SRC%\*.dntheme" (
  copy /y "%THEMES_SRC%\*.dntheme" "%THEMES_DST%\" >nul
)

dir /b "%THEMES_DST%\*.dntheme" >nul 2>nul
if errorlevel 1 (
  echo ERROR: No .dntheme files were found in "%THEMES_DST%".
  exit /b 1
)

echo Theme files are available in "%THEMES_DST%".
exit /b 0

:require_tool
where "%~1" >nul 2>nul
if errorlevel 1 (
  echo ERROR: Required tool "%~1" was not found in PATH.
  exit /b 1
)
exit /b 0

:usage_ok
echo Usage: build.bat [all^|dnspy^|mcp^|mcp-net10^|mcp-net48^|mcp-all^|help]
echo.
echo   all       Repair/build dnSpy, then build the MCP extension for net10.0-windows. Default.
echo   dnspy     Repair/build only the dnSpy host and known output quirks.
echo   mcp       Alias for mcp-net10.
echo   mcp-net10 Build only the MCP extension for net10.0-windows.
echo   mcp-net48 Attempt to build only the MCP extension for net48.
echo   mcp-all   Attempt to build the MCP extension for all target frameworks.
echo   help      Show this help text.
exit /b 0

:usage_fail
call :usage_ok
exit /b 1

:fail
echo.
echo Build failed.
exit /b 1
