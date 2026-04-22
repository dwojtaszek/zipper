@echo off
REM Installs the version-controlled git hooks from .github\hooks\ into .git\hooks\
REM
REM Hooks:
REM   pre-commit  - dotnet format + unit tests (staged snapshot, skips docs-only)
REM   pre-push    - unit tests + basic E2E smoke (skips unit tests if pre-commit just ran)
REM
REM Usage: setup-hook.bat

setlocal

set "HOOK_DIR=.git\hooks"
set "TEMPLATE_DIR=.github\hooks"

if not exist ".git" (
    echo Error: not a git repository ^(no .git directory^). 1^>^&2
    exit /b 1
)

if not exist "%HOOK_DIR%" mkdir "%HOOK_DIR%"

call :install_hook pre-commit
call :install_hook pre-push

if exist "%TEMPLATE_DIR%\pre-push.ps1" (
    copy /y "%TEMPLATE_DIR%\pre-push.ps1" "%HOOK_DIR%\pre-push.ps1" >nul
    echo Installed: %HOOK_DIR%\pre-push.ps1 ^(PowerShell fallback^)
)

echo.
echo Done. Hooks installed from %TEMPLATE_DIR%\ into %HOOK_DIR%\
echo   pre-commit -^> format + unit tests ^(stashed staged snapshot^)
echo   pre-push   -^> unit tests ^(skippable^) + basic E2E ^(5 cases^)
echo.
echo Bypass with: git commit --no-verify / git push --no-verify
endlocal
exit /b 0

:install_hook
set "name=%~1"
set "src=%TEMPLATE_DIR%\%name%"
set "dst=%HOOK_DIR%\%name%"
if not exist "%src%" (
    echo Warning: template %src% not found -- skipping %name% 1^>^&2
    goto :eof
)
copy /y "%src%" "%dst%" >nul
echo Installed: %dst%
goto :eof
