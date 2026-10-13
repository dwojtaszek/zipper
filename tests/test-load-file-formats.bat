@echo off

REM Resolve the Zipper binary once. Sets %ZIPPER_CMD%.
call "%~dp0_zipper-cli.bat"
setlocal enabledelayedexpansion

:: Resolve python command
set PYCMD=
python3 --version >nul 2>&1 && set "PYCMD=python3"
if not defined PYCMD python --version >nul 2>&1 && set "PYCMD=python"
if not defined PYCMD (
  echo [ ERROR ] Python 3 is required to run load file format validations
  exit /b 1
)

:: --- Test Configuration ---

set TEST_OUTPUT_DIR=.\results\load-file-formats
set PROJECT=src\Zipper.csproj

:: --- Test Setup ---

echo [ INFO ] Running Load File Formats E2E Test

:: Clean up previous test results
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

:: --- Test Case 1: OPT Format ---

echo [ INFO ] Test Case 1: OPT (comma-delimited, no-header) format

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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test1" ^
  --format opt ^
  --count 10 ^
  --file-type pdf

if errorlevel 1 (
  echo [ ERROR ] Test 1: Oracle validation failed for OPT
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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test2" ^
  --format csv ^
  --count 10 ^
  --file-type pdf

if errorlevel 1 (
  echo [ ERROR ] Test 2: Oracle validation failed for CSV
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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test3" ^
  --format xml ^
  --count 5 ^
  --file-type pdf

if errorlevel 1 (
  echo [ ERROR ] Test 3: Oracle validation failed for XML
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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test4" ^
  --format concordance ^
  --count 5 ^
  --file-type pdf

if errorlevel 1 (
  echo [ ERROR ] Test 4: Oracle validation failed for CONCORDANCE
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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test5" ^
  --format dat ^
  --count 5 ^
  --file-type pdf

if errorlevel 1 (
  echo [ ERROR ] Test 5: Oracle validation failed for DAT
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

  %PYCMD% "%~dp0validate_load_file_format.py" ^
    --dir "%TEST_OUTPUT_DIR%\test6_%%F" ^
    --format "%%F" ^
    --count 3 ^
    --file-type pdf ^
    --bates-prefix "TEST" ^
    --bates-start 1 ^
    --bates-digits 6

  if errorlevel 1 (
    echo [ ERROR ] Test 6: Oracle validation failed for format %%F
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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test7" ^
  --format dat ^
  --count 5 ^
  --file-type pdf ^
  --delimiter-column "|" ^
  --delimiter-quote "^"

if errorlevel 1 (
  echo [ ERROR ] Test 7: Oracle validation failed for custom delimiters
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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test8" ^
  --format dat ^
  --count 5 ^
  --file-type pdf ^
  --delimiter-column "20" ^
  --delimiter-quote "254"

if errorlevel 1 (
  echo [ ERROR ] Test 8: Oracle validation failed for ASCII code delimiters
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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test9" ^
  --format dat ^
  --count 5 ^
  --file-type pdf ^
  --delimiter-column "|" ^
  --delimiter-quote "34"

if errorlevel 1 (
  echo [ ERROR ] Test 9: Oracle validation failed for delimiter override
  exit /b 1
)

echo [ SUCCESS ] Test Case 9: Delimiter override passed

:: --- Test Case 10: Auto OPT generation for tiff and jpg ---

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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test10_tiff" ^
  --format dat ^
  --count 3 ^
  --file-type tiff ^
  --bates-prefix "TIFF" ^
  --bates-start 1 ^
  --bates-digits 5 ^
  --loadfile-only

if errorlevel 1 (
  echo [ ERROR ] Test 10: Oracle validation failed for tiff DAT
  exit /b 1
)

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test10_tiff" ^
  --format opt ^
  --count 3 ^
  --file-type tiff ^
  --bates-prefix "TIFF" ^
  --bates-start 1 ^
  --bates-digits 5 ^
  --loadfile-only

if errorlevel 1 (
  echo [ ERROR ] Test 10: Oracle validation failed for tiff OPT
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

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test10_jpg" ^
  --format dat ^
  --count 3 ^
  --file-type jpg

if errorlevel 1 (
  echo [ ERROR ] Test 10: Oracle validation failed for jpg DAT
  exit /b 1
)

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test10_jpg" ^
  --format opt ^
  --count 3 ^
  --file-type jpg

if errorlevel 1 (
  echo [ ERROR ] Test 10: Oracle validation failed for jpg OPT
  exit /b 1
)

echo [ SUCCESS ] Test Case 10: Auto OPT generation passed

:: --- Test Case 11: Families support across CSV, XML, Concordance, and Loadfile-only ---

echo [ INFO ] Test Case 11: Families support across load file formats

:: Standard mode EML with families for CSV
%ZIPPER_CMD% ^
  --type eml ^
  --count 5 ^
  --attachment-rate 100 ^
  --with-families ^
  --output-path "%TEST_OUTPUT_DIR%\test11_csv" ^
  --load-file-format csv

if errorlevel 1 (
  echo [ ERROR ] Test 11 failed during execution for CSV
  exit /b 1
)

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test11_csv" ^
  --format csv ^
  --count 5 ^
  --file-type eml ^
  --with-families ^
  --attachment-rate 100

if errorlevel 1 (
  echo [ ERROR ] Test 11: Oracle validation failed for CSV families
  exit /b 1
)

:: Standard mode EML with families for Concordance
%ZIPPER_CMD% ^
  --type eml ^
  --count 5 ^
  --attachment-rate 100 ^
  --with-families ^
  --output-path "%TEST_OUTPUT_DIR%\test11_concordance" ^
  --load-file-format concordance

if errorlevel 1 (
  echo [ ERROR ] Test 11 failed during execution for Concordance
  exit /b 1
)

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test11_concordance" ^
  --format concordance ^
  --count 5 ^
  --file-type eml ^
  --with-families ^
  --attachment-rate 100

if errorlevel 1 (
  echo [ ERROR ] Test 11: Oracle validation failed for Concordance families
  exit /b 1
)

:: Standard mode EML with families for XML (EDRM-XML)
%ZIPPER_CMD% ^
  --type eml ^
  --count 5 ^
  --attachment-rate 100 ^
  --with-families ^
  --output-path "%TEST_OUTPUT_DIR%\test11_xml" ^
  --load-file-format xml

if errorlevel 1 (
  echo [ ERROR ] Test 11 failed during execution for XML
  exit /b 1
)

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test11_xml" ^
  --format xml ^
  --count 5 ^
  --file-type eml ^
  --with-families ^
  --attachment-rate 100

if errorlevel 1 (
  echo [ ERROR ] Test 11: Oracle validation failed for XML families
  exit /b 1
)

:: Loadfile-Only mode with families
%ZIPPER_CMD% ^
  --type eml ^
  --count 5 ^
  --attachment-rate 100 ^
  --with-families ^
  --output-path "%TEST_OUTPUT_DIR%\test11_loadfile_only" ^
  --loadfile-only

if errorlevel 1 (
  echo [ ERROR ] Test 11 failed during execution for loadfile-only
  exit /b 1
)

%PYCMD% "%~dp0validate_load_file_format.py" ^
  --dir "%TEST_OUTPUT_DIR%\test11_loadfile_only" ^
  --format dat ^
  --count 5 ^
  --file-type eml ^
  --with-families ^
  --attachment-rate 100 ^
  --loadfile-only

if errorlevel 1 (
  echo [ ERROR ] Test 11: Oracle validation failed for loadfile-only families
  exit /b 1
)

echo [ SUCCESS ] Test Case 11: Families support passed

:: --- Test Case 12: Negative and malformed output validation ---

echo [ INFO ] Test Case 12: Negative and malformed output validation

%PYCMD% -m unittest discover -s "%~dp0." -p "test_validate_load_file_format.py"
if errorlevel 1 (
  echo [ ERROR ] Test 12: Oracle unit test suite failed
  exit /b 1
)

if not exist "%TEST_OUTPUT_DIR%\test12_malformed" mkdir "%TEST_OUTPUT_DIR%\test12_malformed"

:: 1. Header-only CSV fails
> "%TEST_OUTPUT_DIR%\test12_malformed\header_only.csv" echo CONTROL NUMBER,FILE PATH
%PYCMD% "%~dp0validate_load_file_format.py" --load-file "%TEST_OUTPUT_DIR%\test12_malformed\header_only.csv" --format csv --count 10 > "%TEST_OUTPUT_DIR%\test12_malformed\err1.txt" 2>&1
if not errorlevel 1 (
  echo [ ERROR ] Test 12: Header-only CSV should have failed
  exit /b 1
)
findstr /C:"Header-only CSV" "%TEST_OUTPUT_DIR%\test12_malformed\err1.txt" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 12: Expected 'Header-only CSV' diagnostic
  exit /b 1
)

:: 2. Comma-only OPT fails
> "%TEST_OUTPUT_DIR%\test12_malformed\comma_only.opt" echo DOC00000001,VOL001
%PYCMD% "%~dp0validate_load_file_format.py" --load-file "%TEST_OUTPUT_DIR%\test12_malformed\comma_only.opt" --format opt --count 1 > "%TEST_OUTPUT_DIR%\test12_malformed\err2.txt" 2>&1
if not errorlevel 1 (
  echo [ ERROR ] Test 12: Comma-only OPT should have failed
  exit /b 1
)
findstr /C:"OPT row must have exactly 7 columns" "%TEST_OUTPUT_DIR%\test12_malformed\err2.txt" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 12: Expected 'OPT row must have exactly 7 columns' diagnostic
  exit /b 1
)

:: 3. Malformed XML fails
> "%TEST_OUTPUT_DIR%\test12_malformed\malformed.xml" echo ^<Root^>^<Batch^>^<Document DocID="DOC1"^>^</Batch^>^</Root^>
%PYCMD% "%~dp0validate_load_file_format.py" --load-file "%TEST_OUTPUT_DIR%\test12_malformed\malformed.xml" --format xml --count 1 > "%TEST_OUTPUT_DIR%\test12_malformed\err3.txt" 2>&1
if not errorlevel 1 (
  echo [ ERROR ] Test 12: Malformed XML should have failed
  exit /b 1
)
findstr /C:"Malformed XML" "%TEST_OUTPUT_DIR%\test12_malformed\err3.txt" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 12: Expected 'Malformed XML' diagnostic
  exit /b 1
)

:: 4. Bates number mismatch fails
> "%TEST_OUTPUT_DIR%\test12_malformed\bates_mismatch.csv" echo CONTROL NUMBER,FILE PATH,BATES NUMBER
>> "%TEST_OUTPUT_DIR%\test12_malformed\bates_mismatch.csv" echo DOC00000001,folder_001/00000001.pdf,WRONG000001
%PYCMD% "%~dp0validate_load_file_format.py" --load-file "%TEST_OUTPUT_DIR%\test12_malformed\bates_mismatch.csv" --format csv --count 1 --bates-prefix TEST --bates-start 1 --bates-digits 6 > "%TEST_OUTPUT_DIR%\test12_malformed\err4.txt" 2>&1
if not errorlevel 1 (
  echo [ ERROR ] Test 12: Wrong Bates values should have failed
  exit /b 1
)
findstr /C:"Bates number mismatch" "%TEST_OUTPUT_DIR%\test12_malformed\err4.txt" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 12: Expected 'Bates number mismatch' diagnostic
  exit /b 1
)

:: 5. Ignored delimiter override fails
> "%TEST_OUTPUT_DIR%\test12_malformed\ignored_delim.dat" echo "Control Number","File Path"
>> "%TEST_OUTPUT_DIR%\test12_malformed\ignored_delim.dat" echo "DOC00000001","folder_001/00000001.pdf"
%PYCMD% "%~dp0validate_load_file_format.py" --load-file "%TEST_OUTPUT_DIR%\test12_malformed\ignored_delim.dat" --format dat --count 1 --delimiter-column "|" > "%TEST_OUTPUT_DIR%\test12_malformed\err5.txt" 2>&1
if not errorlevel 1 (
  echo [ ERROR ] Test 12: Ignored delimiter override should have failed
  exit /b 1
)
findstr /I /C:"delimiter" "%TEST_OUTPUT_DIR%\test12_malformed\err5.txt" >nul
if errorlevel 1 (
  echo [ ERROR ] Test 12: Expected delimiter diagnostic
  exit /b 1
)

echo [ SUCCESS ] Test Case 12: Negative and malformed output validation passed

:: --- All Tests Passed ---

echo [ SUCCESS ] All Load File Formats E2E tests passed!
