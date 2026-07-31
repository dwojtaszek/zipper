@echo off

REM Resolve the Zipper binary once. Sets %ZIPPER_CMD%.
call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\source-driven

:: --- Test Setup ---

echo [ INFO ] Running Source-Driven Generation E2E Test

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: Source CSV drives archive paths and Load File identity ---

echo [ INFO ] Test Case 1: Source CSV (3 mixed rows) drives archive entries and DAT rows

(
echo ControlNumber,FilePath,FileType
echo CTRL-001,docs/a.pdf,pdf
echo CTRL-002,b.eml,eml
echo CTRL-003,deep/nested/c.tiff,tiff
) > "%TEST_OUTPUT_DIR%\source.csv"

%ZIPPER_CMD% ^
  --input-csv "%TEST_OUTPUT_DIR%\source.csv" ^
  --output-path "%TEST_OUTPUT_DIR%\csv_archive" ^
  --seed 42

if errorlevel 1 (
  echo [ ERROR ] Test 1 failed during execution
  exit /b 1
)

set ZIP_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\csv_archive\*.zip") do set ZIP_FILE=%%f
if not defined ZIP_FILE (
  echo [ ERROR ] Test 1: No .zip file found
  exit /b 1
)

set DAT_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\csv_archive\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 1: No .dat file found
  exit /b 1
)

set OPT_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\csv_archive\*.opt") do set OPT_FILE=%%f
if not defined OPT_FILE (
  echo [ ERROR ] Test 1: No .opt file found (tiff row should default to DAT+OPT^)
  exit /b 1
)

powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; $z=[System.IO.Compression.ZipFile]::OpenRead('%ZIP_FILE%'); $n=$z.Entries.FullName -join '|'; $z.Dispose(); if ($n -notmatch 'docs/a\.pdf' -or $n -notmatch 'b\.eml' -or $n -notmatch 'deep/nested/c\.tiff') { exit 1 }" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: Archive entries do not match source relative paths
  exit /b 1
)

set /p HEADER=<"%DAT_FILE%"
echo !HEADER! | findstr /C:"File Type" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: 'File Type' column not found in .dat header (mixed source types)
  exit /b 1
)

findstr /C:"CTRL-001" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: Control Number override CTRL-001 not found in .dat
  exit /b 1
)

findstr /C:"deep/nested/c.tiff" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: Source path deep/nested/c.tiff not found in .dat
  exit /b 1
)

echo [ SUCCESS ] Test Case 1: Source CSV archive passed

:: --- Test Case 2: Directory template mirrors nested structure ---

echo [ INFO ] Test Case 2: Directory template recreates nested paths without copying content

mkdir "%TEST_OUTPUT_DIR%\template\folder_a\deep"
echo real content a > "%TEST_OUTPUT_DIR%\template\root.pdf"
echo real content b > "%TEST_OUTPUT_DIR%\template\folder_a\inner.eml"
echo real content c > "%TEST_OUTPUT_DIR%\template\folder_a\deep\x.tiff"

%ZIPPER_CMD% ^
  --directory-template "%TEST_OUTPUT_DIR%\template" ^
  --output-path "%TEST_OUTPUT_DIR%\dir_archive" ^
  --seed 42

if errorlevel 1 (
  echo [ ERROR ] Test 2 failed during execution
  exit /b 1
)

set ZIP_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\dir_archive\*.zip") do set ZIP_FILE=%%f
if not defined ZIP_FILE (
  echo [ ERROR ] Test 2: No .zip file found
  exit /b 1
)

powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; $z=[System.IO.Compression.ZipFile]::OpenRead('%ZIP_FILE%'); $n=$z.Entries.FullName -join '|'; $z.Dispose(); if ($n -notmatch 'root\.pdf' -or $n -notmatch 'folder_a/inner\.eml' -or $n -notmatch 'folder_a/deep/x\.tiff') { exit 1 }" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 2: Archive does not mirror the template structure
  exit /b 1
)

powershell -Command "Add-Type -AssemblyName System.IO.Compression.FileSystem; $z=[System.IO.Compression.ZipFile]::OpenRead('%ZIP_FILE%'); $bad=$false; foreach ($e in $z.Entries) { $sr = New-Object System.IO.StreamReader($e.Open()); $c = $sr.ReadToEnd(); $sr.Close(); if ($c -match 'real content') { $bad=$true } }; $z.Dispose(); if ($bad) { exit 1 }" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 2: Source bytes were copied into the archive (placeholders required)
  exit /b 1
)

