@echo off
REM Performance regression test script for Zipper (Windows)
REM This script runs performance tests to ensure no regressions were introduced

setlocal enabledelayedexpansion

REM Test configuration
set TEST_OUTPUT_DIR=.\performance-results
for /f "tokens=2 delims==" %%a in ('wmic OS Get localdatetime /value') do set "dt=%%a"
set TIMESTAMP=%dt:~0,8%_%dt:~8,6%
set REPORT_DIR=%TEST_OUTPUT_DIR%\%TIMESTAMP%
set PROJECT=Zipper\Zipper.Tests\Zipper.Tests.csproj

REM Create output directory
if not exist "%REPORT_DIR%" mkdir "%REPORT_DIR%"

echo [ INFO ] Starting Zipper performance regression tests...
echo [ INFO ] Platform: Windows
echo [ INFO ] Results directory: %REPORT_DIR%

REM Function to run unit tests with performance focus
echo [ INFO ] Running performance-focused unit tests...

REM Run performance regression tests
echo [ INFO ] Running PerformanceRegressionTests...
dotnet test "%PROJECT%" --filter "FullyQualifiedName~PerformanceRegressionTests" --logger "console;verbosity=detailed" --results-directory "%REPORT_DIR%" --collect:"XPlat Code Coverage" --logger "trx;LogFileName=performance_regression_tests.trx"
if %ERRORLEVEL% neq 0 (
    echo [ ERROR ] Performance regression tests failed
    exit /b 1
)
echo [ SUCCESS ] Performance regression tests passed

REM Run end-to-end performance tests
echo [ INFO ] Running EndToEndPerformanceTests...
dotnet test "%PROJECT%" --filter "FullyQualifiedName~EndToEndPerformanceTests" --logger "console;verbosity=detailed" --results-directory "%REPORT_DIR%" --collect:"XPlat Code Coverage" --logger "trx;LogFileName=end_to_end_performance_tests.trx"
if %ERRORLEVEL% neq 0 (
    echo [ ERROR ] End-to-end performance tests failed
    exit /b 1
)
echo [ SUCCESS ] End-to-end performance tests passed

REM Function to run real-world performance scenarios
echo [ INFO ] Running real-world performance tests...

set TEMP_DIR=%TEMP%\zipper_perf_test_%RANDOM%
mkdir "%TEMP_DIR%"

REM Test 1: Small dataset performance
echo [ INFO ] Test 1: Small dataset (100 files)
set START_TIME=%TIME%
dotnet run --project Zipper\Zipper.csproj -- --type pdf --count 100 --output-path "%TEMP_DIR%\small" --folders 5 --distribution proportional --include-load-file
set END_TIME=%TIME%

REM Calculate duration (simplified)
for /f "tokens=1-3 delims=:." %%a in ("%START_TIME%") do set START_HOURS=%%a&set /a START_MINUTES=100%%b%%100&set /a START_SECONDS=100%%c%%100
for /f "tokens=1-3 delims=:." %%a in ("%END_TIME%") do set END_HOURS=%%a&set /a END_MINUTES=100%%b%%100&set /a END_SECONDS=100%%c%%100

set /a START_TOTAL=START_HOURS*3600 + START_MINUTES*60 + START_SECONDS
set /a END_TOTAL=END_HOURS*3600 + END_MINUTES*60 + END_SECONDS
set /a DURATION_SECONDS=END_TOTAL - START_TOTAL
set /a DURATION_MS=DURATION_SECONDS * 1000

if %DURATION_MS% lss 2000 (
    echo [ SUCCESS ] Small dataset test completed in %DURATION_MS%ms
) else (
    echo [ WARNING ] Small dataset test took %DURATION_MS%ms (target ^< 2000ms)
)

REM Verify output
if exist "%TEMP_DIR%\small.zip" (
    for %%A in ("%TEMP_DIR%\small.zip") do set ZIP_SIZE=%%~zA
    echo [ INFO ] Small dataset: ZIP size %ZIP_SIZE% bytes
)

REM Test 2: Medium dataset performance
echo [ INFO ] Test 2: Medium dataset (1000 files)
set START_TIME=%TIME%
dotnet run --project Zipper\Zipper.csproj -- --type pdf --count 1000 --output-path "%TEMP_DIR%\medium" --folders 10 --distribution gaussian --with-metadata --include-load-file
set END_TIME=%TIME%

for /f "tokens=1-3 delims=:." %%a in ("%START_TIME%") do set START_HOURS=%%a&set /a START_MINUTES=100%%b%%100&set /a START_SECONDS=100%%c%%100
for /f "tokens=1-3 delims=:." %%a in ("%END_TIME%") do set END_HOURS=%%a&set /a END_MINUTES=100%%b%%100&set /a END_SECONDS=100%%c%%100

