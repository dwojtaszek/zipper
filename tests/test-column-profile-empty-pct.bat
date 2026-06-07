@echo off
setlocal enabledelayedexpansion

REM E2E tests for EmptyPercentage behaviour via chi-square goodness-of-fit test. (Windows)

set "REPO_ROOT=%~dp0.."
set "ZIPPER_PROJECT=%REPO_ROOT%\src\Zipper.csproj"

pushd "%REPO_ROOT%"
call "%~dp0_zipper-cli.bat"
popd

if "%ZIPPER_CMD%"=="" (
    echo [ ERROR ] Zipper binary not resolved.
    exit /b 1
)

set "PROFILE_FILE=%~dp0fixtures\profiles\test-empty-pct-20.json"
set "TEST_OUTPUT_DIR=.\results\column-profile-empty-pct"
set "COUNT=10000"
set "SIGNIFICANCE=0.01"

if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

REM Create Python Helper for Chi-Square and count
set "PY_HELPER=%TEMP%\chi_sq_helper_%RANDOM%.py"
(
echo import sys, os, math
echo mode = sys.argv[1]
echo if mode == "count":
echo     dat_path = sys.argv[2]
echo     COL_SEP = '\u0014'
echo     QUOTE   = '\u00fe'
echo     empty = 0
echo     with open(dat_path, encoding='utf-8-sig'^) as f:
echo         next(f^)
echo         for line in f:
echo             line = line.rstrip('\r\n'^)
echo             if not line: continue
echo             fields = [c.strip(QUOTE^) for c in line.split(COL_SEP^)]
echo             if len(fields^) ^> 1 and fields[1] == '':
echo                 empty += 1
echo     print(empty^)
echo     sys.exit(0^)
echo elif mode == "chi_square":
echo     obs = float(sys.argv[2]^)
echo     total = float(sys.argv[3]^)
echo     rate = float(sys.argv[4]^)
echo     sig = float(sys.argv[5]^)
echo     e1 = total * rate
echo     e2 = total * (1.0 - rate^)
echo     obs2 = total - obs
echo     chi2 = 0.0
echo     if e1 ^> 0: chi2 += (obs - e1^)**2 / e1
echo     if e2 ^> 0: chi2 += (obs2 - e2^)**2 / e2
echo     if chi2 ^<= 0:
echo         p = 1.0
echo     else:
echo         k = 1
echo         z = ((chi2/k^)**(1.0/3.0^) - (1.0 - 2.0/(9.0*k^)^)^) / math.sqrt(2.0/(9.0*k^)^)
echo         if z ^>= 0:
echo             t = 1.0 / (1.0 + 0.2316419 * z^)
echo             poly = t*(0.319381530 + t*(-0.356563782 + t*(1.781477937 + t*(-1.821255978 + t*1.330274429^)^)^)^)
echo             phi_z = 1.0 - (1.0/math.sqrt(2.0*math.pi^)^) * math.exp(-z*z/2.0^) * poly
echo             p = 1.0 - phi_z
echo         else:
echo             p = 1.0
echo     print(f"chi2={chi2:.4f} p={p:.6f} obs={int(obs^)} exp={e1:.1f}"^)
echo     sys.exit(0 if p ^>= sig else 1^)
) > "%PY_HELPER%"

echo [ INFO ] === EmptyPercentage=20 chi-square tests ===

for %%S in (1 42 99 1337) do (
    set "OUT_DIR=%TEST_OUTPUT_DIR%\pct20_seed%%S"
    mkdir "!OUT_DIR!"
    echo [ INFO ] Running: profile=test-empty-pct-20 seed=%%S count=%COUNT%

    %ZIPPER_CMD% --count %COUNT% --type pdf --column-profile "%PROFILE_FILE%" --seed %%S --loadfile-only --output-path "!OUT_DIR!"
    if errorlevel 1 (
        echo [ ERROR ] Zipper run failed for seed %%S
        del "%PY_HELPER%"
        exit /b 1
    )

    set "DAT_FILE="
    for /f "delims=" %%F in ('dir /b /s "!OUT_DIR!\*.dat" 2^>nul') do set "DAT_FILE=%%F"
    if "!DAT_FILE!"=="" (
        echo [ ERROR ] No .dat file produced for seed %%S
        del "%PY_HELPER%"
        exit /b 1
    )

    for /f %%E in ('python "%PY_HELPER%" count "!DAT_FILE!"') do set "EMPTIES=%%E"
    echo [ INFO ] seed=%%S: observed !EMPTIES! empty out of %COUNT% ^(expected rate=0.20^)

    python "%PY_HELPER%" chi_square !EMPTIES! %COUNT% 0.20 %SIGNIFICANCE%
    if errorlevel 1 (
        echo [ ERROR ] Chi-square FAILED for EmptyPercentage=20, seed=%%S
        del "%PY_HELPER%"
        exit /b 1
    )
    echo [ SUCCESS ] seed=%%S: chi-square PASSED
)

