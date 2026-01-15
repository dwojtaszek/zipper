@echo off
setlocal enabledelayedexpansion

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\bates-numbering
set PROJECT=src\Zipper.csproj

:: --- Test Setup ---

echo [ INFO ] Running Bates Numbering E2E Test

:: Clean up previous test results
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: Basic Bates Numbering ---

echo [ INFO ] Test Case 1: Basic Bates numbering with default prefix

dotnet run --project "%PROJECT%" -- ^
  --type pdf ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\test1" ^
  --bates-prefix "TEST" ^
  --bates-start 1 ^
  --bates-digits 8

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

:: Check for Bates numbers in load file
findstr /C:"TEST00000001" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: Bates number 'TEST00000001' not found in .dat file
  exit /b 1
)

findstr /C:"TEST00000010" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: Bates number 'TEST00000010' not found in .dat file
  exit /b 1
)

:: Verify Bates Number column exists (check first line)
set /p FIRST_LINE=<"%DAT_FILE%"
echo !FIRST_LINE! | findstr /C:"Bates Number" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: 'Bates Number' column not found in .dat header
  exit /b 1
)

echo [ SUCCESS ] Test Case 1: Basic Bates numbering passed

:: --- Test Case 2: Custom Bates Configuration ---

echo [ INFO ] Test Case 2: Custom Bates prefix, start, and digits

dotnet run --project "%PROJECT%" -- ^
  --type pdf ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test2" ^
  --bates-prefix "CLIENT001" ^
  --bates-start 100 ^
  --bates-digits 6

if errorlevel 1 (
  echo [ ERROR ] Test 2 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test2\*.dat") do set DAT_FILE=%%f

findstr /C:"CLIENT001000100" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 2: Bates number 'CLIENT001000100' not found in .dat file
  exit /b 1
)

findstr /C:"CLIENT001000104" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 2: Bates number 'CLIENT001000104' not found in .dat file
  exit /b 1
)

echo [ SUCCESS ] Test Case 2: Custom Bates configuration passed

:: --- Test Case 3: Bates with Different File Types ---

echo [ INFO ] Test Case 3: Bates numbering with TIFF files

dotnet run --project "%PROJECT%" -- ^
  --type tiff ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test3" ^
  --bates-prefix "IMG" ^
  --bates-start 1 ^
  --bates-digits 8

if errorlevel 1 (
  echo [ ERROR ] Test 3 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test3\*.dat") do set DAT_FILE=%%f

findstr /C:"IMG00000001" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 3: Bates number 'IMG00000001' not found in .dat file
  exit /b 1
)

echo [ SUCCESS ] Test Case 3: Bates numbering with TIFF passed

:: --- Test Case 4: Bates with Office Formats ---

echo [ INFO ] Test Case 4: Bates numbering with DOCX files

dotnet run --project "%PROJECT%" -- ^
  --type docx ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test4" ^
  --bates-prefix "DOCX" ^
  --bates-start 500 ^
  --bates-digits 10

if errorlevel 1 (
  echo [ ERROR ] Test 4 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test4\*.dat") do set DAT_FILE=%%f

findstr /C:"DOCX0000000500" "%DAT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 4: Bates number 'DOCX0000000500' not found in .dat file
  exit /b 1
)

echo [ SUCCESS ] Test Case 4: Bates numbering with DOCX passed

:: --- All Tests Passed ---

echo [ SUCCESS ] All Bates Numbering E2E tests passed!
