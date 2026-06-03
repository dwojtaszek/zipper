@echo off
setlocal enabledelayedexpansion

REM E2E tests for --column-profile with a custom JSON profile (Windows)
REM exercises every Column Kind and every Distribution Pattern.

set "REPO_ROOT=%~dp0.."
set "ZIPPER_PROJECT=%REPO_ROOT%\src\Zipper.csproj"

pushd "%REPO_ROOT%"
call "%~dp0_zipper-cli.bat"
popd

if "%ZIPPER_CMD%"=="" (
    echo [ ERROR ] Zipper binary not resolved.
    exit /b 1
)

set "PROFILE_FILE=%~dp0fixtures\profiles\test-every-kind.json"
set "TEST_OUTPUT_DIR=.\results\column-profile-custom-kinds"
set "COUNT=2000"
set "SEED=42"

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

echo [ INFO ] Generating load file with custom every-kind profile (count=%COUNT%, seed=%SEED%)...

%ZIPPER_CMD% --count %COUNT% --type pdf --column-profile "%PROFILE_FILE%" --seed %SEED% --loadfile-only --output-path "%TEST_OUTPUT_DIR%\run1"
if errorlevel 1 (
    echo [ ERROR ] Zipper run failed
    exit /b 1
)

REM Find the .dat file
set "DAT_FILE="
for /f "delims=" %%F in ('dir /b /s "%TEST_OUTPUT_DIR%\run1\*.dat" 2^>nul') do set "DAT_FILE=%%F"

if "%DAT_FILE%"=="" (
    echo [ ERROR ] No .dat file produced
    exit /b 1
)

REM Write python validator script to a temp file
set "PY_VAL=%TEMP%\custom_kind_val_%RANDOM%.py"
(
echo import sys, os, re
echo dat_path = sys.argv[1]
echo out_dir  = sys.argv[2]
echo COL_SEP  = '\u0014'
echo QUOTE    = '\u00fe'
echo rows = []
echo with open(dat_path, encoding='utf-8-sig'^) as f:
echo     header_line = f.readline(^).rstrip('\r\n'^)
echo     headers = [c.strip(QUOTE^) for c in header_line.split(COL_SEP^)]
echo     expected_headers = ["DOCID", "TEXTFIELD", "LONGTEXTFIELD", "DATEFIELD", "DATETIMEFIELD", "NUMBERFIELD", "NUMBERGAUSSIAN", "NUMBEREXPONENTIAL", "NUMBERPARETO", "BOOLEANFIELD", "CODEDFIELD", "CODEDWEIGHTED", "EMAILFIELD", "EMAILMULTI"]
echo     if headers != expected_headers:
echo         print("Expected headers:", expected_headers^)
echo         print("Actual headers:", headers^)
echo         sys.exit("Header mismatch against expected columns"^)
echo     for line in f:
echo         line = line.rstrip('\r\n'^)
echo         if not line:
echo             continue
echo         fields = [c.strip(QUOTE^) for c in line.split(COL_SEP^)]
echo         rows.append(dict(zip(headers, fields^)^)^)
echo assert len(rows^) == 2000, f"Expected 2000 rows, got {len(rows)}"
echo errors = []
echo for i, row in enumerate(rows^):
echo     if len(row^) != 14:
echo         errors.append(f"Row {i+1}: expected 14 cols, got {len(row)}"^)
echo if errors:
echo     sys.exit(f"Column count errors: {len(errors)}"^)
echo docids = [r['DOCID'] for r in rows]
echo if len(set(docids^)^) != len(docids^):
echo     sys.exit("DOCID has duplicates"^)
echo bad_docid = [d for d in docids if not re.match(r'^DOC[0-9]+$', d^)]
echo if bad_docid:
echo     sys.exit(f"DOCID invalid: {bad_docid[:3]}"^)
echo empty_text = sum(1 for r in rows if not r.get('TEXTFIELD'^)^)
echo if empty_text:
echo     sys.exit("TEXTFIELD has empty values"^)
echo short = [r['LONGTEXTFIELD'] for r in rows if len(r.get('LONGTEXTFIELD', ''^)^) ^< 10]
echo if short:
echo     sys.exit("LONGTEXTFIELD has values shorter than 10 chars"^)
echo import datetime
echo for r in rows:
echo     v = r.get('DATEFIELD', ''^)
echo     if v:
echo         d = datetime.datetime.strptime(v, '%%Y-%%m-%%d'^)
echo         if not (2018 ^<= d.year ^<= 2025^):
echo             sys.exit(f"DATEFIELD year out of range: {v}"^)
echo bad_dt = [r['DATETIMEFIELD'] for r in rows if r.get('DATETIMEFIELD'^) and 'T' not in r['DATETIMEFIELD']]
echo if bad_dt:
echo     sys.exit("DATETIMEFIELD missing T"^)
echo for r in rows:
echo     v = r.get('NUMBERFIELD', ''^)
echo     if v:
echo         n = int(v^)
echo         if not (0 ^<= n ^<= 10000^):
echo             sys.exit(f"NUMBERFIELD out of range: {v}"^)
echo valid_bool = {'Y', 'N'}
echo bad_bool = [r['BOOLEANFIELD'] for r in rows if r.get('BOOLEANFIELD'^) and r['BOOLEANFIELD'] not in valid_bool]
echo if bad_bool:
echo             sys.exit(f"BOOLEANFIELD invalid: {bad_bool}"^)
echo valid_coded = {'Active', 'Inactive', 'Pending', 'Closed', 'Archived'}
echo bad_coded = [r['CODEDFIELD'] for r in rows if r.get('CODEDFIELD'^) and r['CODEDFIELD'] not in valid_coded]
echo if bad_coded:
echo     sys.exit(f"CODEDFIELD invalid: {bad_coded}"^)
echo email_re = re.compile(r'^[A-Za-z0-9._-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$'^)
echo bad_email = [r['EMAILFIELD'] for r in rows if r.get('EMAILFIELD'^) and not email_re.match(r['EMAILFIELD'])]
echo if bad_email:
echo     sys.exit(f"EMAILFIELD invalid: {bad_email}"^)
echo multi_rows = sum(1 for r in rows if ';' in r.get('EMAILMULTI', ''^)^)
echo if multi_rows == 0:
echo     sys.exit("EMAILMULTI has no multi-value rows"^)
echo print("All checks passed!"^)
) > "%PY_VAL%"

python "%PY_VAL%" "%DAT_FILE%" "%TEST_OUTPUT_DIR%"
set PY_ERR=%ERRORLEVEL%
del "%PY_VAL%"

if not %PY_ERR%==0 (
    echo [ ERROR ] Custom column invariants failed
    exit /b 1
)

echo [ INFO ] Verifying determinism (re-run with seed=%SEED%)...
%ZIPPER_CMD% --count %COUNT% --type pdf --column-profile "%PROFILE_FILE%" --seed %SEED% --loadfile-only --output-path "%TEST_OUTPUT_DIR%\run2"
if errorlevel 1 (
    echo [ ERROR ] Zipper run2 failed
    exit /b 1
)

set "DAT_FILE2="
for /f "delims=" %%F in ('dir /b /s "%TEST_OUTPUT_DIR%\run2\*.dat" 2^>nul') do set "DAT_FILE2=%%F"

fc /b "%DAT_FILE%" "%DAT_FILE2%" >nul
if errorlevel 1 (
    echo [ ERROR ] Determinism check failed: two runs with seed=%SEED% differ
    exit /b 1
)

echo [ SUCCESS ] Custom every-kind profile tests PASSED.
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
exit /b 0
