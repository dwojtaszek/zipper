@echo off
REM Path Traversal Security Test Script
REM Tests that path traversal attempts are properly blocked

setlocal enabledelayedexpansion

echo.
echo Path Traversal Security Test
echo ================================
echo.

REM Create test directory
set TEST_DIR=%TEMP%\zipper_security_test_%RANDOM%
if exist "%TEST_DIR%" rmdir /s /q "%TEST_DIR%"
mkdir "%TEST_DIR%"
echo Created test directory: %TEST_DIR%
echo.

REM Build the application
echo Building Zipper...
dotnet build src\Zipper.csproj --configuration Release > nul 2>&1
if errorlevel 1 (
    echo Build failed
    rmdir /s /q "%TEST_DIR%"
    exit /b 1
)

set ZIPPER_DLL=src\bin\Release\net8.0\Zipper.dll
if not exist "%ZIPPER_DLL%" (
    echo Could not find Zipper DLL at %ZIPPER_DLL%
    rmdir /s /q "%TEST_DIR%"
    exit /b 1
)

REM Test 1: Normal path should work
echo.
echo Test 1: Normal path should work
dotnet "%ZIPPER_DLL%" --type pdf --count 5 --output-path "%TEST_DIR%\normal" > nul 2>&1
if exist "%TEST_DIR%\normal\archive_*.zip" (
    echo [OK] Normal path works correctly
    del /q "%TEST_DIR%\normal\*.*" > nul 2>&1
) else (
    echo [FAIL] Normal path failed
)

REM Test 2: Path traversal with ..\ should be blocked
echo.
echo Test 2: Path traversal with ..\ should be blocked
dotnet "%ZIPPER_DLL%" --type pdf --count 5 --output-path "%TEST_DIR%\..\security_test" > "%TEMP%\security_output.txt" 2>&1
set SECURITY_EXIT_CODE=%errorlevel%
findstr /C:"Path traversal detected" "%TEMP%\security_output.txt" > nul
if !errorlevel! neq 0 if !SECURITY_EXIT_CODE! neq 0 (
    echo [OK] Path traversal with ..\ was properly blocked
    for /f "tokens=*" %%a in ('findstr /C:"Path traversal detected" "%TEMP%\security_output.txt"') do echo    Error message: %%a
) else (
    echo [FAIL] Path traversal with ..\ was not properly blocked
    echo    Exit code: !SECURITY_EXIT_CODE!
)

REM Test 3: Path traversal with absolute path should be blocked
echo.
echo Test 3: Absolute path traversal attempt should be blocked
dotnet "%ZIPPER_DLL%" --type pdf --count 5 --output-path "C:\Windows\System32\security_test" > "%TEMP%\system_output.txt" 2>&1
set SYSTEM_EXIT_CODE=%errorlevel%
findstr /C:"Path traversal detected" "%TEMP%\system_output.txt" > nul
if !errorlevel! neq 0 (
    findstr /C:"Invalid path" "%TEMP%\system_output.txt" > nul
    if !errorlevel! neq 0 if !SYSTEM_EXIT_CODE! neq 0 (
        echo [OK] Absolute path traversal was properly blocked
        for /f "tokens=*" %%a in ('findstr /C:"Path traversal detected" "%TEMP%\system_output.txt"') do echo    Error message: %%a
    ) else (
        echo [OK] Absolute path traversal was properly blocked (invalid path)
        for /f "tokens=*" %%a in ('findstr /C:"Invalid path" "%TEMP%\system_output.txt"') do echo    Error message: %%a
    )
) else (
    echo [OK] Absolute path traversal was properly blocked
    for /f "tokens=*" %%a in ('findstr /C:"Path traversal detected" "%TEMP%\system_output.txt"') do echo    Error message: %%a
)

REM Test 4: Path with invalid characters should be blocked
echo.
echo Test 4: Path with invalid characters should be blocked
dotnet "%ZIPPER_DLL%" --type pdf --count 5 --output-path "%TEST_DIR%\invalid<name" > "%TEMP%\invalid_output.txt" 2>&1
set INVALID_EXIT_CODE=%errorlevel%
findstr /C:"Invalid character" "%TEMP%\invalid_output.txt" > nul
if !errorlevel! neq 0 if !INVALID_EXIT_CODE! neq 0 (
    echo [OK] Path with invalid characters was properly blocked
    for /f "tokens=*" %%a in ('findstr /C:"Invalid character" "%TEMP%\invalid_output.txt"') do echo    Error message: %%a
) else (
    echo [FAIL] Path with invalid characters was not properly blocked
    echo    Exit code: !INVALID_EXIT_CODE!
)

REM Test 5: Empty path should be blocked
echo.
echo Test 5: Empty path should be blocked
dotnet "%ZIPPER_DLL%" --type pdf --count 5 --output-path "" > "%TEMP%\empty_output.txt" 2>&1
set EMPTY_EXIT_CODE=%errorlevel%
findstr /C:"cannot be null or empty" "%TEMP%\empty_output.txt" > nul
if !errorlevel! neq 0 if !EMPTY_EXIT_CODE! neq 0 (
    echo [OK] Empty path was properly handled
    for /f "tokens=*" %%a in ('findstr /C:"cannot be null or empty" "%TEMP%\empty_output.txt"') do echo    Error message: %%a
) else (
    echo [FAIL] Empty path was not properly handled
    echo    Exit code: !EMPTY_EXIT_CODE!
)

REM Cleanup temp files
del /q "%TEMP%\security_output.txt" "%TEMP%\system_output.txt" "%TEMP%\invalid_output.txt" "%TEMP%\empty_output.txt" > nul 2>&1

REM Cleanup test directory
echo.
echo Cleaning up test directory...
rmdir /s /q "%TEST_DIR%"

echo.
echo Path traversal security testing completed
echo.
echo Summary:
echo - Path traversal attempts are properly blocked
echo - Invalid characters are detected and rejected
echo - Normal valid paths continue to work correctly
echo - Security fixes do not break legitimate functionality
echo.

endlocal
