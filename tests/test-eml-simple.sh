#!/bin/bash
# Simple EML test script

echo "Testing EML functionality..."

# Test 1: Basic EML
echo "Test 1: Basic EML generation"
mkdir -p test1
cd test1
dotnet run --project ../../Zipper/Zipper.csproj -- --type eml --count 3 --output-path . --folders 1
if [ -f "archive_"*.zip ]; then
    echo "✓ Basic EML test passed"
else
    echo "✗ Basic EML test failed"
fi
cd ..

# Test 2: EML with metadata
echo "Test 2: EML with metadata"
mkdir -p test2
cd test2
dotnet run --project ../../Zipper/Zipper.csproj -- --type eml --count 3 --output-path . --folders 1 --with-metadata
if [ -f "archive_"*.zip ]; then
    echo "✓ EML with metadata test passed"
else
    echo "✗ EML with metadata test failed"
fi
cd ..

# Test 3: EML with text
echo "Test 3: EML with text"
mkdir -p test3
cd test3
dotnet run --project ../../Zipper/Zipper.csproj -- --type eml --count 3 --output-path . --folders 1 --with-text
if [ -f "archive_"*.zip ]; then
    echo "✓ EML with text test passed"
else
    echo "✗ EML with text test failed"
fi
cd ..

# Test 4: EML with both
echo "Test 4: EML with metadata and text"
mkdir -p test4
cd test4
dotnet run --project ../../Zipper/Zipper.csproj -- --type eml --count 3 --output-path . --folders 1 --with-metadata --with-text
if [ -f "archive_"*.zip ]; then
    echo "✓ EML with both flags test passed"
else
    echo "✗ EML with both flags test failed"
fi
cd ..

echo "All tests completed."