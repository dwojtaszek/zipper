@echo off
setlocal enabledelayedexpansion

REM Basic E2E smoke suite — fast subset of tests for pre-push validation.
REM Builds the binary ONCE and reuses it, eliminating per-test dotnet-run overhead.
REM
REM Covers: PDF, EML, TIFF, Bates numbering, load-file-in-zip
REM Full suite: run-tests.bat (17 cases + 8 standalone suites)

REM --- Configuration ---

set TEST_OUTPUT_DIR=.\results\e2e-basic
set PROJECT=src\Zipper.csproj
set BUILD_DIR=src\bin\Release\net8.0

REM --- Build Once ---

echo [ INFO ] Building project (one-time)...
dotnet build %PROJECT% -c Release --nologo -v quiet 2>nul
if errorlevel 1 (
    echo [ ERROR ] Build failed. Run 'dotnet build %PROJECT% -c Release' for details.
    exit /b 1
)

REM Resolve binary path
if exist "%BUILD_DIR%\Zipper.exe" (
    set BINARY=%BUILD_DIR%\Zipper.exe
) else (
    REM Fallback: use dotnet run (slower)
    set BINARY=dotnet run --project %PROJECT% --no-build -c Release --
)
echo [ INFO ] Using binary: %BINARY%

REM --- Setup ---

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

set TESTS_PASSED=0
set TESTS_TOTAL=5

REM ─────────────────────────────────────────────
REM Test 1: Basic PDF generation (core happy path)
REM ─────────────────────────────────────────────
echo [ INFO ] Test 1: Basic PDF generation
%BINARY% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_basic"
if errorlevel 1 (
    echo [ ERROR ] Test 1 failed: PDF generation
    goto :cleanup
)

REM Verify zip and dat exist
set "zip_file="
set "dat_file="
for %%F in ("%TEST_OUTPUT_DIR%\pdf_basic\*.zip") do set "zip_file=%%F"
for %%F in ("%TEST_OUTPUT_DIR%\pdf_basic\*.dat") do set "dat_file=%%F"
if not defined zip_file (
    echo [ ERROR ] Test 1: No .zip file found
    goto :cleanup
)
if not defined dat_file (
    echo [ ERROR ] Test 1: No .dat file found
    goto :cleanup
)

REM Verify line count
powershell -Command "(Get-Content -Path '%dat_file%').Count" > "%temp%\line_count.txt"
set /p line_count=<"%temp%\line_count.txt"
if "!line_count!" neq "11" (
    echo [ ERROR ] Test 1: Expected 11 lines, got !line_count!
    goto :cleanup
)

REM Verify header
powershell -Command "(Get-Content -Path '%dat_file%' -TotalCount 1)" > "%temp%\header.txt"
set /p header=<"%temp%\header.txt"
echo "%header%" | findstr /c:"Control Number" >nul
if errorlevel 1 (
    echo [ ERROR ] Test 1: Header missing 'Control Number'
    goto :cleanup
)

REM Verify PDF count in zip
powershell -Command "Add-Type -Assembly System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::OpenRead('%zip_file%').Entries.Where({$_.Name -like '*.pdf'}).Count" > "%temp%\zip_count.txt"
set /p zip_count=<"%temp%\zip_count.txt"
if "!zip_count!" neq "10" (
    echo [ ERROR ] Test 1: Expected 10 PDFs in zip, got !zip_count!
    goto :cleanup
)

echo [ SUCCESS ] Test 1: Basic PDF — PASSED
set /a TESTS_PASSED+=1

REM ─────────────────────────────────────────────
REM Test 2: EML with attachments
REM ─────────────────────────────────────────────
echo [ INFO ] Test 2: EML with attachments
%BINARY% --type eml --count 10 --output-path "%TEST_OUTPUT_DIR%\eml_attach" --attachment-rate 50
if errorlevel 1 (
    echo [ ERROR ] Test 2 failed: EML generation
    goto :cleanup
)

set "zip_file="
set "dat_file="
for %%F in ("%TEST_OUTPUT_DIR%\eml_attach\*.zip") do set "zip_file=%%F"
for %%F in ("%TEST_OUTPUT_DIR%\eml_attach\*.dat") do set "dat_file=%%F"
if not defined zip_file (
    echo [ ERROR ] Test 2: No .zip file found
    goto :cleanup
)
if not defined dat_file (
    echo [ ERROR ] Test 2: No .dat file found
    goto :cleanup
)

REM Verify line count
powershell -Command "(Get-Content -Path '%dat_file%').Count" > "%temp%\line_count.txt"
set /p line_count=<"%temp%\line_count.txt"
if "!line_count!" neq "11" (
    echo [ ERROR ] Test 2: Expected 11 lines, got !line_count!
    goto :cleanup
)

REM Verify header
powershell -Command "(Get-Content -Path '%dat_file%' -TotalCount 1)" > "%temp%\header.txt"
set /p header=<"%temp%\header.txt"
echo "%header%" | findstr /c:"To" >nul
if errorlevel 1 (
    echo [ ERROR ] Test 2: Header missing 'To'
    goto :cleanup
)

REM Verify EML count in zip
powershell -Command "Add-Type -Assembly System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::OpenRead('%zip_file%').Entries.Where({$_.Name -like '*.eml'}).Count" > "%temp%\zip_count.txt"
set /p zip_count=<"%temp%\zip_count.txt"
if "!zip_count!" neq "10" (
    echo [ ERROR ] Test 2: Expected 10 EMLs in zip, got !zip_count!
    goto :cleanup
)

