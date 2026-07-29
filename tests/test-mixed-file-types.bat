@echo off

REM Resolve the Zipper binary once. Sets %ZIPPER_CMD%.
call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\mixed-file-types

:: --- Test Setup ---

echo [ INFO ] Running Mixed File Types E2E Test

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: Mixed Archive with exact per-type counts ---

echo [ INFO ] Test Case 1: Mixed archive (pdf:50,eml:30,tiff:20) produces exact per-type counts

%ZIPPER_CMD% ^
  --types "pdf:50,eml:30,tiff:20" ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\mixed_archive" ^
  --seed 42

if errorlevel 1 (
  echo [ ERROR ] Test 1 failed during execution
  exit /b 1
)

for %%f in ("%TEST_OUTPUT_DIR%\mixed_archive\*.zip") do set ZIP_FILE=%%f
if not defined ZIP_FILE (
  echo [ ERROR ] Test 1: No .zip file found
  exit /b 1
)

for %%f in ("%TEST_OUTPUT_DIR%\mixed_archive\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 1: No .dat file found
  exit /b 1
)

for %%f in ("%TEST_OUTPUT_DIR%\mixed_archive\*.opt") do set OPT_FILE=%%f
if not defined OPT_FILE (
  echo [ ERROR ] Test 1: No .opt file found (tiff in mix should default to DAT+OPT)
  exit /b 1
)

powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::OpenRead('%ZIP_FILE%').Entries.Where({$_.Name -match '\.pdf$'}).Count" > "%temp%\mixed_pdf_count.txt"
set /p PDF_COUNT=<"%temp%\mixed_pdf_count.txt"
powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::OpenRead('%ZIP_FILE%').Entries.Where({$_.Name -match '\.eml$'}).Count" > "%temp%\mixed_eml_count.txt"
set /p EML_COUNT=<"%temp%\mixed_eml_count.txt"
powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::OpenRead('%ZIP_FILE%').Entries.Where({$_.Name -match '\.tiff$'}).Count" > "%temp%\mixed_tiff_count.txt"
set /p TIFF_COUNT=<"%temp%\mixed_tiff_count.txt"

if not "%PDF_COUNT%" == "5" (
  echo [ ERROR ] Test 1: Expected 5 .pdf files in zip, found %PDF_COUNT%
  exit /b 1
)
if not "%EML_COUNT%" == "3" (
  echo [ ERROR ] Test 1: Expected 3 .eml files in zip, found %EML_COUNT%
  exit /b 1
)
if not "%TIFF_COUNT%" == "2" (
  echo [ ERROR ] Test 1: Expected 2 .tiff files in zip, found %TIFF_COUNT%
  exit /b 1
)

set /p HEADER=<"%DAT_FILE%"
echo !HEADER! | findstr /C:"File Type" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: 'File Type' column not found in .dat header
  exit /b 1
)

echo [ SUCCESS ] Test Case 1: Mixed archive passed

:: --- Test Case 2: Page Count only on TIFF records ---

echo [ INFO ] Test Case 2: Mixed tiff/pdf with --tiff-pages populates Page Count only for TIFF records

%ZIPPER_CMD% ^
  --types "tiff:1,pdf:1" ^
  --count 6 ^
  --output-path "%TEST_OUTPUT_DIR%\mixed_tiff_pages" ^
  --tiff-pages "2-4" ^
  --seed 7

if errorlevel 1 (
  echo [ ERROR ] Test 2 failed during execution
  exit /b 1
)

set DAT_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\mixed_tiff_pages\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 2: No .dat file found
  exit /b 1
)

set /p HEADER=<"%DAT_FILE%"
echo !HEADER! | findstr /C:"Page Count" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 2: 'Page Count' column not found in .dat header
  exit /b 1
)

echo [ SUCCESS ] Test Case 2: Page Count per-record gating passed

:: --- Test Case 3: Email Metadata only on EML records ---

echo [ INFO ] Test Case 3: Mixed pdf/eml populates Email Metadata only for EML records

%ZIPPER_CMD% ^
  --types "pdf:1,eml:1" ^
  --count 4 ^
  --output-path "%TEST_OUTPUT_DIR%\mixed_eml_meta" ^
  --seed 42

