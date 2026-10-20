@echo off
REM Test script to validate the new unified build-and-test.yml workflow
REM This script validates that the new unified workflow functions correctly

setlocal enabledelayedexpansion

echo.
echo === Unified Workflow Validation Test (Windows) ===
echo Testing new build-and-test.yml workflow
echo.

REM The shared job graph lives in a reusable workflow; both callers (PR Checks
REM and Build and Test) invoke it, so assertions must target the right file.
set SHARED_WORKFLOW_FILE=.github\workflows\ci.yml
set CALLER_WORKFLOW_FILE=.github\workflows\build-and-test.yml
set PR_WORKFLOW_FILE=.github\workflows\pr.yml

REM Check if the workflow files exist
echo 1. Checking workflow files exist...
if exist "%SHARED_WORKFLOW_FILE%" (
    echo [OK] ci.yml exists
) else (
    echo [FAIL] ci.yml missing
    exit /b 1
)

if exist "%CALLER_WORKFLOW_FILE%" (
    echo [OK] build-and-test.yml exists
) else (
    echo [FAIL] build-and-test.yml missing
    exit /b 1
)

if exist "%PR_WORKFLOW_FILE%" (
    echo [OK] pr.yml exists
) else (
    echo [FAIL] pr.yml missing
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
findstr /C:"name: Build and Test" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Unified workflow name incorrect
    exit /b 1
) else (
    echo [OK] Unified workflow has correct name
)

REM Check for all required jobs. The shared graph (lint, runner-tests,
REM version-metadata, build-and-test) lives in ci.yml; the caller keeps prepare and tag-and-release.
echo.
echo 4. Checking for all required jobs...
for %%J in (prepare: tag-and-release:) do (
    findstr /C:"%%J" "%CALLER_WORKFLOW_FILE%" > nul
    if errorlevel 1 (
        echo [FAIL] Missing %%J job in build-and-test.yml
        exit /b 1
    ) else (
        echo [OK] Found %%J job in build-and-test.yml
    )
)

for %%J in (lint: runner-tests: version-metadata: build-and-test:) do (
    findstr /C:"%%J" "%SHARED_WORKFLOW_FILE%" > nul
    if errorlevel 1 (
        echo [FAIL] Missing %%J job in ci.yml
        exit /b 1
    ) else (
        echo [OK] Found %%J job in ci.yml
    )
)

findstr /C:"run: bash tests/test-build-version.sh" "%SHARED_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] version-metadata job does not run tests/test-build-version.sh
    exit /b 1
) else (
    echo [OK] version-metadata job runs tests/test-build-version.sh
)

REM The reusable hook and both call sites are what keep the two callers in sync.
echo.
echo 5. Checking reusable workflow wiring...
findstr /C:"workflow_call:" "%SHARED_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] ci.yml must be triggered by workflow_call
    exit /b 1
) else (
    echo [OK] ci.yml is a reusable workflow ^(workflow_call^)
)

findstr /C:"uses: ./.github/workflows/ci.yml" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] build-and-test.yml must call ./.github/workflows/ci.yml
    exit /b 1
) else (
    echo [OK] build-and-test.yml calls ci.yml
)

findstr /C:"uses: ./.github/workflows/ci.yml" "%PR_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] pr.yml must call ./.github/workflows/ci.yml
    exit /b 1
) else (
    echo [OK] pr.yml calls ci.yml
)

REM Check job dependencies
echo.
echo 6. Checking job dependencies...
findstr /C:"needs: lint" "%SHARED_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Build-and-test job missing correct dependencies
    exit /b 1
) else (
    echo [OK] Build-and-test job depends on lint
)

findstr /C:"needs: [prepare, ci]" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Release job missing dependencies
    exit /b 1
) else (
    echo [OK] Release job depends on all previous jobs
)

REM Check for matrix strategy in build job
echo.
echo 7. Checking build-and-test job matrix strategy...
powershell -NoProfile -Command "if ((Select-String -Path '%SHARED_WORKFLOW_FILE%' -Pattern 'build-and-test:' -Context 0,10 | Out-String) -match 'matrix:') { exit 0 } else { exit 1 }" > nul
if errorlevel 1 (
    echo [FAIL] Build-and-test job missing matrix strategy
    exit /b 1
) else (
    echo [OK] Build-and-test job has matrix strategy
)

REM Check platforms in matrix
echo.
echo 8. Checking platform support...
for %%P in (win-x64 linux-x64 osx-arm64) do (
    findstr /C:"%%P" "%SHARED_WORKFLOW_FILE%" > nul
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
findstr /C:"actions/upload-artifact@" "%SHARED_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Missing upload-artifact
    exit /b 1
) else (
    echo [OK] Uses upload-artifact
)

findstr /C:"actions/download-artifact@" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Missing download-artifact
    exit /b 1
) else (
    echo [OK] Uses download-artifact
)

REM Check for artifact retention
echo.
echo 10. Checking artifact retention configuration...
findstr /C:"retention-days: 30" "%SHARED_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Missing artifact retention configuration
    exit /b 1
) else (
    echo [OK] Accepted release artifacts retained for 30 days
)

REM Check for branch triggers
echo.
echo 11. Checking branch triggers...
findstr /C:"branches:" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Missing branch triggers
    exit /b 1
) else (
    echo [OK] Has branch triggers configured
)

findstr /C:"main" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Missing main branch trigger
    exit /b 1
) else (
    echo [OK] Main branch trigger found
)

REM Check for release conditions
echo.
echo 12. Checking release conditions...
findstr /C:"if: needs.prepare.outputs.skip != 'true'" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Release job missing reserved intent condition
    exit /b 1
) else (
    echo [OK] Release job requires an incomplete reserved intent
)

REM Check for permissions
echo.
echo 13. Checking release permissions...
findstr /C:"permissions:" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Release job missing permissions
    exit /b 1
) else (
    echo [OK] Release job has permissions
)

findstr /C:"contents: write" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Release job missing contents write permission
    exit /b 1
) else (
    echo [OK] Release job has contents write permission
)

REM Check for caching
echo.
echo 14. Checking caching configuration...
findstr /S /C:"actions/cache@" ".github\*.yml" > nul
if errorlevel 1 (
    echo [FAIL] Missing caching
    exit /b 1
) else (
    echo [OK] Uses caching
)

REM Check for version handling
echo.
echo 15. Checking version handling...
findstr /C:"python3 .controller/.github/scripts/release.py prepare" "%CALLER_WORKFLOW_FILE%" > nul
if errorlevel 1 (
    echo [FAIL] Missing version handling
    exit /b 1
) else (
    echo [OK] Version handling uses the durable release controller
)

REM Recovery dispatches check out the historical source SHA; the controller
REM itself must come from the triggering ref (the fixed pipeline), so both
REM release jobs need the second checkout at .controller.
powershell -NoProfile -Command "if ((Select-String -Path '%CALLER_WORKFLOW_FILE%' -Pattern 'path: .controller').Count -eq 2) { exit 0 } else { exit 1 }" > nul
if errorlevel 1 (
    echo [FAIL] Release jobs must check out the controller from the triggering ref
    exit /b 1
) else (
    echo [OK] Release controller runs from the triggering ref in both release jobs
)

echo.
echo === All Unified Workflow Validation Tests Passed ===
echo New build-and-test.yml workflow is properly configured and ready
echo.

endlocal
