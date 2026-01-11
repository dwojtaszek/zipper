@echo off
setlocal enabledelayedexpansion

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\office-formats
set PROJECT=Zipper\Zipper.csproj

:: --- Test Setup ---

echo [ INFO ] Running Office Formats E2E Test

:: Clean up previous test results
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: DOCX Generation ---

echo [ INFO ] Test Case 1: DOCX file generation

dotnet run --project "%PROJECT%" -- ^
  --type docx ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\test1" ^
  --folders 3

if errorlevel 1 (
  echo [ ERROR ] Test 1 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test1\*.zip" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 1: No .zip file found
  exit /b 1
)

for %%f in ("%TEST_OUTPUT_DIR%\test1\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 1: No .dat file found
  exit /b 1
)

:: Verify file extension in load file
findstr /C:".docx" "!DAT_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: No .docx extension found in .dat file
  exit /b 1
)

echo [ SUCCESS ] Test Case 1: DOCX generation passed

:: --- Test Case 2: XLSX Generation ---

echo [ INFO ] Test Case 2: XLSX file generation

dotnet run --project "%PROJECT%" -- ^
  --type xlsx ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\test2" ^
  --folders 2

if errorlevel 1 (
  echo [ ERROR ] Test 2 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test2\*.zip" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 2: No .zip file found
  exit /b 1
)

for %%f in ("%TEST_OUTPUT_DIR%\test2\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 2: No .dat file found
  exit /b 1
)

:: Verify file extension in load file
findstr /C:".xlsx" "!DAT_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 2: No .xlsx extension found in .dat file
  exit /b 1
)

echo [ SUCCESS ] Test Case 2: XLSX generation passed

:: --- Test Case 3: DOCX with Metadata ---

echo [ INFO ] Test Case 3: DOCX with metadata

dotnet run --project "%PROJECT%" -- ^
  --type docx ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test3" ^
  --with-metadata

if errorlevel 1 (
  echo [ ERROR ] Test 3 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test3\*.dat") do set DAT_FILE=%%f

:: Check for metadata columns
set /p FIRST_LINE=<"!DAT_FILE!"
echo !FIRST_LINE! | findstr /C:"Custodian" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: 'Custodian' column not found in .dat header
  exit /b 1
)

echo !FIRST_LINE! | findstr /C:"Date Sent" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: 'Date Sent' column not found in .dat header
  exit /b 1
)

echo !FIRST_LINE! | findstr /C:"Author" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: 'Author' column not found in .dat header
  exit /b 1
)

echo !FIRST_LINE! | findstr /C:"File Size" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: 'File Size' column not found in .dat header
  exit /b 1
)

echo [ SUCCESS ] Test Case 3: DOCX with metadata passed

:: --- Test Case 4: DOCX with Bates Numbering ---

echo [ INFO ] Test Case 4: DOCX with Bates numbering

dotnet run --project "%PROJECT%" -- ^
  --type docx ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test4" ^
  --bates-prefix "OFFICE" ^
  --bates-start 500 ^
  --bates-digits 10

if errorlevel 1 (
  echo [ ERROR ] Test 4 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test4\*.dat") do set DAT_FILE=%%f

:: Check for Bates Number column
set /p FIRST_LINE=<"!DAT_FILE!"
echo !FIRST_LINE! | findstr /C:"Bates Number" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: 'Bates Number' column not found in .dat header
  exit /b 1
)

:: Verify Bates numbers
findstr /C:"OFFICE0000000500" "!DAT_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: Bates number 'OFFICE0000000500' not found
  exit /b 1
)

echo [ SUCCESS ] Test Case 4: DOCX with Bates numbering passed

:: --- Test Case 5: XLSX with Multiple Load File Formats ---

echo [ INFO ] Test Case 5: XLSX with different load file formats

for %%F in (dat opt csv xml) do (
  dotnet run --project "%PROJECT%" -- ^
    --type xlsx ^
    --count 3 ^
    --output-path "%TEST_OUTPUT_DIR%\test5_%%F" ^
    --load-file-format "%%F"

  if errorlevel 1 (
    echo [ ERROR ] Test 5 failed for format %%F
    exit /b 1
  )

  echo [ SUCCESS ] Test Case 5: XLSX with %%F format passed
)

:: --- Test Case 6: DOCX Files are Valid ZIP Archives ---

echo [ INFO ] Test Case 6: Verify generated DOCX files are valid ZIP archives

dotnet run --project "%PROJECT%" -- ^
  --type docx ^
  --count 3 ^
  --output-path "%TEST_OUTPUT_DIR%\test6"

if errorlevel 1 (
  echo [ ERROR ] Test 6 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test6\*.zip" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 6: No .zip file found
  exit /b 1
)

:: Extract the zip to verify it contains valid DOCX structure
:: DOCX files should contain [Content_Types].xml, _rels/.rels, and word/document.xml
for %%f in ("%TEST_OUTPUT_DIR%\test6\*.zip") do set ZIP_FILE=%%f

:: Create temp directory for extraction
set TEMP_DIR=%TEMP%\office_test_verify
if exist "%TEMP_DIR%" rmdir /s /q "%TEMP_DIR%"
mkdir "%TEMP_DIR%"

:: Extract the zip
powershell -Command "Expand-Archive -Path '!ZIP_FILE!' -DestinationPath '%TEMP_DIR%' -Force" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 6: Failed to extract ZIP file
  rmdir /s /q "%TEMP_DIR%" 2>nul
  exit /b 1
)

:: Verify required DOCX entries exist
dir /b /s "%TEMP_DIR%\[Content_Types].xml" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 6: DOCX missing [Content_Types].xml
  rmdir /s /q "%TEMP_DIR%" 2>nul
  exit /b 1
)

dir /b /s "%TEMP_DIR%\word\document.xml" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 6: DOCX missing word\document.xml
  rmdir /s /q "%TEMP_DIR%" 2>nul
  exit /b 1
)

:: Clean up
rmdir /s /q "%TEMP_DIR%" 2>nul

echo [ SUCCESS ] Test Case 6: DOCX file structure verified

:: --- All Tests Passed ---

echo [ SUCCESS ] All Office Formats E2E tests passed!
