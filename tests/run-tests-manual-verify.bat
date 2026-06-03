@echo off
setlocal enabledelayedexpansion

REM Windows Batch parity for run-tests-manual-verify.sh
REM Runs E2E tests and manually validates files, lines, and ZIP contents.

set "REPO_ROOT=%~dp0.."
set "PROJECT=%REPO_ROOT%\src\Zipper.csproj"
set "TEST_OUTPUT_DIR=.\test_output"

pushd "%REPO_ROOT%"
call "%~dp0_zipper-cli.bat"
popd

if "%ZIPPER_CMD%"=="" (
    echo [ ERROR ] Zipper binary not resolved.
    exit /b 1
)

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

echo [ INFO ] Starting E2E manual verify tests...

REM --- Test Cases ---

echo [ INFO ] Running Test Case 1: Basic PDF generation
%ZIPPER_CMD% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_basic"
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\pdf_basic" 10 "Control Number,File Path" "pdf" "false" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 1 passed.

echo [ INFO ] Running Test Case 2: JPG generation with UTF-16 encoding
%ZIPPER_CMD% --type jpg --count 10 --output-path "%TEST_OUTPUT_DIR%\jpg_encoding" --encoding UTF-16
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\jpg_encoding" 10 "Control Number,File Path" "jpg" "false" "UTF-16"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 2 passed.

echo [ INFO ] Running Test Case 3: TIFF generation with multiple folders and proportional distribution
%ZIPPER_CMD% --type tiff --count 100 --output-path "%TEST_OUTPUT_DIR%\tiff_folders" --folders 5 --distribution proportional
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\tiff_folders" 100 "Control Number,File Path" "tiff" "false" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 3 passed.

echo [ INFO ] Running Test Case 4: PDF generation with Gaussian distribution
%ZIPPER_CMD% --type pdf --count 100 --output-path "%TEST_OUTPUT_DIR%\pdf_gaussian" --folders 10 --distribution gaussian
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\pdf_gaussian" 100 "Control Number,File Path" "pdf" "false" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 4 passed.

echo [ INFO ] Running Test Case 5: JPG generation with Exponential distribution
%ZIPPER_CMD% --type jpg --count 100 --output-path "%TEST_OUTPUT_DIR%\jpg_exponential" --folders 10 --distribution exponential
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\jpg_exponential" 100 "Control Number,File Path" "jpg" "false" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 5 passed.

echo [ INFO ] Running Test Case 6: PDF generation with metadata
%ZIPPER_CMD% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_metadata" --with-metadata
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\pdf_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "pdf" "false" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 6 passed.

echo [ INFO ] Running Test Case 7: All options combined
%ZIPPER_CMD% --type tiff --count 100 --output-path "%TEST_OUTPUT_DIR%\all_options" --folders 20 --encoding ANSI --distribution gaussian --with-metadata
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\all_options" 100 "Control Number,File Path,Custodian,Date Sent,Author,File Size" "tiff" "false" "ANSI"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 7 passed.

echo [ INFO ] Running Test Case 8: With text
%ZIPPER_CMD% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_with_text" --with-text
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\pdf_with_text" 10 "Control Number,File Path,Extracted Text" "pdf" "true" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 8 passed.

echo [ INFO ] Running Test Case 9: With text and metadata
%ZIPPER_CMD% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_with_text_and_metadata" --with-text --with-metadata
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\pdf_with_text_and_metadata" 10 "Control Number,File Path,Custodian,Date Sent,Author,File Size,Extracted Text" "pdf" "true" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 9 passed.

echo [ INFO ] Running Test Case 10: EML generation with attachments
%ZIPPER_CMD% --type eml --count 20 --output-path "%TEST_OUTPUT_DIR%\eml_attachments" --attachment-rate 50
if errorlevel 1 goto :test_failed
call :verify_eml_output "%TEST_OUTPUT_DIR%\eml_attachments" 20 "Control Number,File Path,To,From,Subject,Sent Date,Attachment" "eml" "false" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 10 passed.

echo [ INFO ] Running Test Case 11: EML generation with metadata
%ZIPPER_CMD% --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_metadata" --with-metadata
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\eml_metadata" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment" "eml" "false" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 11 passed.

