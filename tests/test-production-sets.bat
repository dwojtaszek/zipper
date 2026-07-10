@echo off

REM Resolve the Zipper binary once. Sets %ZIPPER_CMD%.
call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\production-sets
set PROJECT=src\Zipper.csproj

:: --- Test Setup ---

echo [ INFO ] Running Production Sets E2E Test

:: Clean up previous test results
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: Basic Production Set ---

echo [ INFO ] Test Case 1: Basic production set generation

%ZIPPER_CMD% ^
  --production-set ^
  --count 10 ^
  --output-path "%TEST_OUTPUT_DIR%\test1" ^
  --bates-prefix "PROD" ^
  --volume-size 3

if errorlevel 1 (
  echo [ ERROR ] Test 1 failed during execution
  exit /b 1
)

:: Find production dir
for /d %%d in ("%TEST_OUTPUT_DIR%\test1\PRODUCTION_*") do set PROD_DIR=%%d
if not defined PROD_DIR (
  echo [ ERROR ] Test 1: No production directory found.
  exit /b 1
)

:: Verify structure
if not exist "%PROD_DIR%\DATA\" ( echo [ ERROR ] Missing DATA dir & exit /b 1 )
if not exist "%PROD_DIR%\NATIVES\" ( echo [ ERROR ] Missing NATIVES dir & exit /b 1 )
if not exist "%PROD_DIR%\IMAGES\" ( echo [ ERROR ] Missing IMAGES dir & exit /b 1 )
if not exist "%PROD_DIR%\TEXT\" ( echo [ ERROR ] Missing TEXT dir & exit /b 1 )

:: Verify load files exist
if not exist "%PROD_DIR%\DATA\loadfile.dat" ( echo [ ERROR ] Missing DAT load file & exit /b 1 )
if not exist "%PROD_DIR%\DATA\loadfile.opt" ( echo [ ERROR ] Missing OPT load file & exit /b 1 )
if not exist "%PROD_DIR%\_manifest.json" ( echo [ ERROR ] Missing manifest JSON & exit /b 1 )

:: Verify volumes (10 docs / 3 = 4 volumes)
if not exist "%PROD_DIR%\NATIVES\VOL001\" ( echo [ ERROR ] Missing VOL001 & exit /b 1 )
if not exist "%PROD_DIR%\NATIVES\VOL004\" ( echo [ ERROR ] Missing VOL004 & exit /b 1 )

:: Verify DAT contents
findstr /C:"PROD00000001" "%PROD_DIR%\DATA\loadfile.dat" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 1: Bates start not found in DAT
  exit /b 1
)

echo [ SUCCESS ] Test Case 1: Basic production set passed

:: --- Test Case 2: Production ZIP ---

echo [ INFO ] Test Case 2: Production set with --production-zip

%ZIPPER_CMD% ^
  --production-set ^
  --production-zip ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test2" ^
  --bates-prefix "ZIP"

if errorlevel 1 (
  echo [ ERROR ] Test 2 failed during execution
  exit /b 1
)

:: Verify output
dir /b /s "%TEST_OUTPUT_DIR%\test2\*.zip" >nul 2>&1
if errorlevel 1 (
  echo [ ERROR ] Test 2: No ZIP archive generated.
  exit /b 1
)

:: Make sure manifest exists and matches
set PROD_DIR2=
for /d %%d in ("%TEST_OUTPUT_DIR%\test2\PRODUCTION_*") do set PROD_DIR2=%%d
if not exist "%PROD_DIR2%\_manifest.json" ( echo [ ERROR ] Missing manifest JSON & exit /b 1 )

echo [ SUCCESS ] Test Case 2: Production ZIP passed

:: --- Test Case 3: Production Set Line Ending and manifest counts ---

echo [ INFO ] Test Case 3: Production set LF line endings and Attachment counts

%ZIPPER_CMD% ^
  --production-set ^
  --type eml ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test3" ^
  --bates-prefix "FAM" ^
  --attachment-rate 100 ^
  --with-families ^
  --seed 42 ^
  --eol LF

if errorlevel 1 (
  echo [ ERROR ] Test 3 failed during execution
  exit /b 1
)

set PROD_DIR3=
for /d %%d in ("%TEST_OUTPUT_DIR%\test3\PRODUCTION_*") do set PROD_DIR3=%%d
if not defined PROD_DIR3 (
  echo [ ERROR ] Test 3: No production directory found.
  exit /b 1
)

