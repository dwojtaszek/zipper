# Test Reorganization Implementation TODO

## Unit Tests Reorganization

### Delete Unwanted Test
- [ ] Delete `PerformanceMonitorTests.cs` (not needed)

### Update Documentation
- [ ] Update README.md and Requirements.md based on previous changes

## Verification

- [ ] Run unit tests to ensure they still pass after reorganization
- [ ] Run E2E tests to ensure paths and references are updated correctly
- [ ] Verify test project builds successfully
- [ ] Check that all test files are accounted for

## Cleanup

- [ ] Remove empty old directories
- [ ] Update any documentation that references old test locations
- [ ] Commit changes with descriptive commit message

## Future Tasks (Do Not Implement Until Requested)

### Pre-commit Hook Optimization
- [ ] Update Windows pre-commit hook to run only unit tests + one basic E2E test
- [ ] Update Linux pre-commit hook to run only unit tests + one basic E2E test
- [ ] Update macOS pre-commit hook to run only unit tests + one basic E2E test
- [ ] Test the optimized pre-commit hooks to ensure they work correctly
- [ ] Verify performance improvement from the optimization

**Goal**: Speed up pre-commit checks by running minimal test suite (unit tests + 1 basic E2E test) instead of full E2E test suite

### Stress E2E Test Suite (Manual Invocation Only)
- [ ] Research current E2E test configurations to identify gaps
- [ ] Design three unique large-scale test scenarios not covered in regular E2E tests
- [ ] Create stress test script that generates 10GB files in unique configuration
- [ ] Create stress test script that generates 20GB files in unique configuration
- [ ] Create stress test script that generates 30GB files in unique configuration
- [ ] Add clear documentation that stress suite is for manual invocation only
- [ ] Add warnings about storage space requirements and runtime expectations
- [ ] Include validation logic for stress test outputs

**Stress Test Scenarios (3 unique configurations not covered in regular E2E):**

1. **10GB Stress Test - Maximum File Count Challenge**
   - Generate 5 million PDF files with minimal individual size
   - Use exponential distribution across 100 folders
   - Enable metadata + text extraction
   - Target: Test maximum file count handling and Zip64 functionality
   - Unique aspect: Tests absolute limits of file count vs size

2. **20GB Stress Test - Multi-Format Complexity**
   - Generate mix of PDF (60%), JPG (30%), EML (10%) file types
   - Use Gaussian distribution across 500 folders
   - Enable all features: metadata, text extraction, attachments (50% for EML)
   - Use UTF-16 encoding for increased complexity
   - Target: Test mixed format processing and memory management
   - Unique aspect: Tests complex mixed-type generation at scale

3. **30GB Stress Test - Attachment-Heavy EML Focus**
   - Generate 1 million EML files with 80% attachment rate
   - Attachments are varied PDF/JPG/TIFF files (2-5MB each)
   - Use proportional distribution across 1000 folders
   - Enable metadata + text extraction for all files and attachments
   - Target: Test attachment handling, nested file processing, and archive size limits
   - Unique aspect: Tests attachment-heavy generation with nested content

**Important Notes:**
- Stress suite is NOT part of regular CI/CD or pre-commit hooks
- Developer must manually invoke stress tests
- Requires significant disk space (+20% overhead) and time (several hours)
- Includes pre-run validation for available disk space
- Each scenario tests unique failure modes not covered in regular E2E tests

## Notes
- PerformanceMonitorTests.cs is marked for removal as user indicated they don't need those tests