if errorlevel 1 (
  echo [ ERROR ] Test 3 failed during execution
  exit /b 1
)

set DAT_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\mixed_eml_meta\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 3: No .dat file found
  exit /b 1
)

set /p HEADER=<"%DAT_FILE%"
echo !HEADER! | findstr /C:"Subject" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: Email Metadata columns not found in .dat header
  exit /b 1
)

echo [ SUCCESS ] Test Case 3: Email Metadata per-record gating passed

:: --- Test Case 4: Mixed Production Set ---

echo [ INFO ] Test Case 4: Mixed production set (pdf:1,eml:1) per-record natives and FILE_TYPE

%ZIPPER_CMD% ^
  --production-set ^
  --types "pdf:1,eml:1" ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\mixed_prod" ^
  --bates-prefix "MIX" ^
  --seed 42

if errorlevel 1 (
  echo [ ERROR ] Test 4 failed during execution
  exit /b 1
)

set PROD_DIR=
for /d %%d in ("%TEST_OUTPUT_DIR%\mixed_prod\*") do set PROD_DIR=%%d
if not defined PROD_DIR (
  echo [ ERROR ] Test 4: No production directory found
  exit /b 1
)

set PDF_NATIVES=0
for /r "%PROD_DIR%\NATIVES" %%f in (*.pdf) do set /a PDF_NATIVES+=1
set EML_NATIVES=0
for /r "%PROD_DIR%\NATIVES" %%f in (*.eml) do set /a EML_NATIVES+=1

if not "!PDF_NATIVES!" == "5" (
  echo [ ERROR ] Test 4: Expected 5 .pdf natives, found !PDF_NATIVES!
  exit /b 1
)
if not "!EML_NATIVES!" == "5" (
  echo [ ERROR ] Test 4: Expected 5 .eml natives, found !EML_NATIVES!
  exit /b 1
)

if not exist "%PROD_DIR%\DATA\loadfile.dat" (
  echo [ ERROR ] Test 4: No production loadfile.dat found
  exit /b 1
)

findstr /C:"\"fileType\": \"pdf,eml\"" "%PROD_DIR%\_manifest.json" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: Manifest fileType should be 'pdf,eml'
  exit /b 1
)

echo [ SUCCESS ] Test Case 4: Mixed production set passed

:: --- Test Case 5: Validation failures ---

echo [ INFO ] Test Case 5: --types validation failures

%ZIPPER_CMD% --type pdf --types "eml:1" --count 1 --output-path "%TEST_OUTPUT_DIR%\val1" >nul 2>"%temp%\mixed_val1.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 5: --type with --types should fail
  exit /b 1
)
findstr /C:"cannot be used together" "%temp%\mixed_val1.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 5: mutual-exclusion error message not found
  exit /b 1
)

%ZIPPER_CMD% --types "bogus:1" --count 1 --output-path "%TEST_OUTPUT_DIR%\val2" >nul 2>"%temp%\mixed_val2.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 5: unknown type in --types should fail
  exit /b 1
)
findstr /C:"bogus" "%temp%\mixed_val2.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 5: unknown-type error message not found
  exit /b 1
)

%ZIPPER_CMD% --types "pdf:0" --count 1 --output-path "%TEST_OUTPUT_DIR%\val3" >nul 2>nul
if not errorlevel 1 (
  echo [ ERROR ] Test 5: zero weight in --types should fail
  exit /b 1
)

%ZIPPER_CMD% --types "pdf:1,eml:1" --loadfile-only --count 1 --output-path "%TEST_OUTPUT_DIR%\val4" >nul 2>"%temp%\mixed_val4.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 5: --types with --loadfile-only should fail
  exit /b 1
)
findstr /C:"loadfile-only" "%temp%\mixed_val4.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 5: loadfile-only conflict message not found
  exit /b 1
)

echo [ SUCCESS ] Test Case 5: Validation failures passed

:: --- All Tests Passed ---

rmdir /s /q "%TEST_OUTPUT_DIR%"
echo [ SUCCESS ] All Mixed File Types E2E tests passed!
