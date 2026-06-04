@echo off

REM Resolve the Zipper binary once. Sets %ZIPPER_CMD%.
call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\load-file-formats
set PROJECT=src\Zipper.csproj

:: --- Test Setup ---

echo [ INFO ] Running Load File Formats E2E Test

:: Clean up previous test results
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: OPT Format ---

echo [ INFO ] Test Case 1: OPT (tab-delimited) format

%ZIPPER_CMD% ^
  --type pdf ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\test1" ^
  --load-file-format opt

if errorlevel 1 (
  echo [ ERROR ] Test 1 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test1\*.opt" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 1: No .opt file found
  exit /b 1
)

echo [ SUCCESS ] Test Case 1: OPT format passed

:: --- Test Case 2: CSV Format ---

echo [ INFO ] Test Case 2: CSV format

%ZIPPER_CMD% ^
  --type pdf ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\test2" ^
  --load-file-format csv

if errorlevel 1 (
  echo [ ERROR ] Test 2 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test2\*.csv" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 2: No .csv file found
  exit /b 1
)

:: Verify header contains expected columns
for %%f in ("%TEST_OUTPUT_DIR%\test2\*.csv") do set CSV_FILE=%%f
set /p FIRST_LINE=<"!CSV_FILE!"
echo !FIRST_LINE! | findstr /C:"Control Number" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 2: 'Control Number' column not found in .csv header
  exit /b 1
)

echo [ SUCCESS ] Test Case 2: CSV format passed

:: --- Test Case 3: XML Format ---

echo [ INFO ] Test Case 3: XML format

%ZIPPER_CMD% ^
  --type pdf ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test3" ^
  --load-file-format xml

if errorlevel 1 (
  echo [ ERROR ] Test 3 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test3\*.xml" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 3: No .xml file found
  exit /b 1
)

:: Verify XML structure
for %%f in ("%TEST_OUTPUT_DIR%\test3\*.xml") do set XML_FILE=%%f
findstr /C:"<?xml" "!XML_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: XML declaration not found
  exit /b 1
)

findstr /C:"<Root DataInterchangeType=" "!XML_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: Root element ^<Root DataInterchangeType=...^> not found
  exit /b 1
)

findstr /C:"<Documents>" "!XML_FILE!" >nul
if not errorlevel 1 (
  echo [ ERROR ] Test 3: Plural wrapper ^<Documents^> element should not be present
  exit /b 1
)

findstr /C:"<Document DocID=" "!XML_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: ^<Document DocID=...^> element not found
  exit /b 1
)

echo [ SUCCESS ] Test Case 3: XML format passed

:: --- Test Case 4: CONCORDANCE Format ---

echo [ INFO ] Test Case 4: CONCORDANCE format

%ZIPPER_CMD% ^
  --type pdf ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test4" ^
  --load-file-format concordance

if errorlevel 1 (
  echo [ ERROR ] Test 4 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test4\*.dat" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 4: No .dat file found
  exit /b 1
)

:: CONCORDANCE uses CONTROLNUMBER header
for %%f in ("%TEST_OUTPUT_DIR%\test4\*.dat") do set CONC_FILE=%%f
findstr /C:"CONTROLNUMBER" "!CONC_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: 'CONTROLNUMBER' column not found in .dat header
  exit /b 1
)

echo [ SUCCESS ] Test Case 4: CONCORDANCE format passed

:: --- Test Case 5: Default DAT Format ---

echo [ INFO ] Test Case 5: Default DAT format (with caret delimiter)

%ZIPPER_CMD% ^
  --type pdf ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test5" ^
  --load-file-format dat

if errorlevel 1 (
  echo [ ERROR ] Test 5 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test5\*.dat" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 5: No .dat file found
  exit /b 1
)

echo [ SUCCESS ] Test Case 5: Default DAT format passed

:: --- Test Case 6: Load File Formats with Bates Numbering ---

echo [ INFO ] Test Case 6: Load file formats with Bates numbering

for %%F in (dat opt csv xml concordance) do (
  set FORMAT=%%F
  set EXT=%%F
  if "%%F"=="concordance" set EXT=dat

  %ZIPPER_CMD% ^
    --type pdf ^
    --count 3 ^
    --output-path "%TEST_OUTPUT_DIR%\test6_%%F" ^
    --load-file-format "%%F" ^
    --bates-prefix "TEST" ^
    --bates-start 1 ^
    --bates-digits 6

  if errorlevel 1 (
    echo [ ERROR ] Test 6 failed for format %%F
    exit /b 1
  )

  echo [ SUCCESS ] Test Case 6: Bates numbering with %%F format passed
)

:: --- Test Case 7: Custom Delimiters (Pipe and Caret) ---

echo [ INFO ] Test Case 7: Custom delimiters (pipe and caret)

