# Git Hooks Setup

This directory contains git hooks for local code quality enforcement.

## Available Hooks

### pre-commit
Runs on `git commit`:
1. **bd sync** — flush pending `.beads` changes
2. **dotnet format** — auto-fix code style (fails if changes needed)
3. **Unit tests** — run all 374 unit tests (~1.6s)

### pre-push
Runs on `git push`:
1. **Unit tests** — fast gate (~1.6s)
2. **Basic E2E smoke suite** — 5 representative test cases (~6s)
   - PDF generation, EML with attachments, TIFF with folders
   - Load file in zip, Bates numbering

Full E2E suite + coverage checks run in CI only.

## Installation

```bash
./setup-hook.sh  # Linux/Mac
./setup-hook.bat # Windows
```

## Bypass (not recommended)

```bash
git commit --no-verify
git push --no-verify
```
