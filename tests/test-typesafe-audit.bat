@echo off
REM E2E test: TypeSafe audit runner foundation (#955).
REM Runs the Python unit suite, a no-network fixture-mode audit, and workflow
REM structure checks (permissions, fork safety, pinning, advisory behavior).

setlocal

set SCRIPT_DIR=%~dp0
set TOOL_DIR=%SCRIPT_DIR%..\tools\typesafe-audit
set TEMP_DIR=%SCRIPT_DIR%..\results\test-typesafe-audit-%RANDOM%
mkdir "%TEMP_DIR%"
if errorlevel 1 (
    echo [ ERROR ] Could not create the temporary test directory.
    exit /b 1
)

set WORKFLOW_FILE=%SCRIPT_DIR%..\.github\workflows\typesafe-audit.yml

REM --- Part 1: Python unit tests (no network) ---

set TYPESAFE_UNIT_LOG=%TEMP_DIR%\typesafe-unit.log
set ZIPPER_UNIT_LOG=%TEMP_DIR%\zipper-unit.log

echo Running TypeSafe runner unit tests...
python3 -m unittest discover -s "%TOOL_DIR%\tests" >"%TYPESAFE_UNIT_LOG%" 2>&1
if errorlevel 1 (
    echo [ ERROR ] TypeSafe runner unit tests failed.
    type "%TYPESAFE_UNIT_LOG%" >&2
    exit /b 1
)
echo [ SUCCESS ] TypeSafe runner unit tests passed.

echo Running Zipper runner unit tests...
python3 -m unittest discover -s "%SCRIPT_DIR%..\.zipper-runner\tests" >"%ZIPPER_UNIT_LOG%" 2>&1
if errorlevel 1 (
    echo [ ERROR ] Zipper runner unit tests failed.
    type "%ZIPPER_UNIT_LOG%" >&2
    exit /b 1
)
echo [ SUCCESS ] Zipper runner unit tests passed.

REM --- Part 2: fixture-mode audit end to end (no credential, no network) ---

REM Record a deterministic fixture for the current sample inputs, then replay it.
set FIXTURE_DIR=%TEMP_DIR%\fixtures
mkdir "%FIXTURE_DIR%"
if errorlevel 1 (
    echo [ ERROR ] Could not create the fixture directory.
    exit /b 1
)
set FIXTURE_LOG=%TEMP_DIR%\fixture.log
python3 "%TOOL_DIR%\record_sample_fixture.py" "%FIXTURE_DIR%" >"%FIXTURE_LOG%" 2>&1
if errorlevel 1 (
    echo [ ERROR ] Sample fixture recording failed.
    type "%FIXTURE_LOG%" >&2
    exit /b 1
)

set JSON_OUT=%TEMP_DIR%\report.json
set MD_OUT=%TEMP_DIR%\report.md
set RUNNER_LOG=%TEMP_DIR%\runner1.log
python3 "%TOOL_DIR%\runner.py" --mode fixture --fixture-dir "%FIXTURE_DIR%" --files "%TOOL_DIR%\questions\files-sample.list" --questions "%TOOL_DIR%\questions\example.json" --json-out "%JSON_OUT%" --md-out "%MD_OUT%" >"%RUNNER_LOG%" 2>&1
if errorlevel 1 (
    echo [ ERROR ] Fixture-mode audit run failed.
    type "%RUNNER_LOG%" >&2
    exit /b 1
)

if not exist "%JSON_OUT%" (
    echo [ ERROR ] JSON report missing.
    exit /b 1
)
if not exist "%MD_OUT%" (
    echo [ ERROR ] Markdown report missing.
    exit /b 1
)
REM An empty report must fail: fc /b and the secret check both pass on zero bytes.
for %%A in ("%JSON_OUT%") do if %%~zA LEQ 0 (
    echo [ ERROR ] JSON report is empty.
    exit /b 1
)
for %%A in ("%MD_OUT%") do if %%~zA LEQ 0 (
    echo [ ERROR ] Markdown report is empty.
    exit /b 1
)

REM Deterministic: a second identical run produces byte-identical JSON and Markdown.
set JSON_OUT2=%TEMP_DIR%\report2.json
set MD_OUT2=%TEMP_DIR%\report2.md
set RUNNER_LOG2=%TEMP_DIR%\runner2.log
python3 "%TOOL_DIR%\runner.py" --mode fixture --fixture-dir "%FIXTURE_DIR%" --files "%TOOL_DIR%\questions\files-sample.list" --questions "%TOOL_DIR%\questions\example.json" --json-out "%JSON_OUT2%" --md-out "%MD_OUT2%" >"%RUNNER_LOG2%" 2>&1
if errorlevel 1 (
    echo [ ERROR ] Fixture-mode deterministic rerun failed.
    type "%RUNNER_LOG2%" >&2
    exit /b 1
)

set FC_LOG=%TEMP_DIR%\fc.log
fc /b "%JSON_OUT%" "%JSON_OUT2%" >"%FC_LOG%" 2>&1
if errorlevel 1 (
    echo [ ERROR ] Fixture-mode output is not deterministic.
    type "%FC_LOG%" >&2
    exit /b 1
)
fc /b "%MD_OUT%" "%MD_OUT2%" >"%FC_LOG%" 2>&1
if errorlevel 1 (
    echo [ ERROR ] Fixture-mode Markdown output is not deterministic.
    type "%FC_LOG%" >&2
    exit /b 1
)

REM Reports must not contain secrets. /M prints only the matching file names, so
REM the captured evidence never carries the secret text itself. A match means the
REM reports may hold a secret, so the whole temp directory is removed here.
set SECRET_CHECK_LOG=%TEMP_DIR%\secret-check.log
findstr /M /I /C:"TYPESAFE_API_KEY" /C:"Bearer " "%JSON_OUT%" "%MD_OUT%" >"%SECRET_CHECK_LOG%" 2>&1
if errorlevel 2 (
    echo [ ERROR ] Report secret check could not be completed.
    type "%SECRET_CHECK_LOG%" >&2
    exit /b 1
)
if not errorlevel 1 (
    echo [ ERROR ] Report files must not contain secrets:
    type "%SECRET_CHECK_LOG%" >&2
    rd /s /q "%TEMP_DIR%" >nul 2>&1
    exit /b 1
)

echo [ SUCCESS ] Fixture-mode audit is deterministic and secret-free.

REM --- Part 3: workflow structure checks ---

if not exist "%WORKFLOW_FILE%" (
    echo [ ERROR ] Missing "%WORKFLOW_FILE%"
    exit /b 1
)

set WORKFLOW_CHECK_LOG=%TEMP_DIR%\workflow-check.log
set ACTION_USES_LOG=%TEMP_DIR%\action-uses.log

call :check_contains "least-privilege permissions" "^permissions:"
if errorlevel 1 exit /b 1
call :check_contains "contents: read" "contents: read"
if errorlevel 1 exit /b 1
call :check_absent "no pull_request_target (fork safety)" "pull_request_target"
if errorlevel 1 exit /b 1
call :check_contains "concurrency cancellation" "^concurrency:"
if errorlevel 1 exit /b 1
call :check_contains "explicit job timeout" "timeout-minutes:"
if errorlevel 1 exit /b 1
call :check_contains "weekly schedule trigger" "^  schedule:"
if errorlevel 1 exit /b 1
call :check_contains "manual dispatch trigger" "workflow_dispatch"
if errorlevel 1 exit /b 1
call :check_contains "path-filtered PR trigger" "^    paths:"
if errorlevel 1 exit /b 1
call :check_contains "TYPESAFE_API_KEY secret wiring" "TYPESAFE_API_KEY"
if errorlevel 1 exit /b 1
call :check_contains "fork/secret-availability guard" "secret"
if errorlevel 1 exit /b 1

call :check_action_pins
if errorlevel 1 exit /b 1

REM Advisory: the runner must not run in --strict (blocking) mode in CI. Both
REM token orders are matched, and the dot is escaped so runnerXpy cannot match.
findstr /R /C:"runner\.py.*--strict" /C:"--strict.*runner\.py" "%WORKFLOW_FILE%" >"%WORKFLOW_CHECK_LOG%" 2>&1
if errorlevel 2 (
    echo [ ERROR ] Workflow check failed: advisory mode could not be checked.
    type "%WORKFLOW_CHECK_LOG%" >&2
    exit /b 1
)
if not errorlevel 1 (
    echo [ ERROR ] Workflow check failed: audit must stay advisory (no --strict)
    type "%WORKFLOW_CHECK_LOG%" >&2
    exit /b 1
)
echo [ SUCCESS ] Workflow check: advisory (no --strict)

REM Every failure path above types its log before exiting, so the temp directory
REM is only needed for the success path here.
rd /s /q "%TEMP_DIR%"
if errorlevel 1 (
    echo [ ERROR ] Could not remove the temporary test directory.
    exit /b 1
)
echo [ SUCCESS ] All TypeSafe audit E2E tests passed!
exit /b 0

:check_contains
findstr /R /C:"%~2" "%WORKFLOW_FILE%" >"%WORKFLOW_CHECK_LOG%" 2>&1
if errorlevel 1 (
    echo [ ERROR ] Workflow check failed: %~1
    type "%WORKFLOW_CHECK_LOG%" >&2
    exit /b 1
)
echo [ SUCCESS ] Workflow check: %~1
exit /b 0

:check_absent
findstr /R /C:"%~2" "%WORKFLOW_FILE%" >"%WORKFLOW_CHECK_LOG%" 2>&1
if errorlevel 2 (
    echo [ ERROR ] Workflow check failed: %~1 could not be checked.
    type "%WORKFLOW_CHECK_LOG%" >&2
    exit /b 1
)
if not errorlevel 1 (
    echo [ ERROR ] Workflow check failed: %~1 must be absent
    type "%WORKFLOW_CHECK_LOG%" >&2
    exit /b 1
)
echo [ SUCCESS ] Workflow check: %~1
exit /b 0

:check_action_pins
REM Action references may use quoted YAML keys ('uses': or "uses":), so all
REM three spellings are scanned. cmd embeds a literal quote as "" inside a
REM quoted argument.
findstr /R /C:"uses:" /C:"uses': " /C:"uses"": " "%WORKFLOW_FILE%" >"%ACTION_USES_LOG%" 2>&1
if errorlevel 2 (
    echo [ ERROR ] Workflow check failed: action pins could not be read.
    type "%ACTION_USES_LOG%" >&2
    exit /b 1
)
REM No action lines at all must fail rather than pass vacuously.
if errorlevel 1 (
    echo [ ERROR ] Workflow check failed: no action uses lines found.
    exit /b 1
)
for /f "usebackq delims=" %%L in ("%ACTION_USES_LOG%") do (
    call :check_action_pin "%%L"
    if errorlevel 1 exit /b 1
)
echo [ SUCCESS ] Workflow check: all actions pinned to full commit SHAs
exit /b 0

:check_action_pin
REM Strip a trailing YAML comment first, so a SHA inside a comment cannot pose
REM as the pin. Workflow text is quoted everywhere it is echoed.
set "ACTION_LINE=%~1"
for /f "tokens=1,* delims=#" %%A in ("%ACTION_LINE%") do set "ACTION_LINE=%%A"
REM A comment-only line has nothing left after the strip: it is not an action.
set "ACTION_REMAINDER="
for /f "tokens=1" %%Z in ("%ACTION_LINE%") do set "ACTION_REMAINDER=%%Z"
if not defined ACTION_REMAINDER exit /b 0
set "ACTION_REF="
for /f "tokens=1,* delims=@" %%A in ("%ACTION_LINE%") do set "ACTION_REF=%%B"
if not defined ACTION_REF goto :action_pin_invalid
set "ACTION_BEFORE_COMMENT="
set "ACTION_SHA="
set "ACTION_EXTRA="
for /f "tokens=1,* delims=#" %%A in ("%ACTION_REF%") do set "ACTION_BEFORE_COMMENT=%%A"
for /f "tokens=1" %%A in ("%ACTION_BEFORE_COMMENT%") do set "ACTION_SHA=%%A"
for /f "tokens=2" %%B in ("%ACTION_BEFORE_COMMENT%") do set "ACTION_EXTRA=%%B"
if defined ACTION_EXTRA goto :action_pin_invalid
if not defined ACTION_SHA goto :action_pin_invalid
if "%ACTION_SHA:~39,1%"=="" goto :action_pin_invalid
if not "%ACTION_SHA:~40%"=="" goto :action_pin_invalid
REM Hex validation without a regex engine: if the ref is made only of hex
REM digits, treating them as delimiters leaves nothing to iterate, so any
REM surviving token is a non-hex character.
set "NON_HEX="
for /f "delims=0123456789abcdef" %%A in ("%ACTION_SHA%") do set "NON_HEX=%%A"
if defined NON_HEX goto :action_pin_invalid
exit /b 0

:action_pin_invalid
echo [ ERROR ] Workflow check failed: action must be pinned to a full commit SHA: "%~1"
exit /b 1
