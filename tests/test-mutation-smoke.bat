@echo off
REM test-mutation-smoke.bat — Bounded integration smoke for pinned Stryker CLI (#1110)
REM
REM Validates:
REM 1. Pinned Stryker CLI accepts workflow options (--concurrency 4, --output dir, --mutate, etc.)
REM 2. Bounded mutation run on a small production file produces >0 mutants
REM 3. JSON report is located beneath the output dir and normalized to the exact downstream path
REM 4. quality/mutation.py successfully parses the generated report into deterministic categories
REM 5. Missing report and zero selected mutants fail explicitly

setlocal enabledelayedexpansion

set SCRIPT_DIR=%~dp0
set REPO_ROOT=%SCRIPT_DIR%..
set TEMP_DIR=%REPO_ROOT%\results\mutation-smoke-%RANDOM%

set TARGET_FILE=ContentTypeHelper.cs
set TIMEOUT_MINS=15
set PREBUILT_REPORT=

:parse_args
if "%~1"=="" goto after_args
if "%~1"=="--prebuilt-report" (
    set PREBUILT_REPORT=%~2
    shift
    shift
    goto parse_args
)
if "%~1"=="--file" (
    set TARGET_FILE=%~2
    shift
    shift
    goto parse_args
)
if "%~1"=="--timeout" (
    set TIMEOUT_MINS=%~2
    shift
    shift
    goto parse_args
)
shift
goto parse_args

:after_args

mkdir "%TEMP_DIR%"
if errorlevel 1 (
    echo [ ERROR ] Could not create temporary directory: %TEMP_DIR%
    exit /b 1
)

echo [ INFO ] Starting bounded Stryker mutation integration smoke...

echo [ INFO ] 1. Verifying pinned dotnet-stryker CLI options...
dotnet stryker --help >nul 2>&1
if errorlevel 1 (
    echo [ INFO ] Restoring pinned tools...
    dotnet tool restore >nul
)

REM Verify CLI rejects old unsupported option
dotnet stryker --max-concurrent-test-runs 4 >"%TEMP_DIR%\reject.log" 2>&1
findstr /i "Unrecognized option" "%TEMP_DIR%\reject.log" >nul
if errorlevel 1 (
    echo [ ERROR ] Expected CLI to reject --max-concurrent-test-runs
    exit /b 1
)
echo [ SUCCESS ] Verified CLI rejects unsupported --max-concurrent-test-runs

REM Verify CLI accepts pinned options
dotnet stryker --help >"%TEMP_DIR%\help.log" 2>&1
findstr /c:"--concurrency" "%TEMP_DIR%\help.log" >nul || (echo [ ERROR ] Missing --concurrency & exit /b 1)
findstr /c:"--mutate" "%TEMP_DIR%\help.log" >nul || (echo [ ERROR ] Missing --mutate & exit /b 1)
findstr /c:"--project" "%TEMP_DIR%\help.log" >nul || (echo [ ERROR ] Missing --project & exit /b 1)
findstr /c:"--test-project" "%TEMP_DIR%\help.log" >nul || (echo [ ERROR ] Missing --test-project & exit /b 1)
findstr /c:"--reporter" "%TEMP_DIR%\help.log" >nul || (echo [ ERROR ] Missing --reporter & exit /b 1)
findstr /c:"--output" "%TEMP_DIR%\help.log" >nul || (echo [ ERROR ] Missing --output & exit /b 1)
findstr /c:"--break-at" "%TEMP_DIR%\help.log" >nul || (echo [ ERROR ] Missing --break-at & exit /b 1)
findstr /c:"--skip-version-check" "%TEMP_DIR%\help.log" >nul || (echo [ ERROR ] Missing --skip-version-check & exit /b 1)
echo [ SUCCESS ] Stryker CLI accepts all workflow options

set RAW_OUTPUT_DIR=%TEMP_DIR%\stryker-raw
mkdir "%RAW_OUTPUT_DIR%"

if not "%PREBUILT_REPORT%"=="" (
    if exist "%PREBUILT_REPORT%" (
        echo [ INFO ] 2. Using provided prebuilt report: %PREBUILT_REPORT%
        mkdir "%RAW_OUTPUT_DIR%\reports"
        copy "%PREBUILT_REPORT%" "%RAW_OUTPUT_DIR%\reports\mutation-report.json" >nul
        goto locate_report
    )
)

echo [ INFO ] 2. Executing pinned Stryker on small production file: %TARGET_FILE%...
cd /d "%REPO_ROOT%\src"
dotnet stryker --project Zipper.csproj --test-project Zipper.Tests\Zipper.Tests.csproj --mutate "%TARGET_FILE%" --reporter json --output "%RAW_OUTPUT_DIR%" --break-at 0 --concurrency 4 --skip-version-check
set STRYKER_RC=%ERRORLEVEL%
cd /d "%REPO_ROOT%"
if not %STRYKER_RC%==0 (
    echo [ ERROR ] dotnet stryker failed with exit code %STRYKER_RC%
    exit /b %STRYKER_RC%
)

:locate_report
echo [ INFO ] 3. Locating generated JSON report beneath output directory...
if not exist "%RAW_OUTPUT_DIR%\reports\mutation-report.json" (
    echo [ ERROR ] mutation-report.json was not generated
    exit /b 1
)
echo [ SUCCESS ] Located Stryker report at %RAW_OUTPUT_DIR%\reports\mutation-report.json

set DOWNSTREAM_REPORT=%TEMP_DIR%\results\mutation\mutation-report.json
echo [ INFO ] 4. Normalizing report to exact downstream path: %DOWNSTREAM_REPORT%...
python3 "%REPO_ROOT%\tools\typesafe-audit\quality\normalize_report.py" --input "%RAW_OUTPUT_DIR%" --output "%DOWNSTREAM_REPORT%" --scope "%TARGET_FILE%" --repo-root "%REPO_ROOT%"
if errorlevel 1 (
    echo [ ERROR ] normalize_report.py failed on real Stryker output
    exit /b 1
)
if not exist "%DOWNSTREAM_REPORT%" (
    echo [ ERROR ] Downstream normalized report missing at %DOWNSTREAM_REPORT%
    exit /b 1
)
echo [ SUCCESS ] Downstream normalized report created successfully

echo [ INFO ] 5. Verifying schema and categories via quality/mutation.py...
python3 -c "from pathlib import Path; import sys; sys.path.insert(0, r'%REPO_ROOT%\tools\typesafe-audit\quality'); import mutation; cats = mutation.parse_report(Path(r'%DOWNSTREAM_REPORT%'), Path(r'%REPO_ROOT%')); total = sum(len(v) for v in cats.values()); assert total > 0, 'No candidate mutants'; print(f'Extracted {total} mutants across categories: {list(cats.keys())}')"
if errorlevel 1 (
    echo [ ERROR ] quality/mutation.py verification failed
    exit /b 1
)
echo [ SUCCESS ] quality/mutation.py parsed positive mutants with clean category schema

echo [ INFO ] 6. Testing explicit failure modes...
mkdir "%TEMP_DIR%\empty-dir"
python3 "%REPO_ROOT%\tools\typesafe-audit\quality\normalize_report.py" --input "%TEMP_DIR%\empty-dir" --output "%TEMP_DIR%\dummy.json" >nul 2>&1
if not errorlevel 1 (
    echo [ ERROR ] Expected normalize_report.py to fail on missing report
    exit /b 1
)
echo [ SUCCESS ] Verified missing report fails explicitly

python3 -c "import json; json.dump({'files': {'Foo.cs': {'mutants': [{'status': 'Ignored'}]}}}, open(r'%TEMP_DIR%\zero.json', 'w'))"
python3 "%REPO_ROOT%\tools\typesafe-audit\quality\normalize_report.py" --input "%TEMP_DIR%\zero.json" --output "%TEMP_DIR%\dummy2.json" --scope "%TARGET_FILE%" >nul 2>&1
if not errorlevel 1 (
    echo [ ERROR ] Expected normalize_report.py to fail on 0 selected mutants
    exit /b 1
)
echo [ SUCCESS ] Verified zero selected mutants fails explicitly

echo [ SUCCESS ] All Stryker mutation smoke contract checks passed!
rmdir /s /q "%TEMP_DIR%"
exit /b 0
