# Quickstart: Unified Build and Test Workflow

**Purpose**: Validate the new unified build-and-test.yml workflow functionality
**Prerequisites**: GitHub repository with existing build.yml and test.yml workflows

## Validation Steps

### 1. Pre-Implementation Verification
```bash
# Verify current workflows exist
ls -la .github/workflows/build.yml .github/workflows/test.yml

# Verify current workflow functionality
git log --oneline -5  # Check recent commits
```

### 2. Implementation Verification
```bash
# After implementation, verify new workflow exists
ls -la .github/workflows/build-and-test.yml

# Verify old workflows are removed
ls .github/workflows/build.yml .github/workflows/test.yml
# Should return: No such file or directory
```

### 3. Workflow Trigger Test
```bash
# Push to master branch to trigger workflow
git checkout master
git commit --allow-empty -m "test: trigger unified workflow"
git push origin master

# Monitor workflow execution in GitHub Actions UI
# Expected sequence: lint → build → test → release
```

### 4. Workflow Job Validation

#### Lint Job Validation
- **Expected**: Lint job runs first on ubuntu-latest
- **Validation**: Check .editorconfig validation
- **Success Criteria**: Job completes with no style violations

#### Build Job Validation
- **Expected**: Parallel builds for Windows x64, Linux x64, macOS ARM64
- **Validation**: Check matrix strategy execution
- **Success Criteria**: All platform builds succeed and upload artifacts

#### Test Job Validation
- **Expected**: Tests run on all platforms using downloaded artifacts
- **Validation**: Verify tests use build artifacts, not source
- **Success Criteria**: All platform tests pass

#### Release Job Validation
- **Expected**: Automatic release creation on success
- **Validation**: Check GitHub releases page
- **Success Criteria**: Release created with all platform artifacts

### 5. Caching Validation
```bash
# Check cache usage in subsequent builds
# First build: Cache miss, full build
# Second build: Cache hit, faster build
# Compare build times between runs
```

### 6. Failure Handling Test

#### Lint Failure Test
```bash
# Intentionally add style violation to .editorconfig
# Commit and push
# Expected: Workflow fails at lint step, no build/test/release
```

#### Build Failure Test
```bash
# Intentionally break build (e.g., syntax error)
# Commit and push
# Expected: Workflow fails at build step, no test/release
```

#### Test Failure Test
```bash
# Intentionally break a test
# Commit and push
# Expected: Workflow fails at test step, no release
```

## Success Metrics

### Performance Metrics
- **Build Time**: Should be comparable or faster than current separate workflows
- **Cache Hit Rate**: Should be >80% on subsequent builds
- **Parallel Execution**: All platform builds should run concurrently

### Quality Metrics
- **Lint Validation**: Zero style violations in output
- **Build Success Rate**: 100% on valid code
- **Test Success Rate**: 100% on passing tests
- **Release Success Rate**: 100% when all previous jobs succeed

### Reliability Metrics
- **Fail-Fast Behavior**: Workflow stops immediately on first job failure
- **Artifact Integrity**: All artifacts correctly pass between jobs
- **Platform Consistency**: Identical behavior across Windows, Linux, macOS

## Troubleshooting

### Common Issues
1. **Lint job fails**: Check .editorconfig existence and rules
2. **Build cache miss**: Verify cache key generation
3. **Test job fails**: Check artifact download and test script permissions
4. **Release job fails**: Verify GitHub token permissions

### Debug Commands
```bash
# Check workflow syntax
yamllint .github/workflows/build-and-test.yml

# Verify artifact content
ls -la artifacts/zipper-*  # After build job

# Check cache content
# View cache action logs in GitHub Actions UI
```

## Rollback Plan

If the unified workflow fails:
```bash
# Restore original workflows
git checkout HEAD~1 -- .github/workflows/build.yml .github/workflows/test.yml
git rm .github/workflows/build-and-test.yml
git commit -m "rollback: restore original workflows"
git push origin master
```