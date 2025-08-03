#!/bin/bash

# Exit immediately if a command exits with a non-zero status.
set -e

# --- Test Configuration ---

# The directory where test output will be generated.
TEST_OUTPUT_DIR="./test_output"

# The .NET project to run.
PROJECT="Zipper/Zipper.csproj"

# --- Helper Functions ---

# Prints a message with a green background.
function print_success() {
  echo -e "\e[42m[ SUCCESS ]\e[0m $1"
}

# Prints a message with a blue background.
function print_info() {
  echo -e "\e[44m[ INFO ]\e[0m $1"
}

# --- Test Cases ---

print_info "Starting test suite..."

# Create a temporary directory for test output.
rm -rf "$TEST_OUTPUT_DIR"
mkdir -p "$TEST_OUTPUT_DIR"

# Test Case 1: Basic PDF generation
print_info "Running Test Case 1: Basic PDF generation"
dotnet run --project "$PROJECT" -- --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_basic"
print_success "Test Case 1 passed."

# Test Case 2: JPG generation with different encoding
print_info "Running Test Case 2: JPG generation with UTF-16 encoding"
dotnet run --project "$PROJECT" -- --type jpg --count 10 --output-path "$TEST_OUTPUT_DIR/jpg_encoding" --encoding UTF-16
print_success "Test Case 2 passed."

# Test Case 3: TIFF generation with multiple folders and proportional distribution
print_info "Running Test Case 3: TIFF generation with multiple folders and proportional distribution"
dotnet run --project "$PROJECT" -- --type tiff --count 100 --output-path "$TEST_OUTPUT_DIR/tiff_folders" --folders 5 --distribution proportional
print_success "Test Case 3 passed."

# Test Case 4: PDF generation with Gaussian distribution
print_info "Running Test Case 4: PDF generation with Gaussian distribution"
dotnet run --project "$PROJECT" -- --type pdf --count 100 --output-path "$TEST_OUTPUT_DIR/pdf_gaussian" --folders 10 --distribution gaussian
print_success "Test Case 4 passed."

# Test Case 5: JPG generation with Exponential distribution
print_info "Running Test Case 5: JPG generation with Exponential distribution"
dotnet run --project "$PROJECT" -- --type jpg --count 100 --output-path "$TEST_OUTPUT_DIR/jpg_exponential" --folders 10 --distribution exponential
print_success "Test Case 5 passed."

# Test Case 6: PDF generation with metadata
print_info "Running Test Case 6: PDF generation with metadata"
dotnet run --project "$PROJECT" -- --type pdf --count 10 --output-path "$TEST_OUTPUT_DIR/pdf_metadata" --with-metadata
print_success "Test Case 6 passed."

# Test Case 7: All options combined
print_info "Running Test Case 7: All options combined"
dotnet run --project "$PROJECT" -- --type tiff --count 100 --output-path "$TEST_OUTPUT_DIR/all_options" --folders 20 --encoding ANSI --distribution gaussian --with-metadata
print_success "Test Case 7 passed."

# --- Cleanup ---

print_info "Cleaning up test output..."
rm -rf "$TEST_OUTPUT_DIR"

print_success "All tests passed successfully!"
