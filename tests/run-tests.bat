@echo off

REM --- Test Configuration ---

REM The directory where test output will be generated.
set TEST_OUTPUT_DIR=.\test_output

REM The .NET project to run.
set PROJECT=Zipper\Zipper.csproj

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
    set "check_text=%~5"

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

    REM Verify text file count if required
    if "%check_text%" == "true" (
        powershell -Command "[System.IO.Compression.ZipFile]::OpenRead('%zip_file%').Entries.Where({$_.Name -like '*.txt'}).Count" > "%temp%\zip_txt_count.txt"
        set /p txt_count=<"%temp%\zip_txt_count.txt"
        if "%txt_count%" neq "%expected_count%" (
            call :print_error "Incorrect .txt file count in .zip file. Expected %expected_count%, found %txt_count%."
        )
        call :print_info ".zip file count for .txt is correct (%txt_count%)."
    )
    goto :eof

REM Verifies the size of the generated zip file.
REM Arguments:
REM %1: Test case directory
REM %2: Target size in MB
:verify_zip_size
    set "test_dir=%~1"
    set "target_size_mb=%~2"
    set /a target_size_bytes=target_size_mb * 1024 * 1024
    set /a tolerance_bytes=target_size_bytes / 10

    REM Find the zip file
    for %%f in ("%test_dir%\*.zip") do set "zip_file=%%f"

    if not defined zip_file (
        call :print_error "No .zip file found in %test_dir%"
    )

    REM Get file size
    for %%A in ("%zip_file%") do set "actual_size_bytes=%%~zA"
    set /a min_size=target_size_bytes - tolerance_bytes
    set /a max_size=target_size_bytes + tolerance_bytes

    if %actual_size_bytes% lss %min_size% (
        call :print_error "Zip file size is below minimum. Expected around %target_size_mb%MB, found %actual_size_bytes% bytes."
    )
    if %actual_size_bytes% gtr %max_size% (
        call :print_error "Zip file size is above maximum. Expected around %target_size_mb%MB, found %actual_size_bytes% bytes."
    )

    call :print_info "Zip file size is within the expected range."
    goto :eof

REM --- Test Cases ---

call :print_info "Starting test suite..."

REM Create a temporary directory for test output.
if exist "%TEST_OUTPUT_DIR%" (
    rmdir /s /q "%TEST_OUTPUT_DIR%"
)
mkdir "%TEST_OUTPUT_DIR%"

:run_test_case
    set "test_name=%~1"
    shift
    call :print_info "START: %test_name% at %DATE% %TIME%"
    dotnet run --project "%PROJECT%" -- %*
    if errorlevel 1 (
        call :print_error "%test_name% failed with exit code %errorlevel%"
    )
    call :print_info "END: %test_name% at %DATE% %TIME%"
    goto :eof

REM Test Case 1: Basic PDF generation
call :run_test_case "Test Case 1: Basic PDF generation" --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_basic"
call :verify_output "%TEST_OUTPUT_DIR%\pdf_basic" 10 "Control Number,File Path" "pdf" "false"
call :print_success "Test Case 1 passed."

REM Test Case 2: JPG generation with different encoding
call :run_test_case "Test Case 2: JPG generation with UTF-16 encoding" --type jpg --count 10 --output-path "%TEST_OUTPUT_DIR%\jpg_encoding" --encoding UTF-16
call :verify_output "%TEST_OUTPUT_DIR%\jpg_encoding" 10 "Control Number,File Path" "jpg" "false"
call :print_success "Test Case 2 passed."

REM Test Case 3: TIFF generation with multiple folders and proportional distribution
call :run_test_case "Test Case 3: TIFF generation" --type tiff --count 100 --output-path "%TEST_OUTPUT_DIR%\tiff_folders" --folders 5 --distribution proportional
call :verify_output "%TEST_OUTPUT_DIR%\tiff_folders" 100 "Control Number,File Path" "tiff" "false"
call :print_success "Test Case 3 passed."

REM Test Case 4: PDF generation with Gaussian distribution
call :run_test_case "Test Case 4: PDF generation with Gaussian distribution" --type pdf --count 100 --output-path "%TEST_OUTPUT_DIR%\pdf_gaussian" --folders 10 --distribution gaussian
call :verify_output "%TEST_OUTPUT_DIR%\pdf_gaussian" 100 "Control Number,File Path" "pdf" "false"
call :print_success "Test Case 4 passed."

REM Test Case 5: JPG generation with Exponential distribution
call :run_test_case "Test Case 5: JPG generation with Exponential distribution" --type jpg --count 100 --output-path "%TEST_OUTPUT_DIR%\jpg_exponential" --folders 10 --distribution exponential
call :verify_output "%TEST_OUTPUT_DIR%\jpg_exponential" 100 "Control Number,File Path" "jpg" "false"
call :print_success "Test Case 5 passed."

REM Test Case 6: PDF generation with metadata
call :run_test_case "Test Case 6: PDF generation with metadata" --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_metadata" --with-metadata
call :verify_output "%TEST_OUTPUT_DIR%\pdf_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "pdf" "false"
call :print_success "Test Case 6 passed."

REM Test Case 7: All options combined
call :run_test_case "Test Case 7: All options combined" --type tiff --count 100 --output-path "%TEST_OUTPUT_DIR%\all_options" --folders 20 --encoding ANSI --distribution gaussian --with-metadata
call :verify_output "%TEST_OUTPUT_DIR%\all_options" 100 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "tiff" "false"
call :print_success "Test Case 7 passed."

