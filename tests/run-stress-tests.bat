@echo off
REM =============================================================================
REM STRESS E2E TEST SUITE - MANUAL INVOCATION ONLY
REM =============================================================================
REM
REM IMPORTANT NOTES:
REM - This stress suite is NOT part of regular CI/CD or pre-commit hooks
REM - Developer must manually invoke stress tests
REM - Requires significant disk space (+20%% overhead) and time (several hours)
REM - Includes pre-run validation for available disk space
REM - Each scenario tests unique failure modes not covered in regular E2E tests
REM
REM Usage: tests\run-stress-tests.bat [scenario]
REM   scenario: "10gb", "20gb", "30gb", or "all" (default: "all")
REM
REM =============================================================================

setlocal enabledelayedexpansion

set PROJECT=Zipper\Zipper.csproj
set TEST_OUTPUT_DIR=.\stress_test_output

REM --- Parse Command Line Arguments ---
set SCENARIO=%1
if "%SCENARIO%"=="" set SCENARIO=all

REM --- Helper Functions ---
:print_warning
    echo [ WARNING ] %~1
    goto :eof

:print_error
    echo [ ERROR ] %~1
    goto :eof

:print_success
    echo [ SUCCESS ] %~1
    goto :eof

:print_info
    echo [ INFO ] %~1
    goto :eof

:get_disk_space_gb
    REM Get available disk space in GB using PowerShell
    for /f "tokens=2 delims=:" %%G in ('powershell -Command "(Get-PSDrive -PSProvider FileSystem).Free / 1GB"') do set DISK_SPACE=%%G
    set DISK_SPACE=%DISK_SPACE: =%
    goto :eof

:validate_disk_space
    set REQUIRED_GB=%~1
    set SCENARIO_NAME=%~2

    call :print_info "Validating disk space for %SCENARIO_NAME% scenario..."
    call :get_disk_space_gb
    call :print_info "Available space: !DISK_SPACE!GB, Required: !REQUIRED_GB!GB"

    if !DISK_SPACE! LSS !REQUIRED_GB! (
        call :print_error "Insufficient disk space for %SCENARIO_NAME% scenario"
        call :print_error "Available: !DISK_SPACE!GB, Required: !REQUIRED_GB!GB"
        call :print_error "Please free up disk space and try again"
        exit /b 1
    )

    call :print_success "Disk space validation passed for %SCENARIO_NAME% scenario"
    goto :eof

REM --- Stress Test Scenarios ---

REM 10GB Stress Test - Maximum File Count Challenge
:run_10gb_stress_test
    call :print_info "Starting 10GB Stress Test - Maximum File Count Challenge"
    call :print_info "Scenario: 5 million PDF files with exponential distribution across 100 folders"
    call :print_info "Features: Metadata + Text extraction enabled"
    call :print_info "Target: Test maximum file count handling and Zip64 functionality"

    set OUTPUT_DIR=%TEST_OUTPUT_DIR%\10gb_file_count_challenge

    REM Pre-run validation
    call :validate_disk_space 15 "10GB"

    REM Create output directory
    if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

    REM Run the stress test
    call :print_info "Generating 5,000,000 PDF files..."
    dotnet run --project "%PROJECT%" -- --type pdf --count 5000000 --output-path "%OUTPUT_DIR%" --folders 100 --distribution exponential --with-metadata --with-text
    if errorlevel 1 exit /b 1

    REM Verify output
    call :print_info "Verifying stress test output..."

    REM Check file count in dat file using PowerShell
    for %%F in ("%OUTPUT_DIR%\*.dat") do set DAT_FILE=%%F
    if not defined DAT_FILE (
        call :print_error "DAT file not found"
        exit /b 1
    )

    REM Count lines in dat file using PowerShell
    for /f %%L in ('powershell -Command "(Get-Content -Path '%DAT_FILE%').Count"') do set DAT_LINES=%%L
    set EXPECTED_LINES=5000001

    if !DAT_LINES! EQU %EXPECTED_LINES% (
        call :print_success "File count verification passed: 5,000,000 files"
    ) else (
        call :print_error "File count verification failed: expected 5,000,000, found !DAT_LINES!"
        exit /b 1
    )

    REM Check zip file size
    for %%F in ("%OUTPUT_DIR%\*.zip") do set ZIP_FILE=%%F
    for /f %%S in ('powershell -Command "(Get-Item '%ZIP_FILE%').Length / 1GB"') do set ZIP_SIZE_GB=%%S

    call :print_success "10GB Stress Test completed successfully!"
    call :print_info "Archive size: !ZIP_SIZE_GB!GB"
    goto :eof

