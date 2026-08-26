#!/usr/bin/env bash
# Run the generated Playwright specs against a running build and print a summary.
# Usage: scripts/run_e2e.sh <base-url> [extra playwright args...]
set -euo pipefail

BASE_URL="${1:-http://localhost:3000}"
shift || true
export BASE_URL

if ! command -v npx >/dev/null 2>&1; then
  echo "npx not found - install Node.js before running the e2e specs." >&2
  exit 127
fi

if ! curl -sSf --max-time 10 "$BASE_URL" >/dev/null; then
  echo "Nothing responding at $BASE_URL - start the build first." >&2
  exit 1
fi

npx playwright test --reporter=list "$@"
