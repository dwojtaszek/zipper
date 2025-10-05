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

REM --- Test Cases ---

call :print_info "Starting test suite..."

REM Create a temporary directory for test output.
if exist "%TEST_OUTPUT_DIR%" (
    rmdir /s /q "%TEST_OUTPUT_DIR%"
)
mkdir "%TEST_OUTPUT_DIR%"

REM Test Case 1: Basic PDF generation
call :print_info "Running Test Case 1: Basic PDF generation"
dotnet run --project "%PROJECT%" -- --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_basic"
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\pdf_basic" 10 "Control Number,File Path" "pdf" "false"
call :print_success "Test Case 1 passed."

REM Test Case 2: JPG generation with different encoding
call :print_info "Running Test Case 2: JPG generation with UTF-16 encoding"
dotnet run --project "%PROJECT%" -- --type jpg --count 10 --output-path "%TEST_OUTPUT_DIR%\jpg_encoding" --encoding UTF-16
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\jpg_encoding" 10 "Control Number,File Path" "jpg" "false"
call :print_success "Test Case 2 passed."

REM Test Case 3: TIFF generation with multiple folders and proportional distribution
call :print_info "Running Test Case 3: TIFF generation with multiple folders and proportional distribution"
dotnet run --project "%PROJECT%" -- --type tiff --count 100 --output-path "%TEST_OUTPUT_DIR%\tiff_folders" --folders 5 --distribution proportional
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\tiff_folders" 100 "Control Number,File Path" "tiff" "false"
call :print_success "Test Case 3 passed."

REM Test Case 4: PDF generation with Gaussian distribution
call :print_info "Running Test Case 4: PDF generation with Gaussian distribution"
dotnet run --project "%PROJECT%" -- --type pdf --count 100 --output-path "%TEST_OUTPUT_DIR%\pdf_gaussian" --folders 10 --distribution gaussian
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\pdf_gaussian" 100 "Control Number,File Path" "pdf" "false"
call :print_success "Test Case 4 passed."

REM Test Case 5: JPG generation with Exponential distribution
call :print_info "Running Test Case 5: JPG generation with Exponential distribution"
dotnet run --project "%PROJECT%" -- --type jpg --count 100 --output-path "%TEST_OUTPUT_DIR%\jpg_exponential" --folders 10 --distribution exponential
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\jpg_exponential" 100 "Control Number,File Path" "jpg" "false"
call :print_success "Test Case 5 passed."

REM Test Case 6: PDF generation with metadata
call :print_info "Running Test Case 6: PDF generation with metadata"
dotnet run --project "%PROJECT%" -- --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_metadata" --with-metadata
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\pdf_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "pdf" "false"
call :print_success "Test Case 6 passed."

REM Test Case 7: All options combined
call :print_info "Running Test Case 7: All options combined"
dotnet run --project "%PROJECT%" -- --type tiff --count 100 --output-path "%TEST_OUTPUT_DIR%\all_options" --folders 20 --encoding ANSI --distribution gaussian --with-metadata
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\all_options" 100 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "tiff" "false"
call :print_success "Test Case 7 passed."

REM Test Case 8: With text
call :print_info "Running Test Case 8: With text"
dotnet run --project "%PROJECT%" -- --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_with_text" --with-text
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\pdf_with_text" 10 "Control Number,File Path,Extracted Text" "pdf" "true"
call :print_success "Test Case 8 passed."

REM Test Case 9: With text and metadata
call :print_info "Running Test Case 9: With text and metadata"
dotnet run --project "%PROJECT%" -- --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_with_text_and_metadata" --with-text --with-metadata
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\pdf_with_text_and_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size,Extracted Text" "pdf" "true"
call :print_success "Test Case 9 passed."

REM Test Case 10: EML generation with attachments
call :print_info "Running Test Case 10: EML generation with attachments"
dotnet run --project "%PROJECT%" -- --type eml --count 20 --output-path "%TEST_OUTPUT_DIR%\eml_attachments" --attachment-rate 50
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\eml_attachments" 20 "Control Number,File Path,To,From,Subject,Sent Date,Attachment" "eml" "false"
call :print_success "Test Case 10 passed."

REM Test Case 11: EML generation with metadata
call :print_info "Running Test Case 11: EML generation with metadata"
dotnet run --project "%PROJECT%" -- --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_metadata" --with-metadata
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\eml_metadata" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment" "eml" "false"
call :print_success "Test Case 11 passed."

REM Test Case 12: EML generation with text
call :print_info "Running Test Case 12: EML generation with text"
dotnet run --project "%PROJECT%" -- --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_text" --with-text
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\eml_text" 10 "Control Number,File Path,To,From,Subject,Sent Date,Attachment,Extracted Text" "eml" "true"
call :print_success "Test Case 12 passed."

REM Test Case 13: EML generation with metadata and text
call :print_info "Running Test Case 13: EML generation with metadata and text"
dotnet run --project "%PROJECT%" -- --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_metadata_text" --with-metadata --with-text
if errorlevel 1 exit /b 1
call :verify_output "%TEST_OUTPUT_DIR%\eml_metadata_text" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment,Extracted Text" "eml" "true"
call :print_success "Test Case 13 passed."

REM --- Cleanup ---

call :print_info "Cleaning up test output..."
rmdir /s /q "%TEST_OUTPUT_DIR%"

call :print_success "All tests passed successfully!"