echo [ SUCCESS ] Test Case 2: Directory template passed

:: --- Test Case 3: Loadfile-Only source CSV emits records without natives ---

echo [ INFO ] Test Case 3: --loadfile-only with source CSV creates only Load File output

%ZIPPER_CMD% ^
  --loadfile-only ^
  --input-csv "%TEST_OUTPUT_DIR%\source.csv" ^
  --output-path "%TEST_OUTPUT_DIR%\csv_loadfile" ^
  --seed 42

if errorlevel 1 (
  echo [ ERROR ] Test 3 failed during execution
  exit /b 1
)

if exist "%TEST_OUTPUT_DIR%\csv_loadfile\*.zip" (
  echo [ ERROR ] Test 3: Loadfile-Only must not create an Archive
  exit /b 1
)

set DAT_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\csv_loadfile\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 3: No .dat file found
  exit /b 1
)

findstr /C:"CTRL-002" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: Control Number override CTRL-002 not found in .dat
  exit /b 1
)

echo [ SUCCESS ] Test Case 3: Loadfile-Only source passed

:: --- Test Case 4: Extra CSV columns map through a Column Profile ---

echo [ INFO ] Test Case 4: Extra source Metadata column surfaces via --column-profile

(
echo FilePath,FileType,Reviewed
echo a.pdf,pdf,yes-source
echo b.pdf,pdf,no-source
) > "%TEST_OUTPUT_DIR%\profile.csv"

(
echo {
echo   "name": "source-test",
echo   "settings": { "emptyValuePercentage": 0 },
echo   "columns": [
echo     { "name": "DOCID", "type": "identifier", "required": true },
echo     { "name": "FILEPATH", "type": "text", "required": true },
echo     { "name": "REVIEWED", "type": "text", "required": true }
echo   ]
echo }
) > "%TEST_OUTPUT_DIR%\profile.json"

%ZIPPER_CMD% ^
  --input-csv "%TEST_OUTPUT_DIR%\profile.csv" ^
  --column-profile "%TEST_OUTPUT_DIR%\profile.json" ^
  --output-path "%TEST_OUTPUT_DIR%\profile_out" ^
  --seed 42

if errorlevel 1 (
  echo [ ERROR ] Test 4 failed during execution
  exit /b 1
)

set DAT_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\profile_out\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 4: No .dat file found
  exit /b 1
)

findstr /C:"yes-source" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: Source Metadata value 'yes-source' not mapped into the .dat
  exit /b 1
)

echo [ SUCCESS ] Test Case 4: Column Profile source mapping passed

:: --- Test Case 5: Bates override from source rows ---

echo [ INFO ] Test Case 5: BatesNumber column overrides Bates sequence values

(
echo FilePath,FileType,BatesNumber
echo a.pdf,pdf,ABC_00000099
echo b.pdf,pdf,
) > "%TEST_OUTPUT_DIR%\bates.csv"

%ZIPPER_CMD% ^
  --input-csv "%TEST_OUTPUT_DIR%\bates.csv" ^
  --bates-prefix "ABC" ^
  --output-path "%TEST_OUTPUT_DIR%\bates_out" ^
  --seed 42

if errorlevel 1 (
  echo [ ERROR ] Test 5 failed during execution
  exit /b 1
)

set DAT_FILE=
for %%f in ("%TEST_OUTPUT_DIR%\bates_out\*.dat") do set DAT_FILE=%%f
if not defined DAT_FILE (
  echo [ ERROR ] Test 5: No .dat file found
  exit /b 1
)

findstr /C:"ABC_00000099" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 5: Bates override ABC_00000099 not found in .dat
  exit /b 1
)

findstr /C:"ABC00000002" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 5: Sequence Bates ABC00000002 not found in .dat
  exit /b 1
)

echo [ SUCCESS ] Test Case 5: Bates override passed

:: --- Test Case 6: Validation failures ---

echo [ INFO ] Test Case 6: Source input validation failures

(
echo FilePath,FileType
echo ../escape.pdf,pdf
) > "%TEST_OUTPUT_DIR%\traversal.csv"