echo [ INFO ] Running Test Case 12: EML generation with text
%ZIPPER_CMD% --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_text" --with-text
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\eml_text" 10 "Control Number,File Path,To,From,Subject,Sent Date,Attachment,Extracted Text" "eml" "true" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 12 passed.

echo [ INFO ] Running Test Case 13: EML generation with metadata and text
%ZIPPER_CMD% --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_metadata_text" --with-metadata --with-text
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\eml_metadata_text" 10 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment,Extracted Text" "eml" "true" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 13 passed.

echo [ INFO ] Running Test Case 14: Target zip size
%ZIPPER_CMD% --type pdf --count 100 --output-path "%TEST_OUTPUT_DIR%\pdf_target_size" --target-zip-size 1MB
if errorlevel 1 goto :test_failed
call :verify_zip_size "%TEST_OUTPUT_DIR%\pdf_target_size" 1
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 14 passed.

echo [ INFO ] Running Test Case 15: Include load file in zip
%ZIPPER_CMD% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_include_load" --include-load-file
if errorlevel 1 goto :test_failed
call :verify_load_file_included "%TEST_OUTPUT_DIR%\pdf_include_load" 10 "Control Number,File Path" "pdf" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 15 passed.

echo [ INFO ] Running Test Case 16: EML attachments with metadata and text
%ZIPPER_CMD% --type eml --count 50 --output-path "%TEST_OUTPUT_DIR%\eml_attachments_full" --attachment-rate 60 --with-metadata --with-text
if errorlevel 1 goto :test_failed
call :verify_eml_output "%TEST_OUTPUT_DIR%\eml_attachments_full" 50 "Control Number,File Path,To,From,Subject,Custodian,Author,Sent Date,Date Sent,File Size,Attachment,Extracted Text" "eml" "true" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 16 passed.

echo [ INFO ] Running Test Case 17: Maximum folders edge case (100 folders)
%ZIPPER_CMD% --type pdf --count 200 --output-path "%TEST_OUTPUT_DIR%\pdf_max_folders" --folders 100
if errorlevel 1 goto :test_failed
call :verify_output "%TEST_OUTPUT_DIR%\pdf_max_folders" 200 "Control Number,File Path" "pdf" "false" "UTF-8"
if errorlevel 1 goto :test_failed
echo [ SUCCESS ] Test Case 17 passed.

REM Cleanup
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"

echo.
echo [ SUCCESS ] All E2E manual verify tests passed!
exit /b 0

:test_failed
echo [ ERROR ] Test case failed.
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
exit /b 1

REM --- Helper Subroutines ---

:print_success
    echo [ SUCCESS ] %~1
    goto :eof

:print_info
    echo [ INFO ] %~1
    goto :eof

:print_error
    echo [ ERROR ] %~1
    if "%~2"=="" (exit /b 1) else (exit /b %~2)

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
        call :print_error "No .zip file found in %test_dir%" 1 & exit /b 1
    )
    if not defined dat_file (
        call :print_error "No .dat file found in %test_dir%" 1 & exit /b 1
    )

    powershell -Command "(Get-Content -Path '%dat_file%').Count" > "%temp%\line_count.txt"
    set /p line_count=<"%temp%\line_count.txt"
    set /a expected_line_count=%expected_count% + 1
    if "%line_count%" neq "%expected_line_count%" (
        call :print_error "Incorrect line count in .dat file. Expected %expected_line_count%, found %line_count%." 1 & exit /b 1
    )
    call :print_info ".dat file line count is correct (%line_count%)."

    powershell -Command "(Get-Content -Path '%dat_file%' -TotalCount 1)" > "%temp%\header.txt"
    set /p header=<"%temp%\header.txt"
    for %%H in (%expected_header_str%) do (
        echo "%header%" | findstr /c:"%%H" >nul
        if errorlevel 1 (
            call :print_error "Header validation failed. Expected to find '%%H' in '%header%'." 1 & exit /b 1
        )
    )
    call :print_info ".dat file header is correct."

    powershell -Command "[System.IO.Compression.ZipFile]::OpenRead('%zip_file%').Entries.Where({$_.Name -like '*.' + '%file_type%'}).Count" > "%temp%\zip_count.txt"
    set /p zip_file_count=<"%temp%\zip_count.txt"
    if "%zip_file_count%" neq "%expected_count%" (
        call :print_error "Incorrect file count in .zip file. Expected %expected_count%, found %zip_file_count%." 1 & exit /b 1
    )
    call :print_info ".zip file count for .%file_type% is correct (%zip_file_count%)."

    if "%check_text%" == "true" (
        powershell -Command "[System.IO.Compression.ZipFile]::OpenRead('%zip_file%').Entries.Where({$_.Name -like '*.txt'}).Count" > "%temp%\zip_txt_count.txt"
        set /p txt_count=<"%temp%\zip_txt_count.txt"
        if "%txt_count%" neq "%expected_count%" (
            call :print_error "Incorrect .txt file count in .zip file. Expected %expected_count%, found %txt_count%." 1 & exit /b 1
        )
        call :print_info ".zip file count for .txt is correct (%txt_count%)."
    )
    goto :eof

