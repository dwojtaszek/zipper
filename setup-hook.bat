@echo off
REM Installs the version-controlled git hooks from .github\hooks\ into the
REM correct hooks directory for the current checkout (supports worktrees).
REM
REM Hooks:
REM   pre-commit   - dotnet format + unit tests (staged snapshot, DocsDriftTests fast-path for docs-only)
REM   post-commit  - records tree-verified success marker for pre-push skip-if-recent
REM   pre-push     - unit tests + mutation smoke + conditional basic E2E smoke
REM
REM Usage: setup-hook.bat [--quiet]

setlocal

set "QUIET=0"
if "%~1"=="--quiet" set "QUIET=1"
if "%~1"=="-q" set "QUIET=1"

pushd "%~dp0"

set "TEMPLATE_DIR=.github\hooks"

REM Works for both normal repos (returns .git) and worktrees (returns the
REM shared common dir under the main repo).
set "GIT_COMMON_DIR="
for /f "delims=" %%G in ('git rev-parse --git-common-dir 2^>nul') do set "GIT_COMMON_DIR=%%G"
if "%GIT_COMMON_DIR%"=="" (
    if "%QUIET%"=="0" echo Error: not inside a git repository.
    popd
    exit /b 1
)
set "HOOK_DIR=%GIT_COMMON_DIR%\hooks"

if not exist "%HOOK_DIR%" mkdir "%HOOK_DIR%"

call :install_hook pre-commit
call :install_hook post-commit
call :install_hook pre-push

if "%QUIET%"=="0" (
    echo.
    echo Done. Hooks installed from %TEMPLATE_DIR%\ into %HOOK_DIR%\
    echo   pre-commit  -^> format + unit tests (DocsDriftTests fast-path for docs-only)
    echo   post-commit -^> writes tree-verified marker for pre-push skip optimization
    echo   pre-push    -^> unit tests (skippable) + mutation smoke + conditional E2E (6 cases)
    echo.
    echo Bypass with: git commit --no-verify / git push --no-verify
)
popd
endlocal
exit /b 0

:install_hook
set "name=%~1"
set "src=%TEMPLATE_DIR%\%name%"
set "dst=%HOOK_DIR%\%name%"
if not exist "%src%" (
    if "%QUIET%"=="0" echo Warning: template %src% not found -- skipping %name%
    goto :eof
)
copy /y "%src%" "%dst%" >nul
if errorlevel 1 (
    echo Error: failed to copy %src% -^> %dst%
    echo   (check permissions, disk space, and that the destination is writable)
    exit /b 1
)
if "%QUIET%"=="0" echo Installed: %dst%
goto :eof