%ZIPPER_CMD% ^
  --type pdf ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test7" ^
  --load-file-format dat ^
  --delimiter-column "|" ^
  --delimiter-quote "^"

if errorlevel 1 (
  echo [ ERROR ] Test 7 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test7\*.dat") do set DAT_FILE=%%f
if not exist "!DAT_FILE!" (
  echo [ ERROR ] Test 7: No .dat file found
  exit /b 1
)

:: Check for pipe delimiter
findstr /C:"|" "!DAT_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 7: No pipe delimiter found in .dat file
  exit /b 1
)

echo [ SUCCESS ] Test Case 7: Custom delimiters passed

:: --- Test Case 8: ASCII Code Delimiters ---

echo [ INFO ] Test Case 8: ASCII code delimiters (20, 254)

%ZIPPER_CMD% ^
  --type pdf ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test8" ^
  --load-file-format dat ^
  --delimiter-column "20" ^
  --delimiter-quote "254"

if errorlevel 1 (
  echo [ ERROR ] Test 8 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test8\*.dat") do set DAT_FILE=%%f
if not exist "!DAT_FILE!" (
  echo [ ERROR ] Test 8: No .dat file found
  exit /b 1
)

:: Verify file has content
for %%f in ("!DAT_FILE!") do set FILE_SIZE=%%~zf
if !FILE_SIZE! LEQ 0 (
  echo [ ERROR ] Test 8: DAT file is empty
  exit /b 1
)

echo [ SUCCESS ] Test Case 8: ASCII code delimiters passed

:: --- Test Case 9: Delimiter Override (CSV preset with pipe override) ---

echo [ INFO ] Test Case 9: Delimiter override (CSV preset with pipe column delimiter)

%ZIPPER_CMD% ^
  --type pdf ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test9" ^
  --load-file-format dat ^
  --dat-delimiters csv ^
  --delimiter-column "|"

if errorlevel 1 (
  echo [ ERROR ] Test 9 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test9\*.dat") do set DAT_FILE=%%f
if not exist "!DAT_FILE!" (
  echo [ ERROR ] Test 9: No .dat file found
  exit /b 1
)

:: Check for pipe delimiter (override)
findstr /C:"|" "!DAT_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 9: No pipe delimiter found (should override CSV preset)
  exit /b 1
)

:: Check for double-quote (from CSV preset)
findstr /C:"\"" "!DAT_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 9: No double-quote found (should use CSV preset for quote)
  exit /b 1
)

echo [ SUCCESS ] Test Case 9: Delimiter override passed

:: --- Test Case 10: Auto OPT generation for tiff/jpg types ---

echo [ INFO ] Test Case 10: Auto OPT generation for tiff and jpg

:: Run for tiff in loadfile-only mode
%ZIPPER_CMD% ^
  --type tiff ^
  --count 3 ^
  --output-path "%TEST_OUTPUT_DIR%\test10_tiff" ^
  --loadfile-only ^
  --bates-prefix "TIFF" ^
  --bates-start 1 ^
  --bates-digits 5

if errorlevel 1 (
  echo [ ERROR ] Test 10 failed during execution for tiff
  exit /b 1
)

dir /b /s "%TEST_OUTPUT_DIR%\test10_tiff\*.dat" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 10: No .dat file found for tiff
  exit /b 1
)

dir /b /s "%TEST_OUTPUT_DIR%\test10_tiff\*.opt" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 10: No .opt file found for tiff
  exit /b 1
)

:: Verify Bates prefix in OPT
for %%f in ("%TEST_OUTPUT_DIR%\test10_tiff\*.opt") do set OPT_FILE=%%f
findstr /C:"TIFF00001" "!OPT_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 10: Base Bates number 'TIFF00001' not found in tiff OPT file
  exit /b 1
)

:: Run for jpg in standard mode
%ZIPPER_CMD% ^
  --type jpg ^
  --count 3 ^
  --output-path "%TEST_OUTPUT_DIR%\test10_jpg"

if errorlevel 1 (
  echo [ ERROR ] Test 10 failed during execution for jpg
  exit /b 1
)

dir /b /s "%TEST_OUTPUT_DIR%\test10_jpg\*.zip" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 10: No .zip file found for jpg
  exit /b 1
)

dir /b /s "%TEST_OUTPUT_DIR%\test10_jpg\*.dat" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 10: No .dat file found for jpg
  exit /b 1
)

dir /b /s "%TEST_OUTPUT_DIR%\test10_jpg\*.opt" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 10: No .opt file found for jpg
  exit /b 1
)

echo [ SUCCESS ] Test Case 10: Auto OPT generation passed

:: --- All Tests Passed ---

echo [ SUCCESS ] All Load File Formats E2E tests passed!
