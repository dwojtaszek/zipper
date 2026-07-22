@echo off
setlocal enabledelayedexpansion

set "SCRIPT_DIR=%~dp0"
set "PROJECT_PATH=%SCRIPT_DIR%..\src\Zipper.csproj"

echo Testing Deterministic property...
for /f "usebackq delims=" %%i in (`dotnet msbuild "%PROJECT_PATH%" -getProperty:Deterministic`) do set "DETERMINISTIC=%%i"
if not "!DETERMINISTIC!"=="true" (
    echo FAIL: Deterministic is not true. Got '!DETERMINISTIC!'
    exit /b 1
)

echo Testing ContinuousIntegrationBuild property (CI=true)...
set "CI=true"
for /f "usebackq delims=" %%i in (`dotnet msbuild "%PROJECT_PATH%" -getProperty:ContinuousIntegrationBuild`) do set "CI_BUILD=%%i"
if not "!CI_BUILD!"=="true" (
    echo FAIL: ContinuousIntegrationBuild is not true when CI=true. Got '!CI_BUILD!'
    exit /b 1
)
set "CI="

echo Testing ContinuousIntegrationBuild property (GITHUB_ACTIONS=true)...
set "GITHUB_ACTIONS=true"
for /f "usebackq delims=" %%i in (`dotnet msbuild "%PROJECT_PATH%" -getProperty:ContinuousIntegrationBuild`) do set "GH_BUILD=%%i"
if not "!GH_BUILD!"=="true" (
    echo FAIL: ContinuousIntegrationBuild is not true when GITHUB_ACTIONS=true. Got '!GH_BUILD!'
    exit /b 1
)
set "GITHUB_ACTIONS="

echo PASS: Build properties are correct.
exit /b 0
