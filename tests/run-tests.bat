@echo off

REM --- Test Configuration ---

REM The directory where test output will be generated.
set TEST_OUTPUT_DIR=.\test_output

REM The .NET project to run.
set PROJECT=Zipper\Zipper.csproj

REM --- Helper Functions ---

:print_success
    echo [ SUCCESS ] %~1
    goto :eof

:print_info
    echo [ INFO ] %~1
    goto :eof

REM --- Test Cases ---

call :print_info "Starting test suite..."

REM Create a temporary directory for test output.
if exist "%TEST_OUTPUT_DIR%" (
    rmdir /s /q "%TEST_OUTPUT_DIR%"
)
mkdir "%TEST_OUTPUT_DIR%"

REM Test Case 1: Basic PDF generation
call :print_info "Running Test Case 1: Basic PDF generation"
dotnet run --project "%PROJECT%" -- --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_basic"
if errorlevel 1 exit /b 1
call :print_success "Test Case 1 passed."

REM Test Case 2: JPG generation with different encoding
call :print_info "Running Test Case 2: JPG generation with UTF-16 encoding"
dotnet run --project "%PROJECT%" -- --type jpg --count 10 --output-path "%TEST_OUTPUT_DIR%\jpg_encoding" --encoding UTF-16
if errorlevel 1 exit /b 1
call :print_success "Test Case 2 passed."

REM Test Case 3: TIFF generation with multiple folders and proportional distribution
call :print_info "Running Test Case 3: TIFF generation with multiple folders and proportional distribution"
dotnet run --project "%PROJECT%" -- --type tiff --count 100 --output-path "%TEST_OUTPUT_DIR%\tiff_folders" --folders 5 --distribution proportional
if errorlevel 1 exit /b 1
call :print_success "Test Case 3 passed."

REM Test Case 4: PDF generation with Gaussian distribution
call :print_info "Running Test Case 4: PDF generation with Gaussian distribution"
dotnet run --project "%PROJECT%" -- --type pdf --count 100 --output-path "%TEST_OUTPUT_DIR%\pdf_gaussian" --folders 10 --distribution gaussian
if errorlevel 1 exit /b 1
call :print_success "Test Case 4 passed."

REM Test Case 5: JPG generation with Exponential distribution
call :print_info "Running Test Case 5: JPG generation with Exponential distribution"
dotnet run --project "%PROJECT%" -- --type jpg --count 100 --output-path "%TEST_OUTPUT_DIR%\jpg_exponential" --folders 10 --distribution exponential
if errorlevel 1 exit /b 1
call :print_success "Test Case 5 passed."

REM Test Case 6: PDF generation with metadata
call :print_info "Running Test Case 6: PDF generation with metadata"
dotnet run --project "%PROJECT%" -- --type pdf --count 10 --output-path "%TEST_OUTPUT_DIR%\pdf_metadata" --with-metadata
if errorlevel 1 exit /b 1
call :print_success "Test Case 6 passed."

REM Test Case 7: All options combined
call :print_info "Running Test Case 7: All options combined"
dotnet run --project "%PROJECT%" -- --type tiff --count 100 --output-path "%TEST_OUTPUT_DIR%\all_options" --folders 20 --encoding ANSI --distribution gaussian --with-metadata
if errorlevel 1 exit /b 1
call :print_success "Test Case 7 passed."

REM --- Cleanup ---

call :print_info "Cleaning up test output..."
rmdir /s /q "%TEST_OUTPUT_DIR%"

call :print_success "All tests passed successfully!"