REM 20GB Stress Test - Multi-Format Complexity
:run_20gb_stress_test
    call :print_info "Starting 20GB Stress Test - Multi-Format Complexity"
    call :print_info "Scenario: Mixed file types - PDF (60%%), JPG (30%%), EML (10%%)"
    call :print_info "Distribution: Gaussian across 500 folders"
    call :print_info "Features: All options enabled - metadata, text, attachments (50%% for EML)"
    call :print_info "Encoding: UTF-16 for increased complexity"
    call :print_info "Target: Test mixed format processing and memory management"

    set OUTPUT_DIR=%TEST_OUTPUT_DIR%\20gb_multi_format

    REM Pre-run validation
    call :validate_disk_space 25 "20GB"

    REM Create output directory
    if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

    REM Run PDF portion (60% = 1.2M files)
    call :print_info "Generating 1,200,000 PDF files..."
    dotnet run --project "%PROJECT%" -- --type pdf --count 1200000 --output-path "%OUTPUT_DIR%\pdf_portion" --folders 200 --distribution gaussian --with-metadata --with-text --encoding UTF-16
    if errorlevel 1 exit /b 1

    REM Run JPG portion (30% = 600K files)
    call :print_info "Generating 600,000 JPG files..."
    dotnet run --project "%PROJECT%" -- --type jpg --count 600000 --output-path "%OUTPUT_DIR%\jpg_portion" --folders 150 --distribution gaussian --with-metadata --with-text --encoding UTF-16
    if errorlevel 1 exit /b 1

    REM Run EML portion (10% = 200K files)
    call :print_info "Generating 200,000 EML files with 50%% attachment rate..."
    dotnet run --project "%PROJECT%" -- --type eml --count 200000 --output-path "%OUTPUT_DIR%\eml_portion" --folders 150 --distribution gaussian --with-metadata --with-text --attachment-rate 50 --encoding UTF-16
    if errorlevel 1 exit /b 1

    call :print_success "20GB Stress Test completed successfully!"
    call :print_info "Total files: 2,000,000"
    goto :eof

REM 30GB Stress Test - Attachment-Heavy EML Focus
:run_30gb_stress_test
    call :print_info "Starting 30GB Stress Test - Attachment-Heavy EML Focus"
    call :print_info "Scenario: 1 million EML files with 80%% attachment rate"
    call :print_info "Attachments: Varied PDF/JPG/TIFF files (2-5MB each)"
    call :print_info "Distribution: Proportional across 1000 folders"
    call :print_info "Features: Metadata + Text extraction for all files and attachments"
    call :print_info "Target: Test attachment handling, nested file processing, and archive size limits"

    set OUTPUT_DIR=%TEST_OUTPUT_DIR%\30gb_attachment_heavy

    REM Pre-run validation
    call :validate_disk_space 40 "30GB"

    REM Create output directory
    if not exist "%OUTPUT_DIR%" mkdir "%OUTPUT_DIR%"

    REM Run the stress test
    call :print_info "Generating 1,000,000 EML files with 80%% attachment rate..."
    dotnet run --project "%PROJECT%" -- --type eml --count 1000000 --output-path "%OUTPUT_DIR%" --folders 1000 --distribution proportional --with-metadata --with-text --attachment-rate 80
    if errorlevel 1 exit /b 1

    REM Verify output
    call :print_info "Verifying stress test output..."

    REM Check file count in dat file using PowerShell
    for %%F in ("%OUTPUT_DIR%\*.dat") do set DAT_FILE=%%F
    if not defined DAT_FILE (
        call :print_error "DAT file not found"
        exit /b 1
    )

    REM Count lines in dat file using PowerShell
    for /f %%L in ('powershell -Command "(Get-Content -Path '%DAT_FILE%').Count"') do set DAT_LINES=%%L
    set EXPECTED_LINES=1000001

    if !DAT_LINES! EQU %EXPECTED_LINES% (
        call :print_success "File count verification passed: 1,000,000 files"
    ) else (
        call :print_error "File count verification failed: expected 1,000,000, found !DAT_LINES!"
        exit /b 1
    )

    REM Check zip file size
    for %%F in ("%OUTPUT_DIR%\*.zip") do set ZIP_FILE=%%F
    for /f %%S in ('powershell -Command "(Get-Item '%ZIP_FILE%').Length / 1GB"') do set ZIP_SIZE_GB=%%S

    call :print_success "30GB Stress Test completed successfully!"
    call :print_info "Archive size: !ZIP_SIZE_GB!GB"
    goto :eof

REM --- Main Execution ---

:main
    echo ==============================================================================
    echo                     STRESS E2E TEST SUITE
    echo ==============================================================================
    call :print_warning "This stress suite is for manual invocation only"
    call :print_warning "Requires significant disk space and time (several hours)"
    call :print_warning "Each scenario tests unique failure modes not covered in regular E2E tests"
    echo ==============================================================================
    echo.

    REM Check if project exists
    if not exist "%PROJECT%" (
        call :print_error "Project file not found: %PROJECT%"
        exit /b 1
    )

    REM Create main output directory
    if not exist "%TEST_OUTPUT_DIR%" mkdir "%TEST_OUTPUT_DIR%"

    if "%SCENARIO%"=="10gb" (
        call :run_10gb_stress_test
    ) else if "%SCENARIO%"=="20gb" (
        call :run_20gb_stress_test
    ) else if "%SCENARIO%"=="30gb" (
        call :run_30gb_stress_test
    ) else if "%SCENARIO%"=="all" (
        call :run_10gb_stress_test
        echo.
        call :run_20gb_stress_test
        echo.
        call :run_30gb_stress_test
    ) else (
        call :print_error "Invalid scenario: %SCENARIO%"
        echo Usage: %~nx0 [10gb^|20gb^|30gb^|all]
        exit /b 1
    )

    echo.
    call :print_success "All stress test scenarios completed successfully!"
    call :print_info "Output files are available in: %TEST_OUTPUT_DIR%"
    call :print_warning "Remember to clean up the test output directory when done"
    echo ==============================================================================
    exit /b 0

call :main %*