%ZIPPER_CMD% --input-csv "%TEST_OUTPUT_DIR%\traversal.csv" --output-path "%TEST_OUTPUT_DIR%\val1" >nul 2>"%temp%\source_val1.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 6: path traversal row should fail
  exit /b 1
)
findstr /C:"Row 2" "%temp%\source_val1.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 6: traversal error should name the offending row
  exit /b 1
)

%ZIPPER_CMD% --input-csv "%TEST_OUTPUT_DIR%\source.csv" --count 5 --output-path "%TEST_OUTPUT_DIR%\val2" >nul 2>"%temp%\source_val2.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 6: --count mismatch should fail
  exit /b 1
)
findstr /C:"does not match" "%temp%\source_val2.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 6: count mismatch message not found
  exit /b 1
)

%ZIPPER_CMD% --input-csv "%TEST_OUTPUT_DIR%\source.csv" --production-set --bates-prefix "ABC" --output-path "%TEST_OUTPUT_DIR%\val3" >nul 2>"%temp%\source_val3.err"
if errorlevel 1 (
  echo [ ERROR ] Test 6: --production-set with source input should succeed
  exit /b 1
)
set "FOUND_DAT="
for /d %%d in ("%TEST_OUTPUT_DIR%\val3\*") do if exist "%%d\DATA\loadfile.dat" set "FOUND_DAT=1"
if not defined FOUND_DAT (
  echo [ ERROR ] Test 6: source-driven production set did not produce a DAT load file
  exit /b 1
)

mkdir "%TEST_OUTPUT_DIR%\bad-template"
echo x > "%TEST_OUTPUT_DIR%\bad-template\notes.txt"
%ZIPPER_CMD% --directory-template "%TEST_OUTPUT_DIR%\bad-template" --output-path "%TEST_OUTPUT_DIR%\val4" >nul 2>"%temp%\source_val4.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 6: unsupported template extension should fail
  exit /b 1
)
findstr /C:".txt" "%temp%\source_val4.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 6: unsupported extension message not found
  exit /b 1
)

%ZIPPER_CMD% --input-csv "%TEST_OUTPUT_DIR%\missing.csv" --output-path "%TEST_OUTPUT_DIR%\val5" >nul 2>"%temp%\source_val5.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 6: missing source CSV should fail
  exit /b 1
)
findstr /C:"does not exist" "%temp%\source_val5.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 6: missing file message not found
  exit /b 1
)

%ZIPPER_CMD% --input-csv "%TEST_OUTPUT_DIR%\source.csv" --type pdf --output-path "%TEST_OUTPUT_DIR%\val6" >nul 2>nul
if not errorlevel 1 (
  echo [ ERROR ] Test 6: --type with --input-csv should fail
  exit /b 1
)

(
echo FilePath,FileType,ControlNumber
echo a.pdf,pdf,ABC-001
echo b.eml,eml,ABC-001
) > "%TEST_OUTPUT_DIR%\dup-control.csv"
%ZIPPER_CMD% --input-csv "%TEST_OUTPUT_DIR%\dup-control.csv" --output-path "%TEST_OUTPUT_DIR%\val7" >nul 2>"%temp%\source_val7.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 6: duplicate ControlNumber should fail
  exit /b 1
)
findstr /C:"Duplicate ControlNumber" "%temp%\source_val7.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 6: duplicate ControlNumber message not found
  exit /b 1
)

(
echo FilePath,FileType
echo folder/CON.pdf,pdf
) > "%TEST_OUTPUT_DIR%\reserved.csv"
%ZIPPER_CMD% --input-csv "%TEST_OUTPUT_DIR%\reserved.csv" --output-path "%TEST_OUTPUT_DIR%\val8" >nul 2>"%temp%\source_val8.err"
if not errorlevel 1 (
  echo [ ERROR ] Test 6: reserved device name path should fail
  exit /b 1
)
findstr /C:"reserved Windows device name" "%temp%\source_val8.err" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 6: reserved device name message not found
  exit /b 1
)

echo [ SUCCESS ] Test Case 6: Validation failures passed

:: --- All Tests Passed ---

rmdir /s /q "%TEST_OUTPUT_DIR%"
echo [ SUCCESS ] All Source-Driven Generation E2E tests passed!