echo [ SUCCESS ] Test 2: EML with attachments — PASSED
set /a TESTS_PASSED+=1

REM ─────────────────────────────────────────────
REM Test 3: TIFF with folders
REM ─────────────────────────────────────────────
echo [ INFO ] Test 3: TIFF with folders
%BINARY% --type tiff --count 10 --output-path "%TEST_OUTPUT_DIR%\tiff_folders" --folders 3
if errorlevel 1 (
    echo [ ERROR ] Test 3 failed: TIFF generation
    goto :cleanup
)

set "zip_file="
set "dat_file="
for %%F in ("%TEST_OUTPUT_DIR%\tiff_folders\*.zip") do set "zip_file=%%F"
for %%F in ("%TEST_OUTPUT_DIR%\tiff_folders\*.dat") do set "dat_file=%%F"
if not defined zip_file (
    echo [ ERROR ] Test 3: No .zip file found
    goto :cleanup
)
if not defined dat_file (
    echo [ ERROR ] Test 3: No .dat file found
    goto :cleanup
)

REM Verify line count
powershell -Command "(Get-Content -Path '%dat_file%').Count" > "%temp%\line_count.txt"
set /p line_count=<"%temp%\line_count.txt"
if "!line_count!" neq "11" (
    echo [ ERROR ] Test 3: Expected 11 lines, got !line_count!
    goto :cleanup
)

REM Verify header
powershell -Command "(Get-Content -Path '%dat_file%' -TotalCount 1)" > "%temp%\header.txt"
set /p header=<"%temp%\header.txt"
echo "%header%" | findstr /c:"Control Number" >nul
if errorlevel 1 (
    echo [ ERROR ] Test 3: Header missing 'Control Number'
    goto :cleanup
)

powershell -Command "Add-Type -Assembly System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::OpenRead('%zip_file%').Entries.Where({$_.Name -like '*.tiff'}).Count" > "%temp%\zip_count.txt"
set /p zip_count=<"%temp%\zip_count.txt"
if "!zip_count!" neq "10" (
    echo [ ERROR ] Test 3: Expected 10 TIFFs in zip, got !zip_count!
    goto :cleanup
)

echo [ SUCCESS ] Test 3: TIFF with folders — PASSED
set /a TESTS_PASSED+=1

REM ─────────────────────────────────────────────
REM Test 4: Load file included in zip
REM ─────────────────────────────────────────────
echo [ INFO ] Test 4: Include load file in zip
%BINARY% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_include_load" --include-load-file
if errorlevel 1 (
    echo [ ERROR ] Test 4 failed: Include load file
    goto :cleanup
)

REM Verify NO separate .dat exists
set "dat_file="
for %%F in ("%TEST_OUTPUT_DIR%\pdf_include_load\*.dat") do set "dat_file=%%F"
if defined dat_file (
    echo [ ERROR ] Test 4: Found separate .dat file — should be inside zip
    goto :cleanup
)

REM Verify .dat inside zip
set "zip_file="
for %%F in ("%TEST_OUTPUT_DIR%\pdf_include_load\*.zip") do set "zip_file=%%F"
if not defined zip_file (
    echo [ ERROR ] Test 4: No .zip file found
    goto :cleanup
)
powershell -Command "Add-Type -Assembly System.IO.Compression.FileSystem; [System.IO.Compression.ZipFile]::OpenRead('%zip_file%').Entries.Where({$_.Name -like '*.dat'}).Count" > "%temp%\zip_count.txt"
set /p zip_count=<"%temp%\zip_count.txt"
if "!zip_count!" neq "1" (
    echo [ ERROR ] Test 4: Expected 1 .dat in zip, got !zip_count!
    goto :cleanup
)

echo [ SUCCESS ] Test 4: Load file in zip — PASSED
set /a TESTS_PASSED+=1

REM ─────────────────────────────────────────────
REM Test 5: Bates numbering
REM ─────────────────────────────────────────────
echo [ INFO ] Test 5: Bates numbering
%BINARY% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_bates" --bates-prefix SMOKE --bates-start 1 --bates-digits 8
if errorlevel 1 (
    echo [ ERROR ] Test 5 failed: Bates numbering
    goto :cleanup
)

set "dat_file="
for %%F in ("%TEST_OUTPUT_DIR%\pdf_bates\*.dat") do set "dat_file=%%F"
if not defined dat_file (
    echo [ ERROR ] Test 5: No .dat file found
    goto :cleanup
)

findstr /c:"SMOKE00000001" "%dat_file%" >nul
if errorlevel 1 (
    echo [ ERROR ] Test 5: Bates number SMOKE00000001 not found
    goto :cleanup
)
findstr /c:"SMOKE00000010" "%dat_file%" >nul
if errorlevel 1 (
    echo [ ERROR ] Test 5: Bates number SMOKE00000010 not found
    goto :cleanup
)

echo [ SUCCESS ] Test 5: Bates numbering — PASSED
set /a TESTS_PASSED+=1

REM ─────────────────────────────────────────────

:cleanup
echo [ INFO ] Cleaning up...
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"

if %TESTS_PASSED% equ %TESTS_TOTAL% (
    echo [ SUCCESS ] All basic E2E smoke tests passed! (%TESTS_PASSED%/%TESTS_TOTAL%)
    exit /b 0
) else (
    echo [ ERROR ] E2E smoke tests failed: %TESTS_PASSED%/%TESTS_TOTAL% passed
    exit /b 1
)
