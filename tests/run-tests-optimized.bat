@echo off

REM --- Optimized Pre-commit Test Configuration ---
REM This script runs unit tests + one basic E2E test for faster pre-commit checks

REM The directory where test output will be generated.
set TEST_OUTPUT_DIR=.\test_output

REM The .NET project to run.
set PROJECT=src\Zipper.csproj

REM --- Helper Functions ---

:print_success
    echo [ SUCCESS ] %~1
    goto :eof

:print_info
    echo [ INFO ] %~1
    goto :eof

:print_error
    echo [ ERROR ] %~1
    exit /b 1

:verify_output
    set "test_dir=%~1"
    set "expected_count=%~2"
    set "expected_header_str=%~3"
    set "file_type=%~4"

    call :print_info "Verifying output in %test_dir%"

    for %%F in ("%test_dir%\*.zip") do set "zip_file=%%F"
    for %%F in ("%test_dir%\*.dat") do set "dat_file=%%F"

    if not defined zip_file (
        call :print_error "No .zip file found in %test_dir%"
    )
    if not defined dat_file (
        call :print_error "No .dat file found in %test_dir%"
    )

    REM Verify line count in .dat file (+1 for header) using PowerShell
    powershell -Command "(Get-Content -Path '%dat_file%').Count" > "%temp%\line_count.txt"
    set /p line_count=<"%temp%\line_count.txt"
    set /a expected_line_count=%expected_count% + 1
    if "%line_count%" neq "%expected_line_count%" (
        call :print_error "Incorrect line count in .dat file. Expected %expected_line_count%, found %line_count%."
    )
    call :print_info ".dat file line count is correct (%line_count%)."

    REM Verify header using PowerShell
    powershell -Command "(Get-Content -Path '%dat_file%' -TotalCount 1)" > "%temp%\header.txt"
    set /p header=<"%temp%\header.txt"
    for %%H in (%expected_header_str%) do (
        echo "%header%" | findstr /c:"%%H" >nul
        if errorlevel 1 (
            call :print_error "Header validation failed. Expected to find '%%H' in '%header%'."
        )
    )
    call :print_info ".dat file header is correct."

    REM Verify file count in zip using PowerShell
    powershell -Command "[System.IO.Compression.ZipFile]::OpenRead('%zip_file%').Entries.Where({$_.Name -like '*.' + '%file_type%'}).Count" > "%temp%\zip_count.txt"
    set /p zip_file_count=<"%temp%\zip_count.txt"
    if "%zip_file_count%" neq "%expected_count%" (
        call :print_error "Incorrect file count in .zip file. Expected %expected_count%, found %zip_file_count%."
    )
    call :print_info ".zip file count for .%file_type% is correct (%zip_file_count%)."
    goto :eof

REM --- Optimized Test Suite ---

call :print_info "Running optimized pre-commit test suite..."

REM Step 1: Run Unit Tests
call :print_info "Running unit tests..."
dotnet test src\Zipper.Tests\ --verbosity quiet
if errorlevel 1 (
    call :print_error "Unit tests failed"
)

call :print_success "Unit tests passed."

REM Step 2: Run One Basic E2E Test
call :print_info "Running basic E2E test..."

REM Create a temporary directory for test output.
if exist "%TEST_OUTPUT_DIR%" (
    rmdir /s /q "%TEST_OUTPUT_DIR%"
)
mkdir "%TEST_OUTPUT_DIR%"

REM Basic PDF generation test (Test Case 1 from full suite)
call :print_info "Running E2E Test: Basic PDF generation"
dotnet run --project "%PROJECT%" -- --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_basic"
if errorlevel 1 (
    call :print_error "E2E test failed"
)
call :verify_output "%TEST_OUTPUT_DIR%\pdf_basic" 10 "Control Number,File Path" "pdf"

REM --- Cleanup ---
call :print_info "Cleaning up test output..."
rmdir /s /q "%TEST_OUTPUT_DIR%"

call :print_success "Optimized pre-commit tests passed successfully!"
exit /b 0