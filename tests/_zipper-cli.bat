@echo off
REM Shared helper for E2E test .bat scripts.
REM Call this at the top of every test-*.bat:
REM     call "%~dp0_zipper-cli.bat"
REM     ...
REM     %ZIPPER_CMD% --type pdf --count 10 --output-path "%OUT%"
REM
REM After call, %ZIPPER_CMD% holds the preferred invocation (pre-built binary or
REM dotnet-run fallback). Only builds once per run.

if defined ZIPPER_EXECUTABLE (
    REM Never build or fall back when testing an explicitly supplied artifact.
    set "ZIPPER_CMD="%ZIPPER_EXECUTABLE%""
    if not exist "%ZIPPER_EXECUTABLE%" exit /b 1
    for %%F in ("%ZIPPER_EXECUTABLE%") do if not "%%~fF"=="%ZIPPER_EXECUTABLE%" exit /b 1
    exit /b 0
)

if not "%_ZIPPER_CLI_READY%"=="" goto :eof

if "%ZIPPER_PROJECT%"=="" set "ZIPPER_PROJECT=src\Zipper.csproj"

if exist "src\obj\project.assets.json" (
    dotnet build "%ZIPPER_PROJECT%" -c Release --no-restore --nologo -v quiet >nul 2>nul
    if not errorlevel 1 goto :build_done
)
dotnet build "%ZIPPER_PROJECT%" -c Release --nologo -v quiet >nul
if errorlevel 1 (
    echo [_zipper-cli] dotnet build failed; falling back to 'dotnet run' per call
    REM Fall back WITHOUT --no-build so dotnet run can compile on demand.
    REM Using --no-build here would guarantee a missing-assembly failure.
    set "ZIPPER_CMD=dotnet run -c Release --project %ZIPPER_PROJECT% --"
    set "_ZIPPER_CLI_READY=1"
    goto :eof
)
:build_done

REM Resolve BUILD_DIR to an absolute path (%%~fD) so callers can cd elsewhere
REM between this helper and the first %ZIPPER_CMD% invocation.
set "BUILD_DIR="
for /d %%D in (src\bin\Release\net*) do set "BUILD_DIR=%%~fD"

if exist "%BUILD_DIR%\Zipper.exe" (
    set "ZIPPER_CMD=%BUILD_DIR%\Zipper.exe"
) else if exist "%BUILD_DIR%\Zipper" (
    set "ZIPPER_CMD=%BUILD_DIR%\Zipper"
) else (
    set "ZIPPER_CMD=dotnet run -c Release --project %ZIPPER_PROJECT% --"
)

set "_ZIPPER_CLI_READY=1"
goto :eof
