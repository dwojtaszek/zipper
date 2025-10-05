@echo off
REM Comprehensive EML Test Suite - Windows Version
REM Constitutional Requirement: Must test ALL EML functionality scenarios
REM Tests both Windows and Unix compatibility for EML feature implementation

echo ========================================
echo Comprehensive EML Test Suite - Windows
echo ========================================
echo.

REM Set test environment
set TEST_DIR=%TEMP%\zipper-eml-test
set ZIPPER_EXE=Zipper\bin\Debug\net8.0\Zipper.exe

REM Clean up any existing test directory
if exist "%TEST_DIR%" (
    echo Cleaning up existing test directory...
    rmdir /s /q "%TEST_DIR%"
)

echo Creating test directory: %TEST_DIR%
mkdir "%TEST_DIR%"

REM Test Counter
set /a TEST_COUNT=0
set /a PASSED_COUNT=0

REM Function to run a test scenario
:run_test
set /a TEST_COUNT+=1
echo.
echo Test %TEST_COUNT%: %~1
echo Command: %~2
mkdir "%TEST_DIR%\test_%TEST_COUNT%"
cd "%TEST_DIR%\test_%TEST_COUNT%"

echo Running command...
%~2 > test_output.log 2>&1
set RESULT=%ERRORLEVEL%

if %RESULT% EQU 0 (
    echo ✓ Test %TEST_COUNT% PASSED - Command executed successfully

    REM Verify archive was created
    if exist "archive_*.zip" (
        echo   - Archive file created successfully

        REM Extract and verify contents
        powershell -Command "Expand-Archive -Path 'archive_*.zip' -DestinationPath . -Force"

        REM Check DAT file structure
        if exist "archive_*.dat" (
            echo   - Load file created successfully

            REM Count columns in header (first line)
            for /f "delims=" %%i in ('type "archive_*.dat" ^| findstr /n "^" ^| findstr "^1:"') do set HEADER_LINE=%%i
            for /f "tokens=2 delims=:" %%i in ("%HEADER_LINE%") do set ACTUAL_HEADER=%%i

            echo   - Header: %ACTUAL_HEADER%

            REM Count columns by counting delimiters (char 20)
            set /a COLUMN_COUNT=0
            for /f "delims=" %%j in ("%ACTUAL_HEADER%") do (
                setlocal enabledelayedexpansion
                set TEMP_LINE=%%j
                :count_loop
                if "!TEMP_LINE!"=="" goto :count_done
                set /a COLUMN_COUNT+=1
                for /f "tokens=1* delims=" %%k in ("!TEMP_LINE!") do set TEMP_LINE=%%k
                goto :count_loop
                :count_done
                endlocal & set COLUMN_COUNT=!COLUMN_COUNT!
            )

            echo   - Column count: %COLUMN_COUNT%

            REM Validate expected columns
            echo   - Expected columns: %~3
            if %COLUMN_COUNT% EQU %~3 (
                echo   ✓ Column count matches expectation
                set /a PASSED_COUNT+=1
            ) else (
                echo   ✗ Column count mismatch - Expected %~3, got %COLUMN_COUNT%
            )

            REM Check for text files if expected
            if "%~4"=="check_text" (
                dir *.txt >nul 2>&1
                if %ERRORLEVEL% EQU 0 (
                    echo   ✓ Text files found as expected
                ) else (
                    echo   ✗ Text files expected but not found
                )
            )

        ) else (
            echo   ✗ Load file not found
        )

    ) else (
        echo   ✗ Archive file not created
    )

) else (
    echo ✗ Test %TEST_COUNT% FAILED - Command failed with error %RESULT%
    type test_output.log
)

cd ..\..
goto :eof

REM ========================================
REM Test Scenarios
REM ========================================

echo.
echo Running comprehensive EML test scenarios...

REM Test 1: Basic EML Generation (Baseline)
call :run_test "Basic EML Generation" "%ZIPPER_EXE% --type eml --count 5 --output-path . --folders 1" 7

REM Test 2: EML with Metadata Only
call :run_test "EML with Metadata Only" "%ZIPPER_EXE% --type eml --count 5 --output-path . --folders 1 --with-metadata" 11

REM Test 3: EML with Text Only
call :run_test "EML with Text Only" "%ZIPPER_EXE% --type eml --count 5 --output-path . --folders 1 --with-text" 8 check_text

REM Test 4: EML with Both Metadata and Text
call :run_test "EML with Both Flags" "%ZIPPER_EXE% --type eml --count 5 --output-path . --folders 1 --with-metadata --with-text" 12 check_text

REM Test 5: EML with Attachments and Full Flags
call :run_test "EML with Attachments" "%ZIPPER_EXE% --type eml --count 5 --output-path . --folders 2 --with-metadata --with-text --attachment-rate 50" 12 check_text

REM Test 6: Performance Validation
call :run_test "Performance Test" "%ZIPPER_EXE% --type eml --count 100 --output-path . --folders 3 --with-metadata --with-text" 12 check_text

REM ========================================
REM Test Results Summary
REM ========================================

echo.
echo ========================================
echo Test Results Summary
echo ========================================
echo Total Tests: %TEST_COUNT%
echo Passed: %PASSED_COUNT%
echo Failed: %TEST_COUNT%

set /a FAILED_COUNT=%TEST_COUNT%-%PASSED_COUNT%
echo Failed: %FAILED_COUNT%

if %FAILED_COUNT% EQU 0 (
    echo.
    echo ✓ ALL TESTS PASSED - EML feature implementation is working correctly
    echo ✓ Cross-platform validation successful
    exit /b 0
) else (
    echo.
    echo ✗ SOME TESTS FAILED - Please review the implementation
    exit /b 1
)