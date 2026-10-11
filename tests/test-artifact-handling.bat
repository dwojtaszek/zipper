@echo off
setlocal enabledelayedexpansion

REM Test script to validate artifact handling in current workflows (Windows)
REM This script checks artifact creation, naming, and retention patterns

echo === Artifact Handling Test (Windows) ===
echo Testing artifact creation and handling patterns

REM The publish/upload steps live in the reusable ci.yml; the release job's
REM download and its file patterns live in the main caller. Each assertion must
REM read the file that actually declares it, otherwise a passing gate can be
REM asserting against a structure that no longer exists.
set SHARED_WORKFLOW_FILE=.github\workflows\ci.yml
set RELEASE_WORKFLOW_FILE=.github\workflows\build-and-test.yml

REM Check if workflows exist
if not exist "%SHARED_WORKFLOW_FILE%" (
    echo [X] Workflow file not found: %SHARED_WORKFLOW_FILE%
    exit /b 1
)

if not exist "%RELEASE_WORKFLOW_FILE%" (
    echo [X] Workflow file not found: %RELEASE_WORKFLOW_FILE%
    exit /b 1
)

REM Check ci.yml for artifact patterns
echo 1. Analyzing %SHARED_WORKFLOW_FILE% artifact patterns...

REM Check for artifact upload steps
findstr /C:"actions/upload-artifact@" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Uses actions/upload-artifact
) else (
    echo [X] Missing actions/upload-artifact
    exit /b 1
)

echo 2. Checking platform-specific artifact configuration...

findstr /C:"win-x64" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found win-x64 configuration
) else (
    echo [X] Missing win-x64 configuration
    exit /b 1
)

findstr /C:"linux-x64" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found linux-x64 configuration
) else (
    echo [X] Missing linux-x64 configuration
    exit /b 1
)

findstr /C:"osx-arm64" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found osx-arm64 configuration
) else (
    echo [X] Missing osx-arm64 configuration
    exit /b 1
)

findstr /C:"zipper-win-x64" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found zipper-win-x64 naming
) else (
    echo [X] Missing zipper-win-x64 naming
    exit /b 1
)

findstr /C:"zipper-linux-x64" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found zipper-linux-x64 naming
) else (
    echo [X] Missing zipper-linux-x64 naming
    exit /b 1
)

findstr /C:"zipper-osx-arm64" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found zipper-osx-arm64 naming
) else (
    echo [X] Missing zipper-osx-arm64 naming
    exit /b 1
)

echo 3. Analyzing caching strategy...

findstr /S /C:"actions/cache@" ".github\*.yml" >nul
if !errorlevel! equ 0 (
    echo [OK] Uses actions/cache
) else (
    echo [X] Missing actions/cache
    exit /b 1
)

echo 4. Checking cache paths...

findstr /C:"publish/" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found publish/ directory structure
) else (
    echo [X] Missing publish/ directory structure
    exit /b 1
)

findstr /C:"matrix.platform" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Uses platform matrix variable
) else (
    echo [X] Missing platform matrix variable
    exit /b 1
)

echo 5. Checking release job artifact handling...

findstr /C:"actions/download-artifact@" "%RELEASE_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Uses actions/download-artifact
) else (
    echo [X] Missing actions/download-artifact
    exit /b 1
)

echo 6. Checking release file patterns...

findstr /C:"artifacts/zipper-win-x64/zipper-win-x64.exe" "%RELEASE_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found release file pattern: win-x64
) else (
    echo [X] Missing release file pattern: win-x64
    exit /b 1
)

findstr /C:"artifacts/zipper-linux-x64/zipper-linux-x64" "%RELEASE_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found release file pattern: linux-x64
) else (
    echo [X] Missing release file pattern: linux-x64
    exit /b 1
)

findstr /C:"artifacts/zipper-osx-arm64/zipper-osx-arm64" "%RELEASE_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found release file pattern: osx-arm64
) else (
    echo [X] Missing release file pattern: osx-arm64
    exit /b 1
)

echo 7. Validating build output structure...

findstr /C:"publish/" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Uses publish/ directory structure
) else (
    echo [X] Missing publish/ directory structure
    exit /b 1
)

echo 8. Checking for PDB file cleanup...

REM Use regex mode (/R): findstr /C: treats its pattern as literal text, so a
REM regex-style pattern would never match the cleanup command.
findstr /R /C:"rm.*\.pdb" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Removes PDB files from artifacts
) else (
    echo [X] Missing PDB file cleanup
    exit /b 1
)

echo 9. Checking cache dependency configuration...

findstr /C:"cache-dependency-path:" "%SHARED_WORKFLOW_FILE%" >nul
if !errorlevel! equ 0 (
    echo [OK] Found cache dependency path for precise cache keys
) else (
    echo [X] Missing cache dependency path
    exit /b 1
)

echo.
echo === All Artifact Handling Tests Passed ===
echo Current artifact patterns are compatible with unified workflow design
echo.

endlocal
