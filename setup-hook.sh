#!/bin/bash

# This script sets up a Git pre-commit hook to run the test suite.

HOOK_DIR=".git/hooks"
HOOK_FILE="$HOOK_DIR/pre-commit"

# Create the hooks directory if it doesn't exist.
mkdir -p "$HOOK_DIR"

# Create the pre-commit hook.
cat > "$HOOK_FILE" << EOL
#!/bin/bash

# Run the test suite.
./tests/run-tests.sh

# If the tests fail, exit with a non-zero status to prevent the commit.
if [ \$? -ne 0 ]; then
  echo "Tests failed. Aborting commit."
  exit 1
fi

exit 0
EOL

# Make the hook executable.
chmod +x "$HOOK_FILE"

echo "Pre-commit hook created successfully."
