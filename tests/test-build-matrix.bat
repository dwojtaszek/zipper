@echo off
setlocal enabledelayedexpansion

REM Test script to validate build matrix strategy in current workflows (Windows)
REM This script checks matrix configuration and platform support

echo === Build Matrix Test (Windows) ===
echo Testing matrix strategy and platform configuration

REM Analyze current test.yml matrix
echo 1. Analyzing test.yml matrix strategy...
findstr /C:"matrix:" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ test.yml has matrix strategy
) else (
    echo ✗ test.yml missing matrix strategy
    exit /b 1
)

REM Check matrix operating systems
echo 2. Checking matrix operating systems...
findstr /C:"ubuntu-latest" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found ubuntu-latest in test matrix
) else (
    echo ✗ Missing ubuntu-latest in test matrix
    exit /b 1
)

findstr /C:"windows-latest" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found windows-latest in test matrix
) else (
    echo ✗ Missing windows-latest in test matrix
    exit /b 1
)

findstr /C:"macos-latest" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found macos-latest in test matrix
) else (
    echo ✗ Missing macos-latest in test matrix
    exit /b 1
)

REM Check matrix variable syntax
findstr /C:"runs-on: ${{ matrix.os }}" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Uses correct matrix variable syntax
) else (
    echo ✗ Incorrect matrix variable syntax
    exit /b 1
)

REM Analyze build.yml platform support
echo 3. Analyzing build.yml platform support...
findstr /C:"win-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found win-x64 in build configuration
) else (
    echo ✗ Missing win-x64 in build configuration
    exit /b 1
)

findstr /C:"linux-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found linux-x64 in build configuration
) else (
    echo ✗ Missing linux-x64 in build configuration
    exit /b 1
)

findstr /C:"osx-arm64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found osx-arm64 in build configuration
) else (
    echo ✗ Missing osx-arm64 in build configuration
    exit /b 1
)

REM Check for runtime specifications
echo 4. Checking runtime specifications...
findstr /C:"-r win-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found runtime specification: -r win-x64
) else (
    echo ✗ Missing runtime specification: -r win-x64
    exit /b 1
)

findstr /C:"-r linux-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found runtime specification: -r linux-x64
) else (
    echo ✗ Missing runtime specification: -r linux-x64
    exit /b 1
)

findstr /C:"-r osx-arm64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found runtime specification: -r osx-arm64
) else (
    echo ✗ Missing runtime specification: -r osx-arm64
    exit /b 1
)

REM Check build runners
echo 5. Checking build runner configuration...
findstr /C:"runs-on: ubuntu-latest" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Build job runs on ubuntu-latest
) else (
    echo ✗ Build job runner not properly configured
    exit /b 1
)

REM Check release job runner
findstr /C:"runs-on: ubuntu-latest" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Release job runs on ubuntu-latest
) else (
    echo ✗ Release job runner not properly configured
    exit /b 1
)

REM Validate cross-platform build logic
echo 6. Validating cross-platform build logic...
findstr /C:"self-contained true" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Builds are self-contained
) else (
    echo ✗ Builds are not self-contained
    exit /b 1
)

REM Check output directories
echo 7. Checking output directories...
findstr /C:"publish/win-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found output directory: publish/win-x64
) else (
    echo ✗ Missing output directory: publish/win-x64
    exit /b 1
)

findstr /C:"publish/linux-x64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found output directory: publish/linux-x64
) else (
    echo ✗ Missing output directory: publish/linux-x64
    exit /b 1
)

findstr /C:"publish/osx-arm64" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found output directory: publish/osx-arm64
) else (
    echo ✗ Missing output directory: publish/osx-arm64
    exit /b 1
)

REM Check conditional platform logic in tests
echo 8. Checking conditional platform logic in tests...
findstr /C:"runner.os != 'Windows'" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found Unix/Linux conditional logic
) else (
    echo ✗ Missing Unix/Linux conditional logic
    exit /b 1
)

findstr /C:"runner.os == 'Windows'" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Found Windows conditional logic
) else (
    echo ✗ Missing Windows conditional logic
    exit /b 1
)

REM Check for proper shell specification
echo 9. Checking shell specifications...
findstr /C:"shell: cmd" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Windows steps use cmd shell
) else (
    echo ✗ Missing shell specification for Windows
    exit /b 1
)

REM Check for test script execution patterns
echo 10. Checking test script execution patterns...
findstr /C:"./tests/run-tests.sh" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Unix/Linux uses shell script
) else (
    echo ✗ Missing Unix/Linux shell script execution
    exit /b 1
)

findstr /C:".\tests\run-tests.bat" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Windows uses batch script
) else (
    echo ✗ Missing Windows batch script execution
    exit /b 1
)

REM Verify .NET setup consistency
echo 11. Verifying .NET setup consistency...
findstr /C:"dotnet-version: 8.0.x" .github\workflows\build.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Build uses .NET 8.0.x
) else (
    echo ✗ Build missing .NET 8.0.x
    exit /b 1
)

findstr /C:"dotnet-version: 8.0.x" .github\workflows\test.yml >nul
if !errorlevel! equ 0 (
    echo ✓ Test uses .NET 8.0.x
) else (
    echo ✗ Test missing .NET 8.0.x
    exit /b 1
)

echo.
echo === All Build Matrix Tests Passed ===
echo Current matrix strategy supports unified workflow design