set /a START_TOTAL=START_HOURS*3600 + START_MINUTES*60 + START_SECONDS
set /a END_TOTAL=END_HOURS*3600 + END_MINUTES*60 + END_SECONDS
set /a DURATION_SECONDS=END_TOTAL - START_TOTAL
set /a DURATION_MS=DURATION_SECONDS * 1000

if %DURATION_MS% lss 10000 (
    echo [ SUCCESS ] Medium dataset test completed in %DURATION_MS%ms
) else (
    echo [ WARNING ] Medium dataset test took %DURATION_MS%ms (target ^< 10000ms)
)

REM Verify output
if exist "%TEMP_DIR%\medium.zip" (
    for %%A in ("%TEMP_DIR%\medium.zip") do set ZIP_SIZE=%%~zA
    echo [ INFO ] Medium dataset: ZIP size %ZIP_SIZE% bytes
)

REM Test 3: EML performance
echo [ INFO ] Test 3: EML files with attachments (500 files)
set START_TIME=%TIME%
dotnet run --project Zipper\Zipper.csproj -- --type eml --count 500 --output-path "%TEMP_DIR%\eml" --folders 8 --distribution exponential --attachment-rate 60 --with-metadata --with-text --include-load-file
set END_TIME=%TIME%

for /f "tokens=1-3 delims=:." %%a in ("%START_TIME%") do set START_HOURS=%%a&set /a START_MINUTES=100%%b%%100&set /a START_SECONDS=100%%c%%100
for /f "tokens=1-3 delims=:." %%a in ("%END_TIME%") do set END_HOURS=%%a&set /a END_MINUTES=100%%b%%100&set /a END_SECONDS=100%%c%%100

set /a START_TOTAL=START_HOURS*3600 + START_MINUTES*60 + START_SECONDS
set /a END_TOTAL=END_HOURS*3600 + END_MINUTES*60 + END_SECONDS
set /a DURATION_SECONDS=END_TOTAL - START_TOTAL
set /a DURATION_MS=DURATION_SECONDS * 1000

echo [ INFO ] EML dataset test completed in %DURATION_MS%ms

REM Verify output
if exist "%TEMP_DIR%\eml.zip" (
    for %%A in ("%TEMP_DIR%\eml.zip") do set ZIP_SIZE=%%~zA
    echo [ INFO ] EML dataset: ZIP size %ZIP_SIZE% bytes
)

REM Calculate throughput metrics
echo [ INFO ] Performance Summary:
set /a SMALL_THROUGHPUT=100 * 1000 / DURATION_MS
echo [ INFO ] Small dataset (100 files): %SMALL_THROUGHPUT% files/second
set /a MEDIUM_THROUGHPUT=1000 * 1000 / DURATION_MS
echo [ INFO ] Medium dataset (1000 files): %MEDIUM_THROUGHPUT% files/second
set /a EML_THROUGHPUT=500 * 1000 / DURATION_MS
echo [ INFO ] EML dataset (500 files): %EML_THROUGHPUT% files/second

REM Create performance summary report
set SUMMARY_FILE=%REPORT_DIR%\performance_summary.txt

(
echo Zipper Performance Regression Test Report
echo ========================================
echo Date: %date%
echo Platform: Windows
echo .NET Version:
dotnet --version 2>nul
echo.
echo Test Results:
echo ------------
echo.
echo 1. Unit Test Performance:
if exist "%REPORT_DIR%\performance_regression_tests.trx" (
    echo    - Performance Regression Tests: PASSED
) else (
    echo    - Performance Regression Tests: FAILED
)
if exist "%REPORT_DIR%\end_to_end_performance_tests.trx" (
    echo    - End-to-End Performance Tests: PASSED
) else (
    echo    - End-to-End Performance Tests: FAILED
)
echo.
echo 2. Real-World Performance:
echo    - Small Dataset ^(100 files^): Target ^< 2s
echo    - Medium Dataset ^(1000 files^): Target ^< 10s
echo    - EML Dataset ^(500 files^): Target reasonable time
echo.
echo 3. Recommendations:
echo    - Monitor performance metrics over time
echo    - Set up automated alerts for regressions
echo    - Compare results with baseline measurements
) > "%SUMMARY_FILE%"

echo [ INFO ] Performance summary report generated: %SUMMARY_FILE%
type "%SUMMARY_FILE%"

REM Cleanup
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"

REM Cleanup old test results (keep last 5)
for /f "skip=5 delims=" %%d in ('dir /b /ad "%TEST_OUTPUT_DIR%\20*" 2^>nul') do rmdir /s /q "%TEST_OUTPUT_DIR%\%%d"

echo [ SUCCESS ] All performance regression tests completed!
echo [ INFO ] Results saved to: %REPORT_DIR%
echo [ INFO ] Summary report: %REPORT_DIR%\performance_summary.txt

pause