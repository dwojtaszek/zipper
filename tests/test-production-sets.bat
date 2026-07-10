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

:: --- Test Case 4: Rolling Production Sets - Continuous Mode ---

echo [ INFO ] Test Case 4: Rolling production sets continuous mode

%ZIPPER_CMD% ^
  --production-set ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test4" ^
  --bates-prefix "CONT" ^
  --bates-start 10 ^
  --production-id "CONT_001" ^
  --rolling-count 3 ^
  --rolling-bates-mode continuous

if errorlevel 1 (
  echo [ ERROR ] Test 4 failed during execution
  exit /b 1
)

if not exist "%TEST_OUTPUT_DIR%\test4\CONT_001\" ( echo [ ERROR ] Missing CONT_001 folder & exit /b 1 )
if not exist "%TEST_OUTPUT_DIR%\test4\CONT_002\" ( echo [ ERROR ] Missing CONT_002 folder & exit /b 1 )
if not exist "%TEST_OUTPUT_DIR%\test4\CONT_003\" ( echo [ ERROR ] Missing CONT_003 folder & exit /b 1 )

powershell -NoProfile -Command ^
  "$root = '%TEST_OUTPUT_DIR%\test4';" ^
  "$m1 = Get-Content (Join-Path $root 'CONT_001\_manifest.json') -Raw | ConvertFrom-Json;" ^
  "$m2 = Get-Content (Join-Path $root 'CONT_002\_manifest.json') -Raw | ConvertFrom-Json;" ^
  "$m3 = Get-Content (Join-Path $root 'CONT_003\_manifest.json') -Raw | ConvertFrom-Json;" ^
  "if ($m1.productionId -ne 'CONT_001') { throw 'm1 prod ID invalid' }" ^
  "if ($m2.productionId -ne 'CONT_002') { throw 'm2 prod ID invalid' }" ^
  "if ($m3.productionId -ne 'CONT_003') { throw 'm3 prod ID invalid' }" ^
  "if ($m1.rollingSequenceNumber -ne 1) { throw 'm1 rolling seq invalid' }" ^
  "if ($m2.rollingSequenceNumber -ne 2) { throw 'm2 rolling seq invalid' }" ^
  "if ($m3.rollingSequenceNumber -ne 3) { throw 'm3 rolling seq invalid' }" ^
  "if ($m1.batesNumberStart -ne 'CONT00000010') { throw 'm1 start invalid' }" ^
  "if ($m1.batesNumberEnd -ne 'CONT00000014') { throw 'm1 end invalid' }" ^
  "if ($m2.batesNumberStart -ne 'CONT00000015') { throw 'm2 start invalid' }" ^
  "if ($m2.batesNumberEnd -ne 'CONT00000019') { throw 'm2 end invalid' }" ^
  "if ($m3.batesNumberStart -ne 'CONT00000020') { throw 'm3 start invalid' }" ^
  "if ($m3.batesNumberEnd -ne 'CONT00000024') { throw 'm3 end invalid' }" ^
  "if ($m1.batesRangeMode -ne 'continuous') { throw 'm1 mode invalid' }"
if errorlevel 1 (
  echo [ ERROR ] Test 4 validation failed
  exit /b 1
)

echo [ SUCCESS ] Test Case 4: Rolling production sets continuous mode passed


:: --- Test Case 5: Rolling Production Sets - Restart Mode ---

echo [ INFO ] Test Case 5: Rolling production sets restart mode

%ZIPPER_CMD% ^
  --production-set ^
  --count 5 ^
  --output-path "%TEST_OUTPUT_DIR%\test5" ^
  --bates-prefix "REST" ^
  --bates-start 100 ^
  --production-id "REST_A" ^
  --rolling-count 2 ^
  --rolling-bates-mode restart

if errorlevel 1 (
  echo [ ERROR ] Test 5 failed during execution
  exit /b 1
)

if not exist "%TEST_OUTPUT_DIR%\test5\REST_A\" ( echo [ ERROR ] Missing REST_A folder & exit /b 1 )
if not exist "%TEST_OUTPUT_DIR%\test5\REST_A_2\" ( echo [ ERROR ] Missing REST_A_2 folder & exit /b 1 )

powershell -NoProfile -Command ^
  "$root = '%TEST_OUTPUT_DIR%\test5';" ^
  "$m1 = Get-Content (Join-Path $root 'REST_A\_manifest.json') -Raw | ConvertFrom-Json;" ^
  "$m2 = Get-Content (Join-Path $root 'REST_A_2\_manifest.json') -Raw | ConvertFrom-Json;" ^
  "if ($m1.batesNumberStart -ne 'REST00000100' -or $m1.batesNumberEnd -ne 'REST00000104') { throw 'm1 range invalid' }" ^
  "if ($m2.batesNumberStart -ne 'REST00000100' -or $m2.batesNumberEnd -ne 'REST00000104') { throw 'm2 range invalid' }" ^
  "if ($m1.batesRangeMode -ne 'restart') { throw 'm1 mode invalid' }"
if errorlevel 1 (
  echo [ ERROR ] Test 5 validation failed
  exit /b 1
)

echo [ SUCCESS ] Test Case 5: Rolling production sets restart mode passed


:: --- Test Case 6: Rolling Production Sets - Zip Packaging ---

echo [ INFO ] Test Case 6: Rolling production sets with zip packaging

%ZIPPER_CMD% ^
  --production-set ^
  --production-zip ^
  --count 3 ^
  --output-path "%TEST_OUTPUT_DIR%\test6" ^
  --bates-prefix "ROLLZIP" ^
  --production-id "ROLLZIP_01" ^
  --rolling-count 2

if errorlevel 1 (
  echo [ ERROR ] Test 6 failed during execution
  exit /b 1
)

if not exist "%TEST_OUTPUT_DIR%\test6\ROLLZIP_01.zip" ( echo [ ERROR ] Missing ROLLZIP_01.zip & exit /b 1 )
if not exist "%TEST_OUTPUT_DIR%\test6\ROLLZIP_02.zip" ( echo [ ERROR ] Missing ROLLZIP_02.zip & exit /b 1 )

echo [ SUCCESS ] Test Case 6: Rolling production sets zip packaging passed

:: --- All Tests Passed ---

echo [ SUCCESS ] All Production Sets E2E tests passed!
