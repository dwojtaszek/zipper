@echo off
setlocal enabledelayedexpansion

REM Comprehensive EML Test Suite - Windows Version
REM Constitutional Requirement: Must test ALL EML functionality scenarios

echo ========================================
echo Comprehensive EML Test Suite - Windows
echo ========================================
echo.

REM Set test environment
set "TEST_DIR=%TEMP%\zipper-eml-test-%RANDOM%"
set "REPO_ROOT=%~dp0.."
set "ZIPPER_CMD=dotnet run --project "%REPO_ROOT%\Zipper\Zipper.csproj" --"
set "FILE_COUNT=20"

REM Clean up function
:cleanup
if exist "%TEST_DIR%" (
    echo Cleaning up test directory: %TEST_DIR%
    rmdir /s /q "%TEST_DIR%"
)
goto :eof

REM Build the project first
echo Building Zipper project...
dotnet build "%REPO_ROOT%\Zipper\Zipper.csproj" > nul 2>&1
if %ERRORLEVEL% neq 0 (
    echo ✗ Build failed. Exiting.
    exit /b 1
)

echo Creating test directory: %TEST_DIR%
mkdir "%TEST_DIR%"

REM Test counters
set /a TEST_COUNT=0
set /a PASSED_COUNT=0

REM Main test function
:run_test
set /a TEST_COUNT+=1
set "test_name=%~1"
set "command_args=%~2"
set "expected_headers=%~3"
set "check_text=false"
set "check_attachments=false"
set "attachment_rate=0"

REM Poor man's arg parsing for batch
if "%~4"=="--check-text" (
    set "check_text=true"
    if "%~5"=="--check-attachments" set "check_attachments=true"
)
if "%~4"=="--check-attachments" (
    set "check_attachments=true"
    if "%~5"=="--check-text" set "check_text=true"
)


echo.
echo --------------------------------------------------
echo Test !TEST_COUNT!: !test_name!
echo --------------------------------------------------

set "test_path=%TEST_DIR%\test_!TEST_COUNT!"
mkdir "!test_path!"

set "full_command=%ZIPPER_CMD% %command_args% --output-path "!test_path!""
echo   - Executing: !full_command!

