@echo off
setlocal enabledelayedexpansion

REM Test script to validate current build and test workflows (Windows)
REM This script validates that the existing workflows function correctly

echo === Workflow Validation Test (Windows) ===
echo Testing current build.yml and test.yml workflows

REM Check if workflow files exist
echo 1. Checking workflow file existence...
if exist ".github\workflows\build.yml" (
    echo ✓ build.yml exists
) else (
    echo ✗ build.yml missing
    exit /b 1
)

if exist ".github\workflows\test.yml" (
    echo ✓ test.yml exists
) else (
    echo ✗ test.yml missing
    exit /b 1
)

REM Check if .editorconfig exists
echo 2. Checking .editorconfig...
if exist ".editorconfig" (
    echo ✓ .editorconfig exists
) else (
    echo ✗ .editorconfig missing
    exit /b 1
)

REM Check if test scripts exist
echo 3. Checking test scripts...
if exist "tests\run-tests.bat" (
    echo ✓ run-tests.bat exists
) else (
    echo ✗ run-tests.bat missing
    exit /b 1
)

REM Check .version file
echo 4. Checking .version file...
if exist ".version" (
    echo ✓ .version exists
    set /p VERSION_CONTENT=<.version
    echo   Current version: !VERSION_CONTENT!
) else (
    echo ✗ .version missing
    exit /b 1
)

REM Check project structure
echo 5. Checking project structure...
if exist "src" (
    echo ✓ src directory exists
    if exist "src\Zipper.csproj" (
        echo ✓ Zipper.csproj exists
    ) else (
        echo ✗ Zipper.csproj missing
        exit /b 1
    )
) else (
    echo ✗ src directory missing
    exit /b 1
)

REM Validate build.yml structure
echo 6. Validating build.yml structure...
findstr /C:"name: Build and Release" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ build.yml has correct name
) else (
    echo ✗ build.yml name incorrect
    exit /b 1
)

findstr /C:"on:" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ build.yml has triggers
) else (
    echo ✗ build.yml missing triggers
    exit /b 1
)

findstr /C:"jobs:" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ build.yml has jobs section
) else (
    echo ✗ build.yml missing jobs section
    exit /b 1
)

REM Validate test.yml structure
echo 7. Validating test.yml structure...
findstr /C:"name: Run Tests" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ test.yml has correct name
) else (
    echo ✗ test.yml name incorrect
    exit /b 1
)

findstr /C:"matrix:" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ test.yml has matrix strategy
) else (
    echo ✗ test.yml missing matrix strategy
    exit /b 1
)

REM Check for required actions in build.yml
echo 8. Validating required actions in build.yml...
set ACTIONS_FOUND=0

findstr /C:"actions/checkout@v3" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found actions/checkout@v3
    set /a ACTIONS_FOUND+=1
) else (
    echo ✗ Missing actions/checkout@v3
)

findstr /C:"actions/setup-dotnet@v3" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found actions/setup-dotnet@v3
    set /a ACTIONS_FOUND+=1
) else (
    echo ✗ Missing actions/setup-dotnet@v3
)

findstr /C:"actions/cache@v3" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found actions/cache@v3
    set /a ACTIONS_FOUND+=1
) else (
    echo ✗ Missing actions/cache@v3
)

findstr /C:"actions/upload-artifact@v4" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found actions/upload-artifact@v4
    set /a ACTIONS_FOUND+=1
) else (
    echo ✗ Missing actions/upload-artifact@v4
)

findstr /C:"softprops/action-gh-release@v2" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found softprops/action-gh-release@v2
    set /a ACTIONS_FOUND+=1
) else (
    echo ✗ Missing softprops/action-gh-release@v2
)

if !ACTIONS_FOUND! lss 5 (
    echo ✗ Some required actions are missing
    exit /b 1
)

REM Check for required actions in test.yml
echo 9. Validating required actions in test.yml...
set TEST_ACTIONS_FOUND=0

findstr /C:"actions/checkout@v3" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found actions/checkout@v3
    set /a TEST_ACTIONS_FOUND+=1
) else (
    echo ✗ Missing actions/checkout@v3
)

findstr /C:"actions/setup-dotnet@v3" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found actions/setup-dotnet@v3
    set /a TEST_ACTIONS_FOUND+=1
) else (
    echo ✗ Missing actions/setup-dotnet@v3
)

if !TEST_ACTIONS_FOUND! lss 2 (
    echo ✗ Some required actions are missing
    exit /b 1
)

echo.
echo === All Workflow Validation Tests Passed ===
echo Current workflows are properly structured and ready for unification