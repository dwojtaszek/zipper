@echo off
REM Shared helper for E2E test .bat scripts.
REM Call this at the top of every test-*.bat:
REM     call "%~dp0_zipper-cli.bat"
REM     ...
REM     %ZIPPER_CMD% --type pdf --count 10 --output-path "%OUT%"
REM
REM After call, %ZIPPER_CMD% holds the preferred invocation (pre-built binary or
REM dotnet-run fallback). Only builds once per run.

if not "%_ZIPPER_CLI_READY%"=="" goto :eof

if "%ZIPPER_PROJECT%"=="" set "ZIPPER_PROJECT=src\Zipper.csproj"

dotnet build "%ZIPPER_PROJECT%" -c Release --nologo -v quiet >nul
if errorlevel 1 (
    echo [_zipper-cli] dotnet build failed; falling back to 'dotnet run' per call 1^>^&2
    set "ZIPPER_CMD=dotnet run --no-build -c Release --project %ZIPPER_PROJECT% --"
    set "_ZIPPER_CLI_READY=1"
    goto :eof
)

set "BUILD_DIR="
for /d %%D in (src\bin\Release\net*) do set "BUILD_DIR=%%D"

if exist "%BUILD_DIR%\Zipper.exe" (
    set "ZIPPER_CMD=%BUILD_DIR%\Zipper.exe"
) else if exist "%BUILD_DIR%\Zipper" (
    set "ZIPPER_CMD=%BUILD_DIR%\Zipper"
) else (
    set "ZIPPER_CMD=dotnet run --no-build -c Release --project %ZIPPER_PROJECT% --"
)

set "_ZIPPER_CLI_READY=1"
goto :eof
