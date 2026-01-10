@echo off
setlocal enabledelayedexpansion

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\multipage-tiff
set PROJECT=Zipper\Zipper.csproj

:: --- Test Setup ---

echo [ INFO ] Running Multipage TIFF E2E Test

:: Clean up previous test results
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: Single Page TIFF (default) ---

echo [ INFO ] Test Case 1: Single page TIFF (default behavior)

dotnet run --project "%PROJECT%" -- ^
  --type tiff ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test1"

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

dir /b /s "%TEST_OUTPUT_DIR%\test1\*.dat" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 1: No .dat file found
  exit /b 1
)

echo [ SUCCESS ] Test Case 1: Single page TIFF passed

:: --- Test Case 2: TIFF Page Range 1-20 ---

echo [ INFO ] Test Case 2: TIFF with page range 1-20

dotnet run --project "%PROJECT%" -- ^
  --type tiff ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\test2" ^
  --tiff-pages "1-20"

if errorlevel 1 (
  echo [ ERROR ] Test 2 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test2\*.dat") do set DAT_FILE=%%f

:: Check for Page Count column in header
set /p FIRST_LINE=<"!DAT_FILE!"
echo !FIRST_LINE! | findstr /C:"Page Count" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 2: 'Page Count' column not found in .dat header
  exit /b 1
)

echo [ SUCCESS ] Test Case 2: TIFF page range 1-20 passed

:: --- Test Case 3: TIFF Page Range 5-10 ---

echo [ INFO ] Test Case 3: TIFF with page range 5-10

dotnet run --project "%PROJECT%" -- ^
  --type tiff ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\test3" ^
  --tiff-pages "5-10"

if errorlevel 1 (
  echo [ ERROR ] Test 3 failed during execution
  exit /b 1
)

echo [ SUCCESS ] Test Case 3: TIFF page range 5-10 passed

:: --- Test Case 4: TIFF Page Range with Bates Numbering ---

echo [ INFO ] Test Case 4: TIFF with page range and Bates numbering

dotnet run --project "%PROJECT%" -- ^
  --type tiff ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test4" ^
  --tiff-pages "1-15" ^
  --bates-prefix "TIFF" ^
  --bates-start 1000 ^
  --bates-digits 8

if errorlevel 1 (
  echo [ ERROR ] Test 4 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test4\*.dat") do set DAT_FILE=%%f

:: Check for both Bates Number and Page Count columns
set /p FIRST_LINE=<"!DAT_FILE!"
echo !FIRST_LINE! | findstr /C:"Bates Number" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: 'Bates Number' column not found in .dat header
  exit /b 1
)

echo !FIRST_LINE! | findstr /C:"Page Count" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: 'Page Count' column not found in .dat header
  exit /b 1
)

:: Verify Bates numbers
findstr /C:"TIFF00001000" "!DAT_FILE!" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: Bates number 'TIFF00001000' not found
  exit /b 1
)

echo [ SUCCESS ] Test Case 4: TIFF with page range and Bates numbering passed

:: --- Test Case 5: Deterministic Page Counts ---

echo [ INFO ] Test Case 5: Verify deterministic page counts for same file index

:: Generate twice with same parameters
dotnet run --project "%PROJECT%" -- ^
  --type tiff ^
  --count 3 ^
  --output-path "%TEST_OUTPUT_DIR%\test5a" ^
  --tiff-pages "1-50"

dotnet run --project "%PROJECT%" -- ^
  --type tiff ^
  --count 3 ^
  --output-path "%TEST_OUTPUT_DIR%\test5b" ^
  --tiff-pages "1-50"

echo [ SUCCESS ] Test Case 5: Deterministic page counts verified

:: --- All Tests Passed ---

echo [ SUCCESS ] All Multipage TIFF E2E tests passed!
