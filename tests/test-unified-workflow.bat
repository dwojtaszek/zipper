@echo off
REM Test script to validate the new unified build-and-test.yml workflow
REM This script validates that the new unified workflow functions correctly

setlocal enabledelayedexpansion

echo.
echo === Unified Workflow Validation Test (Windows) ===
echo Testing new build-and-test.yml workflow
echo.

REM Check if unified workflow exists
echo 1. Checking unified workflow file existence...
if exist ".github\workflows\build-and-test.yml" (
    echo [OK] build-and-test.yml exists
) else (
    echo [FAIL] build-and-test.yml missing
    exit /b 1
)

REM Check if old workflows still exist (they should until cleanup phase)
echo.
echo 2. Checking old workflow files...
if exist ".github\workflows\build.yml" (
    echo [OK] build.yml still exists ^(expected before cleanup^)
) else (
    echo [WARN] build.yml already removed ^(cleanup may have been done^)
)

if exist ".github\workflows\test.yml" (
    echo [OK] test.yml still exists ^(expected before cleanup^)
) else (
    echo [WARN] test.yml already removed ^(cleanup may have been done^)
)

REM Validate unified workflow structure
echo.
echo 3. Validating unified workflow structure...
findstr /C:"name: Build and Test" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Unified workflow name incorrect
    exit /b 1
) else (
    echo [OK] Unified workflow has correct name
)

REM Check for all required jobs
echo.
echo 4. Checking for all required jobs...
set REQUIRED_JOBS=lint: build: test: release:
for %%J in (%REQUIRED_JOBS%) do (
    findstr /C:"%%J" ".github\workflows\build-and-test.yml" > nul
    if errorlevel 1 (
        echo [FAIL] Missing %%J job
        exit /b 1
    ) else (
        echo [OK] Found %%J job
    )
)

REM Check job dependencies
echo.
echo 5. Checking job dependencies...
findstr /C:"needs: [prepare, lint]" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Build job missing correct dependencies
    exit /b 1
) else (
    echo [OK] Build job depends on prepare and lint
)

findstr /C:"needs: build" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Test job missing build dependency
    exit /b 1
) else (
    echo [OK] Test job depends on build
)

findstr /C:"needs: [prepare, lint, build, test]" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Release job missing dependencies
    exit /b 1
) else (
    echo [OK] Release job depends on all previous jobs
)

REM Check for matrix strategy in build job
echo.
echo 6. Checking build job matrix strategy...
findstr /A:10 /C:"build:" ".github\workflows\build-and-test.yml" | findstr /C:"matrix:" > nul
if errorlevel 1 (
    echo [FAIL] Build job missing matrix strategy
    exit /b 1
) else (
    echo [OK] Build job has matrix strategy
)

REM Check for matrix strategy in test job
echo.
echo 7. Checking test job matrix strategy...
findstr /A:10 /C:"test:" ".github\workflows\build-and-test.yml" | findstr /C:"matrix:" > nul
if errorlevel 1 (
    echo [FAIL] Test job missing matrix strategy
    exit /b 1
) else (
    echo [OK] Test job has matrix strategy
)

REM Check platforms in matrix
echo.
echo 8. Checking platform support...
set PLATFORMS=win-x64 linux-x64 osx-arm64
for %%P in (%PLATFORMS%) do (
    findstr /C:"%%P" ".github\workflows\build-and-test.yml" > nul
    if errorlevel 1 (
        echo [FAIL] Missing %%P in workflow
        exit /b 1
    ) else (
        echo [OK] Found %%P in workflow
    )
)

REM Check for artifact handling
echo.
echo 9. Checking artifact handling...
findstr /C:"actions/upload-artifact@v" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Missing upload-artifact
    exit /b 1
) else (
    echo [OK] Uses upload-artifact
)

findstr /C:"actions/download-artifact@v" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Missing download-artifact
    exit /b 1
) else (
    echo [OK] Uses download-artifact
)

REM Check for artifact retention
echo.
echo 10. Checking artifact retention configuration...
findstr /C:"retention-days: 7" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Missing artifact retention configuration
    exit /b 1
) else (
    echo [OK] Artifact retention set to 7 days
)

REM Check for branch triggers
echo.
echo 11. Checking branch triggers...
findstr /C:"branches:" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Missing branch triggers
    exit /b 1
) else (
    echo [OK] Has branch triggers configured
)

findstr /C:"main" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Missing main branch trigger
    exit /b 1
) else (
    echo [OK] Main branch trigger found
)

REM Check for release conditions
echo.
echo 12. Checking release conditions...
findstr /C:"if: startsWith(github.ref, 'refs/tags/v')" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Release job missing tag condition
    exit /b 1
) else (
    echo [OK] Release job runs on tags
)

REM Check for permissions
echo.
echo 13. Checking release permissions...
findstr /C:"permissions:" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Release job missing permissions
    exit /b 1
) else (
    echo [OK] Release job has permissions
)

findstr /C:"contents: write" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Release job missing contents write permission
    exit /b 1
) else (
    echo [OK] Release job has contents write permission
)

REM Check for caching
echo.
echo 14. Checking caching configuration...
findstr /C:"actions/cache@v" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Missing caching
    exit /b 1
) else (
    echo [OK] Uses caching
)

REM Check for version handling
echo.
echo 15. Checking version handling...
findstr /C:"Set Version" ".github\workflows\build-and-test.yml" > nul
if errorlevel 1 (
    echo [FAIL] Missing version handling
    exit /b 1
) else (
    echo [OK] Has version handling
)

echo.
echo === All Unified Workflow Validation Tests Passed ===
echo New build-and-test.yml workflow is properly configured and ready
echo.

endlocal