:verify_zip_size
    set "test_dir=%~1"
    set "target_size_mb=%~2"
    set /a target_size_bytes=target_size_mb * 1024 * 1024
    set /a tolerance_bytes=target_size_bytes / 10

    for %%f in ("%test_dir%\*.zip") do set "zip_file=%%f"

    if not defined zip_file (
        call :print_error "No .zip file found in %test_dir%" 1 & exit /b 1
    )

    for %%A in ("%zip_file%") do set "actual_size_bytes=%%~zA"
    set /a min_size=target_size_bytes - tolerance_bytes
    set /a max_size=target_size_bytes + tolerance_bytes

    if %actual_size_bytes% lss %min_size% (
        call :print_error "Zip file size is below minimum. Expected around %target_size_mb%MB, found %actual_size_bytes% bytes." 1 & exit /b 1
    )
    if %actual_size_bytes% gtr %max_size% (
        call :print_error "Zip file size is above maximum. Expected around %target_size_mb%MB, found %actual_size_bytes% bytes." 1 & exit /b 1
    )

    call :print_info "Zip file size is within the expected range."
    goto :eof

:verify_eml_output
    set "test_dir=%~1"
    set "expected_count=%~2"
    set "expected_header_str=%~3"
    set "file_type=%~4"
    set "check_text=%~5"
    set "encoding=%~6"

    call :verify_output "%test_dir%" "%expected_count%" "%expected_header_str%" "%file_type%" "%check_text%" "%encoding%"
    if errorlevel 1 exit /b %errorlevel%

    for %%F in ("%test_dir%\*.zip") do set "zip_file=%%F"
    for %%F in ("%test_dir%\*.dat") do set "dat_file=%%F"
    if not defined zip_file (
        call :print_error "No .zip file found in %test_dir%" 1 & exit /b 1
    )
    if not defined dat_file (
        call :print_error "No .dat file found in %test_dir%" 1 & exit /b 1
    )

    powershell -Command "try { $z = [System.IO.Compression.ZipFile]::OpenRead('%zip_file%'); $c = ($z.Entries | Where { $_.Name -match 'attachment.*\.(pdf|jpg|tiff)$' }).Count; $z.Dispose(); Write-Host $c } catch { Write-Host 0 }" > "%temp%\att_count.txt"
    set /p attachment_files=<"%temp%\att_count.txt"

    powershell -Command "try { $z = [System.IO.Compression.ZipFile]::OpenRead('%zip_file%'); $c = ($z.Entries | Where { $_.Name -match '\.eml$' }).Count; $z.Dispose(); Write-Host $c } catch { Write-Host 0 }" > "%temp%\eml_count.txt"
    set /p eml_files=<"%temp%\eml_count.txt"

    set /a min_expected_attachments=eml_files / 10
    if %attachment_files% lss %min_expected_attachments% (
        call :print_error "Expected at least %min_expected_attachments% attachment files in ZIP, but found %attachment_files%." 1 & exit /b 1
    )
    call :print_info "Found %attachment_files% attachment files in ZIP archive (expected at least %min_expected_attachments%)."

    findstr /c:"attachment" "%dat_file%" >nul
    if errorlevel 1 (
        call :print_error "No attachments found in .dat file, but they were expected." 1 & exit /b 1
    )
    call :print_info "Found attachments in .dat file."

    if "%check_text%" == "true" (
        powershell -Command "try { $z = [System.IO.Compression.ZipFile]::OpenRead('%zip_file%'); $c = ($z.Entries | Where { $_.Name -match 'attachment.*\.txt$' }).Count; $z.Dispose(); Write-Host $c } catch { Write-Host 0 }" > "%temp%\att_txt_count.txt"
        set /p attachment_text_files=<"%temp%\att_txt_count.txt"
        if %attachment_text_files% lss %min_expected_attachments% (
            call :print_error "Expected at least %min_expected_attachments% attachment text files, but found %attachment_text_files%." 1 & exit /b 1
        )
        call :print_info "Found %attachment_text_files% attachment text files in ZIP archive."
    )
    goto :eof

