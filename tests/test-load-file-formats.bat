@echo off
setlocal enabledelayedexpansion

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\load-file-formats
set PROJECT=Zipper\Zipper.csproj

:: --- Test Setup ---

echo [ INFO ] Running Load File Formats E2E Test

:: Clean up previous test results
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: OPT Format ---

echo [ INFO ] Test Case 1: OPT (tab-delimited) format

dotnet run --project "%PROJECT%" -- ^
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

dotnet run --project "%PROJECT%" -- ^
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

dotnet run --project "%PROJECT%" -- ^
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

findstr /C:"<documents>" "!XML_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: Root element ^<documents^> not found
  exit /b 1
)

findstr /C:"<document>" "!XML_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: ^<document^> element not found
  exit /b 1
)

echo [ SUCCESS ] Test Case 3: XML format passed

:: --- Test Case 4: CONCORDANCE Format ---

echo [ INFO ] Test Case 4: CONCORDANCE format

dotnet run --project "%PROJECT%" -- ^
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

dotnet run --project "%PROJECT%" -- ^
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

for %%F in (dat opt csv xml) do (
  set FORMAT=%%F
  set EXT=%%F
  if "%%F"=="concordance" set EXT=dat

  dotnet run --project "%PROJECT%" -- ^
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

:: --- All Tests Passed ---

echo [ SUCCESS ] All Load File Formats E2E tests passed!