echo [ INFO ] === Edge case: EmptyPercentage=0 ===
set "TMP_PROFILE_0=%TEST_OUTPUT_DIR%\test-empty-pct-0.json"
(
echo {
echo   "name": "test-empty-pct-0",
echo   "description": "Empty percentage edge case: 0%%",
echo   "version": "1.0",
echo   "fieldNamingConvention": "UPPERCASE",
echo   "settings": { "emptyValuePercentage": 0, "dateFormat": "yyyy-MM-dd" },
echo   "dataSources": {},
echo   "columns": [
echo     { "name": "DOCID", "type": "identifier", "required": true },
echo     { "name": "TEXTFIELD", "type": "text", "emptyPercentage": 0 }
echo   ]
echo }
) > "%TMP_PROFILE_0%"

%ZIPPER_CMD% --count %COUNT% --type pdf --column-profile "%TMP_PROFILE_0%" --seed 9999 --loadfile-only --output-path "%TEST_OUTPUT_DIR%\pct0"
if errorlevel 1 (
    echo [ ERROR ] Zipper pct0 run failed
    del "%PY_HELPER%"
    exit /b 1
)

set "DAT_0="
for /f "delims=" %%F in ('dir /b /s "%TEST_OUTPUT_DIR%\pct0\*.dat" 2^>nul') do set "DAT_0=%%F"
for /f %%E in ('python "%PY_HELPER%" count "%DAT_0%"') do set "EMPTIES_0=%%E"
if not "!EMPTIES_0!"=="0" (
    echo [ ERROR ] EmptyPercentage=0: expected 0 empties, got !EMPTIES_0!
    del "%PY_HELPER%"
    exit /b 1
)
echo [ SUCCESS ] EmptyPercentage=0: exactly 0 empty values

echo [ INFO ] === Edge case: EmptyPercentage=100 ===
set "TMP_PROFILE_100=%TEST_OUTPUT_DIR%\test-empty-pct-100.json"
(
echo {
echo   "name": "test-empty-pct-100",
echo   "description": "Empty percentage edge case: 100%%",
echo   "version": "1.0",
echo   "fieldNamingConvention": "UPPERCASE",
echo   "settings": { "emptyValuePercentage": 0, "dateFormat": "yyyy-MM-dd" },
echo   "dataSources": {},
echo   "columns": [
echo     { "name": "DOCID", "type": "identifier", "required": true },
echo     { "name": "TEXTFIELD", "type": "text", "emptyPercentage": 100 }
echo   ]
echo }
) > "%TMP_PROFILE_100%"

%ZIPPER_CMD% --count %COUNT% --type pdf --column-profile "%TMP_PROFILE_100%" --seed 9999 --loadfile-only --output-path "%TEST_OUTPUT_DIR%\pct100"
if errorlevel 1 (
    echo [ ERROR ] Zipper pct100 run failed
    del "%PY_HELPER%"
    exit /b 1
)

set "DAT_100="
for /f "delims=" %%F in ('dir /b /s "%TEST_OUTPUT_DIR%\pct100\*.dat" 2^>nul') do set "DAT_100=%%F"
for /f %%E in ('python "%PY_HELPER%" count "%DAT_100%"') do set "EMPTIES_100=%%E"
if not "!EMPTIES_100!"=="%COUNT%" (
    echo [ ERROR ] EmptyPercentage=100: expected %COUNT% empties, got !EMPTIES_100!
    del "%PY_HELPER%"
    exit /b 1
)
echo [ SUCCESS ] EmptyPercentage=100: all %COUNT% values are empty

