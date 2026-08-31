#!/usr/bin/env bash
# Create a zip for client handoff (no git, no build artifacts, no secrets).
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"

OUT="${1:-MelloRoos-client.zip}"

zip -r "$OUT" . \
  -x '*/bin/*' \
  -x '*/obj/*' \
  -x 'out/*' \
  -x '.git/*' \
  -x '.github/*' \
  -x '.scratch/*' \
  -x '.DS_Store' \
  -x '*.swp' \
  -x '.env' \
  -x '.vs/*' \
  -x 'Reference-Docs/CFD 1, Series 2002 (1).pdf'

SIZE=$(du -h "$OUT" | cut -f1)
echo "Created $OUT ($SIZE)"
echo "Send via file share if over email size limit (~25 MB)."
