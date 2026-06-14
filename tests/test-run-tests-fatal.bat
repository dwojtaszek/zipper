@echo off
REM Regression guard for #442: run-tests.bat must exit non-zero if a test fails.

echo [ INFO ] Running meta-test to verify run-tests.bat fatal error behavior...
call "%~dp0run-tests.bat" --meta-test-fail-only

if errorlevel 1 (
    echo [ SUCCESS ] run-tests.bat correctly exited non-zero on failure.
    exit /b 0
) else (
    echo [ ERROR ] run-tests.bat exited 0 despite a deliberate failure!
    exit /b 1
)