:verify_load_file_included
    set "test_dir=%~1"
    set "expected_count=%~2"
    set "expected_header_str=%~3"
    set "file_type=%~4"
    set "encoding=%~5"

    call :print_info "Verifying load file included in zip archive (Encoding: %encoding%)"

    for %%F in ("%test_dir%\*.zip") do set "zip_file=%%F"
    if not defined zip_file (
        call :print_error "No .zip file found in %test_dir%" 1 & exit /b 1
    )

    for %%F in ("%test_dir%\*.dat") do set "dat_file=%%F"
    if defined dat_file (
        call :print_error "Found separate .dat file in output directory, but --include-load-file was specified" 1 & exit /b 1
    )

    powershell -Command "try { $z = [System.IO.Compression.ZipFile]::OpenRead('%zip_file%'); $c = ($z.Entries | Where { $_.Name -match '\.dat$' }).Count; $z.Dispose(); exit ($c -eq 1 ? 0 : 1) } catch { exit 1 }"
    if errorlevel 1 (
        call :print_error "Expected 1 .dat file in zip archive" 1 & exit /b 1
    )
    call :print_info ".dat file correctly included in zip archive."

    set "temp_extract=%test_dir%\_verify_temp"
    if exist "%temp_extract%" rmdir /s /q "%temp_extract%"
    mkdir "%temp_extract%"
    powershell -Command "try { $z = [System.IO.Compression.ZipFile]::OpenRead('%zip_file%'); $e = $z.Entries | Where { $_.Name -match '\.dat$' } | Select -First 1; $dest = Join-Path '%temp_extract%' $e.Name; [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, $dest, $true); $z.Dispose() } catch { exit 1 }"

    for %%F in ("%temp_extract%\*.dat") do set "extracted_dat=%%F"
    if not defined extracted_dat (
        call :print_error "Failed to extract .dat file from zip archive" 1 & exit /b 1
    )

    powershell -Command "(Get-Content -Path '%extracted_dat%').Count" > "%temp%\line_count2.txt"
    set /p line_count=<"%temp%\line_count2.txt"
    set /a expected_line_count=%expected_count% + 1
    if "%line_count%" neq "%expected_line_count%" (
        call :print_error "Incorrect line count in .dat file. Expected %expected_line_count%, found %line_count%." 1 & exit /b 1
    )
    call :print_info ".dat file line count is correct (%line_count%)."

    powershell -Command "(Get-Content -Path '%extracted_dat%' -TotalCount 1)" > "%temp%\header2.txt"
    set /p header=<"%temp%\header2.txt"
    for %%H in (%expected_header_str%) do (
        echo "%header%" | findstr /c:"%%H" >nul
        if errorlevel 1 (
            call :print_error "Header validation failed. Expected to find '%%H' in '%header%'." 1 & exit /b 1
        )
    )
    call :print_info ".dat file header is correct."

    powershell -Command "try { $z = [System.IO.Compression.ZipFile]::OpenRead('%zip_file%'); $c = ($z.Entries | Where { $_.Name -match '\.%file_type%$' }).Count; $z.Dispose(); Write-Host $c } catch { Write-Host 0 }" > "%temp%\zip_count2.txt"
    set /p zip_file_count=<"%temp%\zip_count2.txt"
    if "%zip_file_count%" neq "%expected_count%" (
        call :print_error "Incorrect file count in .zip file. Expected %expected_count%, found %zip_file_count%." 1 & exit /b 1
    )
    call :print_info ".zip file count for .%file_type% is correct (%zip_file_count%)."
    goto :eof