powershell -NoProfile -Command ^
  "$root = '%PROD_DIR3%';" ^
  "foreach ($rel in @('DATA/loadfile.dat','DATA/loadfile.opt')) { $bytes = [IO.File]::ReadAllBytes((Join-Path $root $rel)); if ($bytes -contains 13) { throw \"$rel contains CR bytes despite --eol LF\" } }" ^
  "foreach ($rel in @('DATA/loadfile_properties.json','DATA/loadfile.opt_properties.json')) { $doc = Get-Content (Join-Path $root $rel) -Raw | ConvertFrom-Json; if ($doc.properties.lineEnding -ne 'LF') { throw \"$rel did not report LF line ending\" } }" ^
  "$manifest = Get-Content (Join-Path $root '_manifest.json') -Raw | ConvertFrom-Json;" ^
  "$actual = (Get-ChildItem -Path (Join-Path $root 'NATIVES') -File -Recurse).Count;" ^
  "if ($manifest.nativeFileCount -ne $actual) { throw \"nativeFileCount mismatch\" }" ^
  "if ($manifest.parentNativeFileCount -ne 5) { throw \"parentNativeFileCount should be 5\" }" ^
  "if ($manifest.attachmentNativeFileCount -le 0) { throw \"attachmentNativeFileCount should be positive\" }" ^
  "if ($manifest.nativeFileCount -ne ($manifest.parentNativeFileCount + $manifest.attachmentNativeFileCount)) { throw \"manifest Native File counts do not add up\" }"
if errorlevel 1 (
  echo [ ERROR ] Test 3 validation failed
  exit /b 1
)

echo [ SUCCESS ] Test Case 3: Production Set LF line endings and Attachment counts passed

:: --- Test Case 4: Successful supplemental Production Set after a prior manifest ---

echo [ INFO ] Test Case 4: Successful supplemental Production Set after a prior manifest

:: 1. Generate prior
%ZIPPER_CMD% ^
  --production-set ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test4_prior" ^
  --bates-prefix "SUPP" ^
  --bates-start 1

if errorlevel 1 (
  echo [ ERROR ] Test 4 prior generation failed
  exit /b 1
)

set PRIOR_DIR=
for /d %%d in ("%TEST_OUTPUT_DIR%\test4_prior\PRODUCTION_*") do set PRIOR_DIR=%%d
if not defined PRIOR_DIR (
  echo [ ERROR ] Test 4: Prior production directory not found.
  exit /b 1
)
set PRIOR_MANIFEST=%PRIOR_DIR%\_manifest.json

:: 2. Generate supplemental
%ZIPPER_CMD% ^
  --production-set ^
  --supplemental-production ^
  --prior-manifest "%PRIOR_MANIFEST%" ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test4_supp" ^
  --bates-prefix "SUPP" ^
  --bates-start 6

if errorlevel 1 (
  echo [ ERROR ] Test 4 supplemental generation failed
  exit /b 1
)

set SUPP_DIR=
for /d %%d in ("%TEST_OUTPUT_DIR%\test4_supp\PRODUCTION_*") do set SUPP_DIR=%%d
if not defined SUPP_DIR (
  echo [ ERROR ] Test 4: Supplemental production directory not found.
  exit /b 1
)
set SUPP_MANIFEST=%SUPP_DIR%\_manifest.json

powershell -NoProfile -Command ^
  "$supp = Get-Content '%SUPP_MANIFEST%' -Raw | ConvertFrom-Json;" ^
  "if (-not $supp.priorManifests) { throw 'priorManifests missing' };" ^
  "if ($supp.priorManifests -notcontains '%PRIOR_MANIFEST%') { throw 'prior path not found in priorManifests' };" ^
  "if (-not $supp.supplementalValidation) { throw 'supplementalValidation missing' };" ^
  "if ($supp.supplementalValidation.expectedNextBates -ne 'SUPP00000006') { throw 'expectedNextBates should be SUPP00000006' };" ^
  "if ($supp.supplementalValidation.actualStartingBates -ne 'SUPP00000006') { throw 'actualStartingBates should be SUPP00000006' };"
if errorlevel 1 (
  echo [ ERROR ] Test 4 validation failed
  exit /b 1
)

echo [ SUCCESS ] Test Case 4: Successful supplemental Production Set passed

:: --- Test Case 5: Failing duplicate supplemental Bates range ---

echo [ INFO ] Test Case 5: Failing duplicate supplemental Bates range

%ZIPPER_CMD% ^
  --production-set ^
  --supplemental-production ^
  --prior-manifest "%PRIOR_MANIFEST%" ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test5_supp" ^
  --bates-prefix "SUPP" ^
  --bates-start 4 >nul 2>&1

if not errorlevel 1 (
  echo [ ERROR ] Test 5: Supplemental generation succeeded but should have failed due to duplicate Bates numbers.
  exit /b 1
)

echo [ SUCCESS ] Test Case 5: Failing duplicate supplemental Bates range passed


:: --- All Tests Passed ---

echo [ SUCCESS ] All Production Sets E2E tests passed!
