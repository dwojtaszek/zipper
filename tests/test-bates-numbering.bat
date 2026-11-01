@echo off

REM Resolve the Zipper binary once. Sets %ZIPPER_CMD%.
call "%~dp0_zipper-cli.bat"
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

%ZIPPER_CMD% ^
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

%ZIPPER_CMD% ^
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

%ZIPPER_CMD% ^
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

%ZIPPER_CMD% ^
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

:: --- Test Case 5: Page-level Bates suffix on one-page documents ---

echo [ INFO ] Test Case 5: Page-level Bates suffix (--bates-page-suffix)

%ZIPPER_CMD% ^
  --type pdf ^
  --count 3 ^
  --output-path "%TEST_OUTPUT_DIR%\test5" ^
  --bates-prefix "PGS" ^
  --bates-page-suffix ^
  --load-file-format opt

if errorlevel 1 (
  echo [ ERROR ] Test 5 failed during execution
  exit /b 1
)

:: Verify output
for %%f in ("%TEST_OUTPUT_DIR%\test5\*.opt") do set OPT_FILE=%%f

findstr /B /C:"PGS00000001_001," "%OPT_FILE%" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 5: Page-level Bates 'PGS00000001_001' not found in .opt file
  exit /b 1
)

findstr /B /C:"PGS00000001," "%OPT_FILE%" >nul
if not errorlevel 1 (
  echo [ ERROR ] Test 5: Document-level Bates 'PGS00000001' should not be an OPT key with --bates-page-suffix
  exit /b 1
)

echo [ SUCCESS ] Test Case 5: Page-level Bates suffix passed

:: --- Test Case 6: Over-limit Bates configuration warns and still generates ---

echo [ INFO ] Test Case 6: Bates prefix over the Everlaw limit warns

%ZIPPER_CMD% ^
  --type pdf ^
  --count 2 ^
  --output-path "%TEST_OUTPUT_DIR%\test6" ^
  --bates-prefix "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDE" > "%TEST_OUTPUT_DIR%\test6-output.txt" 2>&1

if errorlevel 1 (
  echo [ ERROR ] Test 6 failed: over-limit Bates prefix must warn, not fail
  exit /b 1
)

findstr /C:"Warning: Bates prefix 'ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCDE' is 41 characters" "%TEST_OUTPUT_DIR%\test6-output.txt" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 6: Everlaw prefix warning not printed
  exit /b 1
)

echo [ SUCCESS ] Test Case 6: Over-limit Bates prefix warning passed

:: --- Test Case 7: Page-suffix width warning follows the pages each output numbers ---

echo [ INFO ] Test Case 7: Page-suffix width warning covers Loadfile-Only pages only

%ZIPPER_CMD% ^
  --type pdf ^
  --count 1 ^
  --output-path "%TEST_OUTPUT_DIR%\test7-pdf" ^
  --bates-prefix "CL001" ^
  --bates-digits 12 ^
  --bates-page-suffix ^
  --tiff-pages 1-1000 > "%TEST_OUTPUT_DIR%\test7-pdf-output.txt" 2>&1

if errorlevel 1 (
  echo [ ERROR ] Test 7 failed: PDF run with --tiff-pages past 999 must not fail
  exit /b 1
)

findstr /C:"Warning: Bates numbers are" "%TEST_OUTPUT_DIR%\test7-pdf-output.txt" >nul
if not errorlevel 1 (
  echo [ ERROR ] Test 7: PDF run should not warn about a page suffix it never numbers
  exit /b 1
)

%ZIPPER_CMD% ^
  --type pdf ^
  --count 1 ^
  --loadfile-only ^
  --output-path "%TEST_OUTPUT_DIR%\test7-loadfile" ^
  --load-file-format opt ^
  --bates-prefix "CL001" ^
  --bates-digits 12 ^
  --bates-page-suffix ^
  --tiff-pages 1000-1000 > "%TEST_OUTPUT_DIR%\test7-loadfile-output.txt" 2>&1

if errorlevel 1 (
  echo [ ERROR ] Test 7 failed: Loadfile-Only run with --tiff-pages 1000 must not fail
  exit /b 1
)

findstr /C:"Warning: Bates numbers are 17 characters including the page suffix" "%TEST_OUTPUT_DIR%\test7-loadfile-output.txt" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 7: Loadfile-Only page suffix width warning not printed
  exit /b 1
)

echo [ SUCCESS ] Test Case 7: Page-suffix width warning passed

:: --- Test Case 8: Page-suffix width warning needs a page label in the output ---

echo [ INFO ] Test Case 8: Page-suffix width warning needs a page label in the output

%ZIPPER_CMD% ^
  --type tiff ^
  --count 1 ^
  --output-path "%TEST_OUTPUT_DIR%\test8-plain-dat" ^
  --bates-prefix "CL001" ^
  --bates-digits 12 ^
  --bates-page-suffix ^
  --tiff-pages 1000-1000 ^
  --loadfile-format dat > "%TEST_OUTPUT_DIR%\test8-plain-dat-output.txt" 2>&1

if errorlevel 1 (
  echo [ ERROR ] Test 8 failed: plain TIFF DAT run with --tiff-pages 1000 must not fail
  exit /b 1
)

findstr /C:"Warning: Bates numbers are" "%TEST_OUTPUT_DIR%\test8-plain-dat-output.txt" >nul
if not errorlevel 1 (
  echo [ ERROR ] Test 8: plain TIFF DAT run should not warn about a page suffix it never writes
  exit /b 1
)

%ZIPPER_CMD% ^
  --type tiff ^
  --count 1 ^
  --output-path "%TEST_OUTPUT_DIR%\test8-realistic-dat" ^
  --bates-prefix "CL001" ^
  --bates-digits 12 ^
  --bates-page-suffix ^
  --tiff-pages 1000-1000 ^
  --loadfile-format dat ^
  --with-tiff-realism > "%TEST_OUTPUT_DIR%\test8-realistic-dat-output.txt" 2>&1

if errorlevel 1 (
  echo [ ERROR ] Test 8 failed: realistic TIFF DAT run with --tiff-pages 1000 must not fail
  exit /b 1
)

findstr /C:"Warning: Bates numbers are 17 characters including the page suffix" "%TEST_OUTPUT_DIR%\test8-realistic-dat-output.txt" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 8: realistic TIFF endorses page labels, so the page suffix width warning should print
  exit /b 1
)

echo [ SUCCESS ] Test Case 8: Page-suffix width warning needs a page label in the output passed

:: --- All Tests Passed ---

echo [ SUCCESS ] All Bates Numbering E2E tests passed!