!full_command! > "!test_path!\test_output.log" 2>&1
if %ERRORLEVEL% equ 0 (
    echo   ✓ Command executed successfully.
    cd "!test_path!"

    if not exist "archive_*.zip" (
        echo   ✗ Test FAILED. Archive file not created.
        cd "%REPO_ROOT%"
        goto :eof
    )
    if not exist "archive_*.dat" (
        echo   ✗ Test FAILED. Load file not created.
        cd "%REPO_ROOT%"
        goto :eof
    )

    echo   - Archive and load file created.
    
    set "all_checks_passed=true"

    REM Header Verification
    powershell -NoProfile -Command "$header = Get-Content -Path 'archive_*.dat' -TotalCount 1; $expected = '%expected_headers%'.Split(','); $missing = @(); foreach ($e in $expected) { if ($header -notlike \"*$($e.Trim())*\") { $missing += $e } }; if ($missing.Count -gt 0) { Write-Host \"✗ Header check FAILED. Missing: $($missing -join ', ')\"; exit 1 } else { Write-Host '  ✓ Header check PASSED.'; exit 0 }"
    if !ERRORLEVEL! neq 0 (
        set "all_checks_passed=false"
    )

    REM Text File Verification
    if "!check_text!"=="true" (
        echo   - Verifying extracted text files...
        for %%F in (archive_*.zip) do set "ARCHIVE_FILE=%%F"
        powershell -NoProfile -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; $zip = [System.IO.Compression.ZipFile]::OpenRead('!ARCHIVE_FILE!'); $emlCount = ($zip.Entries | Where-Object { $_.Name -like '*.eml' }).Count; $txtCount = ($zip.Entries | Where-Object { $_.Name -like '*.txt' }).Count; $attachmentCount = ($zip.Entries | Where-Object { $_.FullName -notlike '*.eml' -and $_.FullName -notlike '*.txt' -and $_.Name -ne '' }).Count; $expectedTxtCount = $emlCount + $attachmentCount; $zip.Dispose(); if ($expectedTxtCount -eq $txtCount) { Write-Host \"  ✓ Correct number of text files found ($txtCount).\"; exit 0 } else { Write-Host \"  ✗ Mismatch: Expected $expectedTxtCount TXT files but found $txtCount.\"; exit 1 }"
        if !ERRORLEVEL! neq 0 (
            set "all_checks_passed=false"
        )
    )

    REM Attachment Verification
    if "!check_attachments!"=="true" (
        echo   - Verifying attachments...
        for /f "tokens=1,2 delims== " %%a in ('set command_args') do (
            if "%%a"=="--attachment-rate" set attachment_rate=%%b
        )
        for %%F in (archive_*.dat) do set "DAT_FILE=%%F"
        for %%F in (archive_*.zip) do set "ARCHIVE_FILE=%%F"
        
        powershell -NoProfile -Command "
            $datFile = '!DAT_FILE!';
            $zipFile = '!ARCHIVE_FILE!';
            $attachmentRate = %attachment_rate%;

            Add-Type -AssemblyName System.IO.Compression.FileSystem;
            $zip = [System.IO.Compression.ZipFile]::OpenRead($zipFile);
            $emlCount = ($zip.Entries.Name | Where-Object { $_ -like '*.eml' }).Count;
            $attachmentZipCount = ($zip.Entries | Where-Object { $_.FullName -notlike '*.eml' -and $_.FullName -notlike '*.txt' -and $_.Name -ne '' }).Count;
            $zip.Dispose();

            $header = Get-Content $datFile -TotalCount 1;
            $delimiter = [char]20;
            $columns = $header.Split($delimiter) | ForEach-Object { $_.Trim('\"') };
            $attachmentColIndex = [array]::IndexOf($columns, 'Attachment');

            $attachmentDatCount = 0;
            if ($attachmentColIndex -ge 0) {
                $attachmentDatCount = (Get-Content $datFile | Select-Object -Skip 1 | ForEach-Object { $_.Split($delimiter)[$attachmentColIndex] } | Where-Object { $_.Trim('þ') -ne '' }).Count;
            } else {
                Write-Host '  ✗ Could not find Attachment column.'; exit 1;
            }

            Write-Host \"  - Attachments found in ZIP: $attachmentZipCount\";
            Write-Host \"  - Attachments referenced in DAT: $attachmentDatCount\";

            if ($attachmentDatCount -ne $attachmentZipCount) {
                Write-Host '  ✗ Mismatch between attachments in ZIP and references in DAT file.'; exit 1;
            }

            $minExpected = [int]($emlCount * $attachmentRate / 100 * 0.5);
            if ($attachmentDatCount -ge $minExpected) {
                Write-Host \"  ✓ Attachment count ($attachmentDatCount) is plausible for a $attachmentRate`% rate.\"; exit 0;
            } else {
                Write-Host \"  ✗ Attachment count ($attachmentDatCount) seems too low for a $attachmentRate`% rate (expected at least $minExpected).\"; exit 1;
            }
        "
        if !ERRORLEVEL! neq 0 (
            set "all_checks_passed=false"
        )
    )

    if "!all_checks_passed!"=="true" (
        echo ✓ Test !TEST_COUNT! PASSED
        set /a PASSED_COUNT+=1
    ) else (
        echo ✗ Test !TEST_COUNT! FAILED due to verification errors.
    )
    cd "%REPO_ROOT%"
) else (
    echo ✗ Test !TEST_COUNT! FAILED - Command execution failed.
    type "!test_path!\test_output.log"
)
goto :eof

REM ========================================
REM Test Scenarios
REM ========================================

set "base_headers=Control Number,File Path,Custodian,Date Sent,Author,File Size,To,From,Subject,Sent Date,Attachment"
set "metadata_headers="
set "text_header=Extracted Text"

REM Test 1: Basic EML Generation
call :run_test "Basic EML Generation" "--type eml --count %FILE_COUNT%" "%base_headers%"

REM Test 2: EML with Metadata
call :run_test "EML with Metadata" "--type eml --count %FILE_COUNT% --with-metadata" "%base_headers%"

REM Test 3: EML with Extracted Text
call :run_test "EML with Extracted Text" "--type eml --count %FILE_COUNT% --with-text" "%base_headers%,%text_header%" --check-text

REM Test 4: EML with Both Metadata and Text
call :run_test "EML with Metadata and Text" "--type eml --count %FILE_COUNT% --with-metadata --with-text" "%base_headers%,%text_header%" --check-text

REM Test 5: EML with Attachments (and all flags)
call :run_test "EML with Attachments, Metadata, and Text" "--type eml --count %FILE_COUNT% --with-metadata --with-text --attachment-rate 80" "%base_headers%,%text_header%" --check-text --check-attachments

REM Test 6: High Volume Performance Test
call :run_test "High Volume Performance Test" "--type eml --count 500 --folders 10 --with-metadata --with-text --attachment-rate 25" "%base_headers%,%text_header%" --check-text --check-attachments


REM ========================================
REM Test Results Summary
REM ========================================

echo.
echo ========================================
echo Test Results Summary
echo ========================================
echo Total Tests: %TEST_COUNT%
echo Passed: %PASSED_COUNT%
set /a FAILED_COUNT=%TEST_COUNT%-%PASSED_COUNT%
echo Failed: %FAILED_COUNT%

call :cleanup

if %FAILED_COUNT% equ 0 (
    echo.
    echo ✓ ALL TESTS PASSED - EML feature implementation is working correctly
    exit /b 0
) else (
    echo.
    echo ✗ SOME TESTS FAILED - Please review the implementation
    exit /b 1
)