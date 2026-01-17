# Git Hooks Setup

This directory contains git hooks that can be installed to enforce code quality standards locally.

## Available Hooks

### pre-push
Runs before `git push` to ensure code coverage is >= 80%.

**To install:**
```bash
chmod +x .github/hooks/pre-push
cp .github/hooks/pre-push .git/hooks/pre-push
```

Or run the setup script:
```bash
./setup-hook.sh  # Linux/Mac
./setup-hook.bat # Windows
```

**To bypass the hook** (not recommended):
```bash
git push --no-verify
```

## Automatic Installation

The hooks can be automatically installed by running:
- Linux/Mac: `./setup-hook.sh`
- Windows: `./setup-hook.bat`

These scripts will copy the pre-push hook to `.git/hooks/` and make it executable.