echo [ INFO ] === EmptyPercentage=10 chi-square test ===
set "TMP_PROFILE_10=%TEST_OUTPUT_DIR%\test-empty-pct-10.json"
(
echo {
echo   "name": "test-empty-pct-10",
echo   "description": "Empty percentage test: 10%%",
echo   "version": "1.0",
echo   "fieldNamingConvention": "UPPERCASE",
echo   "settings": { "emptyValuePercentage": 0, "dateFormat": "yyyy-MM-dd" },
echo   "dataSources": {},
echo   "columns": [
echo     { "name": "DOCID", "type": "identifier", "required": true },
echo     { "name": "TEXTFIELD", "type": "text", "emptyPercentage": 10 }
echo   ]
echo }
) > "%TMP_PROFILE_10%"

%ZIPPER_CMD% --count %COUNT% --type pdf --column-profile "%TMP_PROFILE_10%" --seed 9999 --loadfile-only --output-path "%TEST_OUTPUT_DIR%\pct10"
if errorlevel 1 (
    echo [ ERROR ] Zipper pct10 run failed
    del "%PY_HELPER%"
    exit /b 1
)

set "DAT_10="
for /f "delims=" %%F in ('dir /b /s "%TEST_OUTPUT_DIR%\pct10\*.dat" 2^>nul') do set "DAT_10=%%F"
for /f %%E in ('python "%PY_HELPER%" count "%DAT_10%"') do set "EMPTIES_10=%%E"
echo [ INFO ] EmptyPercentage=10: observed !EMPTIES_10! empty out of %COUNT%
python "%PY_HELPER%" chi_square !EMPTIES_10! %COUNT% 0.10 %SIGNIFICANCE%
if errorlevel 1 (
    echo [ ERROR ] Chi-square FAILED for EmptyPercentage=10
    del "%PY_HELPER%"
    exit /b 1
)
echo [ SUCCESS ] EmptyPercentage=10: chi-square PASSED

echo [ INFO ] === EmptyPercentage=50 chi-square test ===
set "TMP_PROFILE_50=%TEST_OUTPUT_DIR%\test-empty-pct-50.json"
(
echo {
echo   "name": "test-empty-pct-50",
echo   "description": "Empty percentage test: 50%%",
echo   "version": "1.0",
echo   "fieldNamingConvention": "UPPERCASE",
echo   "settings": { "emptyValuePercentage": 0, "dateFormat": "yyyy-MM-dd" },
echo   "dataSources": {},
echo   "columns": [
echo     { "name": "DOCID", "type": "identifier", "required": true },
echo     { "name": "TEXTFIELD", "type": "text", "emptyPercentage": 50 }
echo   ]
echo }
) > "%TMP_PROFILE_50%"

%ZIPPER_CMD% --count %COUNT% --type pdf --column-profile "%TMP_PROFILE_50%" --seed 9999 --loadfile-only --output-path "%TEST_OUTPUT_DIR%\pct50"
if errorlevel 1 (
    echo [ ERROR ] Zipper pct50 run failed
    del "%PY_HELPER%"
    exit /b 1
)

set "DAT_50="
for /f "delims=" %%F in ('dir /b /s "%TEST_OUTPUT_DIR%\pct50\*.dat" 2^>nul') do set "DAT_50=%%F"
for /f %%E in ('python "%PY_HELPER%" count "%DAT_50%"') do set "EMPTIES_50=%%E"
echo [ INFO ] EmptyPercentage=50: observed !EMPTIES_50! empty out of %COUNT%
python "%PY_HELPER%" chi_square !EMPTIES_50! %COUNT% 0.50 %SIGNIFICANCE%
if errorlevel 1 (
    echo [ ERROR ] Chi-square FAILED for EmptyPercentage=50
    del "%PY_HELPER%"
    exit /b 1
)
echo [ SUCCESS ] EmptyPercentage=50: chi-square PASSED

del "%PY_HELPER%"
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
echo [ SUCCESS ] All EmptyPercentage tests PASSED.
exit /b 0
