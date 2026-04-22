@echo off

REM Resolve the Zipper binary once. Sets %ZIPPER_CMD%.
call "%~dp0_zipper-cli.bat"
REM Cross-platform compatibility test script for Zipper
REM This script tests core functionality across different platforms

setlocal enabledelayedexpansion

REM Test configuration
set TEST_OUTPUT_DIR=.\cross-platform-results
set PROJECT=src\Zipper.csproj

REM Helper functions
:print_success
    echo [ SUCCESS ] %~1
    goto :eof

:print_info
    echo [ INFO ] %~1
    goto :eof

:print_warning
    echo [ WARNING ] %~1
    goto :eof

:print_error
    echo [ ERROR ] %~1
    exit /b 1

REM Main execution
call :print_info "Starting cross-platform compatibility tests on Windows"
call :print_info "Platform: Windows"
call :print_info "Architecture: %PROCESSOR_ARCHITECTURE%"

REM Clean up previous results
if exist "%TEST_OUTPUT_DIR%" rmdir /s /q "%TEST_OUTPUT_DIR%"
mkdir "%TEST_OUTPUT_DIR%"

REM Test 1: Basic PDF generation
call :print_info "Test 1: Basic PDF generation"
%ZIPPER_CMD% --type pdf --count 5 --output-path "%TEST_OUTPUT_DIR%\basic_pdf"
if exist "%TEST_OUTPUT_DIR%\basic_pdf.zip" (
    call :print_success "Basic PDF generation completed"
) else (
    call :print_error "Basic PDF generation failed"
    exit /b 1
)

REM Test 2: Basic EML generation
call :print_info "Test 2: Basic EML generation"
%ZIPPER_CMD% --type eml --count 3 --output-path "%TEST_OUTPUT_DIR%\basic_eml"
if exist "%TEST_OUTPUT_DIR%\basic_eml.zip" (
    call :print_success "Basic EML generation completed"
) else (
    call :print_error "Basic EML generation failed"
    exit /b 1
)

REM Test 3: Different encodings
call :print_info "Test 3: Different encodings"

REM UTF-8
%ZIPPER_CMD% --type pdf --count 3 --output-path "%TEST_OUTPUT_DIR%\utf8" --encoding UTF-8

REM UTF-16
%ZIPPER_CMD% --type pdf --count 3 --output-path "%TEST_OUTPUT_DIR%\utf16" --encoding UTF-16

REM ANSI
%ZIPPER_CMD% --type pdf --count 3 --output-path "%TEST_OUTPUT_DIR%\ansi" --encoding ANSI

if exist "%TEST_OUTPUT_DIR%\utf8.zip" if exist "%TEST_OUTPUT_DIR%\utf16.zip" if exist "%TEST_OUTPUT_DIR%\ansi.zip" (
    call :print_success "All encoding tests completed"
) else (
    call :print_error "Encoding tests failed"
    exit /b 1
)

REM Test 4: Different distributions
call :print_info "Test 4: Different distributions"

for %%D in (proportional gaussian exponential) do (
    %ZIPPER_CMD% --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\dist_%%D" --folders 3 --distribution %%D
    if exist "%TEST_OUTPUT_DIR%\dist_%%D.zip" (
        call :print_success "%%D distribution test completed"
    ) else (
        call :print_error "%%D distribution test failed"
        exit /b 1
    )
)

REM Test 5: With metadata
call :print_info "Test 5: Testing with metadata"
%ZIPPER_CMD% --type pdf --count 5 --output-path "%TEST_OUTPUT_DIR%\metadata" --with-metadata
if exist "%TEST_OUTPUT_DIR%\metadata.zip" (
    call :print_success "Metadata test completed"
) else (
    call :print_error "Metadata test failed"
    exit /b 1
)

REM Test 6: With text extraction
call :print_info "Test 6: Testing with text extraction"
%ZIPPER_CMD% --type pdf --count 5 --output-path "%TEST_OUTPUT_DIR%\text" --with-text
if exist "%TEST_OUTPUT_DIR%\text.zip" (
    call :print_success "Text extraction test completed"
) else (
    call :print_error "Text extraction test failed"
    exit /b 1
)

REM Test 7: EML with attachments
call :print_info "Test 7: Testing EML with attachments"
%ZIPPER_CMD% --type eml --count 5 --output-path "%TEST_OUTPUT_DIR%\eml_attachments" --attachment-rate 80
if exist "%TEST_OUTPUT_DIR%\eml_attachments.zip" (
    call :print_success "EML attachments test completed"
) else (
    call :print_error "EML attachments test failed"
    exit /b 1
)

REM Test 8: Path compatibility (standard path)
call :print_info "Test 8: Testing path compatibility - standard path"
%ZIPPER_CMD% --type pdf --count 2 --output-path "%TEST_OUTPUT_DIR%\standard_path"
if exist "%TEST_OUTPUT_DIR%\standard_path.zip" (
    call :print_success "Standard path test completed"
) else (
    call :print_error "Standard path test failed"
    exit /b 1
)

REM Test 9: Path with spaces
call :print_info "Test 9: Testing path compatibility - path with spaces"
%ZIPPER_CMD% --type pdf --count 2 --output-path "%TEST_OUTPUT_DIR%\path with spaces"
if exist "%TEST_OUTPUT_DIR%\path with spaces.zip" (
    call :print_success "Path with spaces test completed"
) else (
    call :print_error "Path with spaces test failed"
    exit /b 1
)

REM Test 10: Path with dashes
call :print_info "Test 10: Testing path compatibility - path with dashes"
%ZIPPER_CMD% --type pdf --count 2 --output-path "%TEST_OUTPUT_DIR%\path-with-dashes"
if exist "%TEST_OUTPUT_DIR%\path-with-dashes.zip" (
    call :print_success "Path with dashes test completed"
) else (
    call :print_error "Path with dashes test failed"
    exit /b 1
)

REM Test 11: Performance test
call :print_info "Test 11: Testing performance"
%ZIPPER_CMD% --type pdf --count 20 --output-path "%TEST_OUTPUT_DIR%\perf"
if exist "%TEST_OUTPUT_DIR%\perf.zip" (
    call :print_success "Performance test completed"
) else (
    call :print_error "Performance test failed"
    exit /b 1
)

REM Cleanup
call :print_info "Cleaning up test output..."
rmdir /s /q "%TEST_OUTPUT_DIR%"

call :print_success "All cross-platform compatibility tests passed successfully!"
endlocal
