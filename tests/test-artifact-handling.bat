@echo off
setlocal enabledelayedexpansion

REM Test script to validate artifact handling in current workflows (Windows)
REM This script checks artifact creation, naming, and retention patterns

echo === Artifact Handling Test (Windows) ===
echo Testing artifact creation and handling patterns

REM Check build.yml for artifact patterns
echo 1. Analyzing build.yml artifact patterns...

REM Check for artifact upload steps
findstr /C:"actions/upload-artifact@v4" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Uses actions/upload-artifact@v4
) else (
    echo ✗ Missing actions/upload-artifact@v4
    exit /b 1
)

REM Check for artifact names
echo 2. Checking platform-specific artifact configuration...

findstr /C:"win-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found win-x64 configuration
) else (
    echo ✗ Missing win-x64 configuration
    exit /b 1
)

findstr /C:"linux-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found linux-x64 configuration
) else (
    echo ✗ Missing linux-x64 configuration
    exit /b 1
)

findstr /C:"osx-arm64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found osx-arm64 configuration
) else (
    echo ✗ Missing osx-arm64 configuration
    exit /b 1
)

findstr /C:"zipper-win-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found zipper-win-x64 naming
) else (
    echo ✗ Missing zipper-win-x64 naming
    exit /b 1
)

findstr /C:"zipper-linux-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found zipper-linux-x64 naming
) else (
    echo ✗ Missing zipper-linux-x64 naming
    exit /b 1
)

findstr /C:"zipper-osx-arm64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found zipper-osx-arm64 naming
) else (
    echo ✗ Missing zipper-osx-arm64 naming
    exit /b 1
)

REM Check for caching strategy
echo 3. Analyzing caching strategy...
findstr /C:"actions/cache@v3" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Uses actions/cache@v3
) else (
    echo ✗ Missing actions/cache@v3
    exit /b 1
)

REM Check cache paths
echo 4. Checking cache paths...

findstr /C:"publish/win-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found cache path for publish/win-x64
) else (
    echo ✗ Missing cache path for publish/win-x64
    exit /b 1
)

findstr /C:"publish/linux-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found cache path for publish/linux-x64
) else (
    echo ✗ Missing cache path for publish/linux-x64
    exit /b 1
)

findstr /C:"publish/osx-arm64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found cache path for publish/osx-arm64
) else (
    echo ✗ Missing cache path for publish/osx-arm64
    exit /b 1
)

REM Check for artifact download in release job
echo 5. Checking release job artifact handling...
findstr /C:"actions/download-artifact@v4" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Uses actions/download-artifact@v4
) else (
    echo ✗ Missing actions/download-artifact@v4
    exit /b 1
)

REM Check release file patterns
echo 6. Checking release file patterns...

findstr /C:"artifacts/zipper-win-x64/zipper-win-x64.exe" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found release file pattern: artifacts/zipper-win-x64/zipper-win-x64.exe
) else (
    echo ✗ Missing release file pattern: artifacts/zipper-win-x64/zipper-win-x64.exe
    exit /b 1
)

findstr /C:"artifacts/zipper-linux-x64/zipper-linux-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found release file pattern: artifacts/zipper-linux-x64/zipper-linux-x64
) else (
    echo ✗ Missing release file pattern: artifacts/zipper-linux-x64/zipper-linux-x64
    exit /b 1
)

findstr /C:"artifacts/zipper-osx-arm64/zipper-osx-arm64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found release file pattern: artifacts/zipper-osx-arm64/zipper-osx-arm64
) else (
    echo ✗ Missing release file pattern: artifacts/zipper-osx-arm64/zipper-osx-arm64
    exit /b 1
)

REM Check build output directory structure
echo 7. Validating build output structure...
findstr /C:"publish/" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Uses publish/ directory structure
) else (
    echo ✗ Missing publish/ directory structure
    exit /b 1
)

REM Check for PDB file cleanup
echo 8. Checking for PDB file cleanup...
findstr /C:"rm *.pdb" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Removes PDB files from artifacts
) else (
    echo ✗ Missing PDB file cleanup
    exit /b 1
)

REM Check for executable renaming
echo 9. Checking executable renaming patterns...

findstr /C:"mv publish/win-x64/Zipper.exe" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found Windows renaming pattern
) else (
    echo ✗ Missing Windows renaming pattern
    exit /b 1
)

findstr /C:"mv publish/linux-x64/Zipper" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found Linux renaming pattern
) else (
    echo ✗ Missing Linux renaming pattern
    exit /b 1
)

findstr /C:"mv publish/osx-arm64/Zipper" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found macOS renaming pattern
) else (
    echo ✗ Missing macOS renaming pattern
    exit /b 1
)

REM Check for conditional builds based on cache
echo 10. Checking conditional build logic...
findstr /C:"cache-hit != 'true'" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Uses conditional builds based on cache
) else (
    echo ✗ Missing conditional build logic
    exit /b 1
)

echo.
echo === All Artifact Handling Tests Passed ===
echo Current artifact patterns are compatible with unified workflow design