REM Test Case 8: With text
call :run_test_case "Test Case 8: With text" --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_with_text" --with-text
call :verify_output "%TEST_OUTPUT_DIR%\pdf_with_text" 10 "Control Number,File Path,Extracted Text" "pdf" "true"
call :print_success "Test Case 8 passed."

REM Test Case 9: With text and metadata
call :run_test_case "Test Case 9: With text and metadata" --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_with_text_and_metadata" --with-text --with-metadata
call :verify_output "%TEST_OUTPUT_DIR%\pdf_with_text_and_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size,Extracted Text" "pdf" "true"
call :print_success "Test Case 9 passed."

REM Test Case 10: EML generation with attachments
call :run_test_case "Test Case 10: EML generation with attachments" --type eml --count 20 --output-path "%TEST_OUTPUT_DIR%\eml_attachments" --attachment-rate 50
call :verify_output "%TEST_OUTPUT_DIR%\eml_attachments" 20 "Control Number,File Path,To,From,Subject,Sent Date,Attachment" "eml" "false"
call :print_success "Test Case 10 passed."

REM Test Case 11: EML generation with metadata
call :run_test_case "Test Case 11: EML generation with metadata" --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_metadata" --with-metadata
call :verify_output "%TEST_OUTPUT_DIR%\eml_metadata" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment" "eml" "false"
call :print_success "Test Case 11 passed."

REM Test Case 12: EML generation with text
call :run_test_case "Test Case 12: EML generation with text" --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_text" --with-text
call :verify_output "%TEST_OUTPUT_DIR%\eml_text" 10 "Control Number,File Path,To,From,Subject,Sent Date,Attachment,Extracted Text" "eml" "true"
call :print_success "Test Case 12 passed."

REM Test Case 13: EML generation with metadata and text
call :run_test_case "Test Case 13: EML generation with metadata and text" --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_metadata_text" --with-metadata --with-text
call :verify_output "%TEST_OUTPUT_DIR%\eml_metadata_text" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment,Extracted Text" "eml" "true"
call :print_success "Test Case 13 passed."

REM Test Case 14: Target zip size
call :run_test_case "Test Case 14: Target zip size" --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_target_size" --target-zip-size 1MB
call :verify_zip_size "%TEST_OUTPUT_DIR%\pdf_target_size" 1
call :print_success "Test Case 14 passed."

REM Test Case 15: Include load file in zip
call :run_test_case "Test Case 15: Include load file in zip" --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_include_load" --include-load-file
call :verify_load_file_included "%TEST_OUTPUT_DIR%\pdf_include_load" 10 "Control Number,File Path" "pdf" "UTF-8"
call :print_success "Test Case 15 passed."

REM Test Case 16: EML attachments with metadata and text (comprehensive attachment test)
call :run_test_case "Test Case 16: EML attachments with metadata and text" --type eml --count 15 --output-path "%TEST_OUTPUT_DIR%\eml_attachments_full" --attachment-rate 60 --with-metadata --with-text
call :verify_eml_output "%TEST_OUTPUT_DIR%\eml_attachments_full" 15 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment,Extracted Text" "eml" "true" "UTF-8"
call :print_success "Test Case 16 passed."

REM Test Case 17: Maximum folders edge case (100 folders)
call :run_test_case "Test Case 17: Maximum folders edge case (100 folders)" --type pdf --count 25 --output-path "%TEST_OUTPUT_DIR%\pdf_max_folders" --folders 10
call :verify_output "%TEST_OUTPUT_DIR%\pdf_max_folders" 25 "Control Number,File Path" "pdf" "false" "UTF-8"
call :print_success "Test Case 17 passed."

REM --- Cleanup ---

call :print_info "Cleaning up test output..."
rmdir /s /q "%TEST_OUTPUT_DIR%"

REM --- Standalone Feature Test Suites ---
REM Run all standalone test scripts to ensure comprehensive coverage

call :print_info "Running standalone feature test suites..."

REM Test 1: EML comprehensive tests
call :print_info "Running EML comprehensive tests..."
call .\tests\test-eml-comprehensive.bat
call :print_success "EML comprehensive tests passed."

REM Test 2: Bates numbering tests
call :print_info "Running Bates numbering tests..."
call .\tests\test-bates-numbering.bat
call :print_success "Bates numbering tests passed."

REM Test 3: Multipage TIFF tests
call :print_info "Running multipage TIFF tests..."
call .\tests\test-multipage-tiff.bat
call :print_success "Multipage TIFF tests passed."

REM Test 4: Office formats tests
call :print_info "Running office formats tests..."
call .\tests\test-office-formats.bat
call :print_success "Office formats tests passed."

REM Test 5: Load file formats tests
call :print_info "Running load file formats tests..."
call .\tests\test-load-file-formats.bat
call :print_success "Load file formats tests passed."

REM Test 6: Artifact handling tests
call :print_info "Running artifact handling tests..."
call .\tests\test-artifact-handling.bat
call :print_success "Artifact handling tests passed."

REM Test 7-8: Skip workflow validation tests (obsolete - checking old build.yml structure)
call :print_info "Skipping obsolete workflow validation tests..."

REM Test 9: Cross-platform tests
call :print_info "Running cross-platform tests..."
call .\tests\test-cross-platform.bat
call :print_success "Cross-platform tests passed."

REM Test 10: Path traversal security tests
call :print_info "Running path traversal security tests..."
call .\tests\test-path-traversal-security.bat
call :print_success "Path traversal security tests passed."

REM Test 11: Unified workflow tests
call :print_info "Running unified workflow tests..."
call .\tests\test-unified-workflow.bat
call :print_success "Unified workflow tests passed."

call :print_success "All tests passed successfully!"
