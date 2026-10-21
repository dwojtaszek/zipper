@echo off
setlocal enabledelayedexpansion

REM Smart local developer CLI wrapper for Zipper (Windows).
REM Delegates to Git Bash + dev.sh when available for diff-aware check/mutation/all/options,
REM and provides native Windows execution for build, test, lint, hooks, and E2E.

set "REPO_ROOT=%~dp0"
pushd "%REPO_ROOT%"

set "COMMAND=%~1"
if "%COMMAND%"=="" set "COMMAND=check"

if "%COMMAND%"=="-h" goto :usage_ok
if "%COMMAND%"=="--help" goto :usage_ok
if "%COMMAND%"=="help" goto :usage_ok

set "_FIRST_CHAR=%COMMAND:~0,1%"
if "%_FIRST_CHAR%"=="-" set "COMMAND=check"

if "%COMMAND%"=="hooks" (
    echo ==^> [dev] Sync local Git hooks
    call "%REPO_ROOT%setup-hook.bat"
    set "EXIT_CODE=!ERRORLEVEL!"
    popd
    exit /b !EXIT_CODE!
)

REM If bash is available, delegate check/mutation/all to dev.sh for full parity
where bash >nul 2>&1
if %ERRORLEVEL% equ 0 (
    if "%COMMAND%"=="check" (
        bash "%REPO_ROOT%dev.sh" %*
        set "EXIT_CODE=!ERRORLEVEL!"
        popd
        exit /b !EXIT_CODE!
    )
    if "%COMMAND%"=="mutation" (
        bash "%REPO_ROOT%dev.sh" %*
        set "EXIT_CODE=!ERRORLEVEL!"
        popd
        exit /b !EXIT_CODE!
    )
    if "%COMMAND%"=="all" (
        bash "%REPO_ROOT%dev.sh" %*
        set "EXIT_CODE=!ERRORLEVEL!"
        popd
        exit /b !EXIT_CODE!
    )
) else (
    if "%COMMAND%"=="mutation" (
        echo Warning: 'mutation' smoke requires Git Bash ^(bash on PATH^); skipping mutation smoke on Windows without bash. 1>&2
        popd
        exit /b 0
    )
    if "%COMMAND%"=="check" (
        echo Warning: Git Bash not found; falling back to native Windows verification gates. 1>&2
        set "COMMAND=all_native"
    )
    if "%COMMAND%"=="all" (
        echo Warning: Git Bash not found; falling back to native Windows verification gates. 1>&2
        set "COMMAND=all_native"
    )
)

if "%COMMAND%"=="all_native" (
    echo ==^> [dev] Build solution ^(Release^)
    dotnet build zipper.sln -c Release
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    echo ==^> [dev] Verify code formatting
    dotnet format --verify-no-changes src/
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    echo ==^> [dev] Run unit + property tests
    dotnet test src\Zipper.Tests\Zipper.Tests.csproj --no-build -c Release
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    echo ==^> [dev] Run Roslyn analyzer tests
    dotnet test src\Zipper.Analyzers.Tests\Zipper.Analyzers.Tests.csproj --no-build -c Release
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    call "%REPO_ROOT%tests\_zipper-cli.bat"
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    echo ==^> [dev] Run basic E2E smoke
    call "%REPO_ROOT%tests\run-e2e-basic.bat"
    set "EXIT_CODE=!ERRORLEVEL!"
    popd
    exit /b !EXIT_CODE!
)

if "%COMMAND%"=="test" (
    echo ==^> [dev] Build solution ^(Release^)
    dotnet build zipper.sln -c Release
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    echo ==^> [dev] Run unit + property tests
    dotnet test src\Zipper.Tests\Zipper.Tests.csproj --no-build -c Release
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    if "%~2"=="--all" (
        echo ==^> [dev] Run Roslyn analyzer tests
        dotnet test src\Zipper.Analyzers.Tests\Zipper.Analyzers.Tests.csproj --no-build -c Release
        if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    )
    popd
    exit /b 0
)

if "%COMMAND%"=="e2e" (
    echo ==^> [dev] Build solution ^(Release^)
    dotnet build zipper.sln -c Release
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    call "%REPO_ROOT%tests\_zipper-cli.bat"
    if !ERRORLEVEL! neq 0 ( popd & exit /b !ERRORLEVEL! )
    if "%~2"=="--full" (
        echo ==^> [dev] Run full E2E suite
        call "%REPO_ROOT%tests\run-tests.bat"
    ) else (
        echo ==^> [dev] Run basic E2E smoke
        call "%REPO_ROOT%tests\run-e2e-basic.bat"
    )
    set "EXIT_CODE=!ERRORLEVEL!"
    popd
    exit /b !EXIT_CODE!
)

if "%COMMAND%"=="lint" (
    echo ==^> [dev] Verify code formatting
    dotnet format --verify-no-changes src/
    set "EXIT_CODE=!ERRORLEVEL!"
    popd
    exit /b !EXIT_CODE!
)

echo Error: Unknown command '%COMMAND%'. 1>&2
echo Usage: dev.bat [check^|test^|e2e^|mutation^|lint^|hooks^|all^|help] [options] 1>&2
popd
exit /b 1

:usage_ok
echo Usage: dev.bat [check^|test^|e2e^|mutation^|lint^|hooks^|all^|help] [options]
popd
exit /b 0
