@echo off
REM REQ-033, REQ-034, REQ-035: native Windows acceptance, no build fallback.
call "%~dp0_zipper-cli.bat"
if errorlevel 1 exit /b 1
python "%~dp0published_artifact.py"
exit /b %errorlevel%
