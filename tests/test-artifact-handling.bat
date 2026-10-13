@echo off
setlocal enabledelayedexpansion

REM Test script to validate artifact handling in current workflows (Windows wrapper)
REM Invokes shared deterministic YAML-aware structural validation in workflow_validator.py (#1112)

echo === Artifact Handling Test (Windows) ===
echo Testing artifact creation and handling patterns

set "SCRIPT_DIR=%~dp0"
set "REPO_ROOT=%SCRIPT_DIR%.."

set "PYCMD="
python3 --version >nul 2>&1 && set "PYCMD=python3"
if not defined PYCMD python --version >nul 2>&1 && set "PYCMD=python"
if not defined PYCMD py -3 --version >nul 2>&1 && set "PYCMD=py -3"
if not defined PYCMD (
    echo [ ERROR ] Python interpreter not found.
    exit /b 1
)

%PYCMD% "%REPO_ROOT%\tools\typesafe-audit\workflow_validator.py" --artifacts %*
if errorlevel 1 (
    echo [ ERROR ] Artifact handling validation failed.
    exit /b 1
)

endlocal
