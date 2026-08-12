#!/usr/bin/env bash
# Type-check the project's C# without opening Unity.
#
# Unity holds an exclusive lock on the project, so the usual way to find out
# whether a change compiles is to close the editor and run a headless build -
# a poor trade for a check that should take seconds.
#
# Unity also generates .csproj files for IDEs, and those can be built directly.
# This compiles them and reports only the errors.
#
# ONE-TIME SETUP: in Unity, run
#   VexDesigner > Regenerate C# Project Files
# (Batch mode cannot do this: Unity's CurrentEditor is a no-op without a GUI.)
#
# The .csproj files are git-ignored, so each machine generates its own.
#
# Usage:  bash tools/typecheck.sh

set -uo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/Assembly-CSharp.csproj"

if [ ! -f "$PROJECT" ]; then
  echo "Assembly-CSharp.csproj not found."
  echo
  echo "In Unity, run: VexDesigner > Regenerate C# Project Files"
  echo "Then run this again."
  exit 2
fi

echo "Type-checking $(basename "$PROJECT")..."

# --no-restore because Unity's project files reference engine DLLs by absolute
# path and have no NuGet dependencies; a restore would only fail noisily.
OUTPUT="$(dotnet build "$PROJECT" \
  --no-restore \
  --nologo \
  --verbosity quiet \
  -consoleloggerparameters:NoSummary 2>&1)"
STATUS=$?

ERRORS="$(printf '%s\n' "$OUTPUT" | grep -E "error [A-Z]+[0-9]+" | sort -u)"

if [ -n "$ERRORS" ]; then
  echo
  echo "COMPILE ERRORS:"
  printf '%s\n' "$ERRORS"
  exit 1
fi

if [ $STATUS -ne 0 ]; then
  echo
  echo "Build failed without a recognisable error line:"
  printf '%s\n' "$OUTPUT" | tail -20
  exit 1
fi

echo "No compile errors."
