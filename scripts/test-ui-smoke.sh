#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
DOTNET="${DOTNET:-$(command -v dotnet || true)}"
if [ -z "$DOTNET" ]; then DOTNET="$HOME/.dotnet/dotnet.exe"; fi
if [ ! -x "$DOTNET" ]; then echo "Set DOTNET to a .NET 8 SDK executable." >&2; exit 1; fi
# Each run gets its own evidence directory unless SMOKE_OUT is set explicitly, so a later run
# can never overwrite an earlier batch's logs or screenshots (the hardcoded experience-b path
# used to be clobbered by every rerun).
LOG="${SMOKE_OUT:-.mcp-probes/ui-smoke-$(date +%Y%m%d-%H%M%S)}"
mkdir -p "$LOG"
ARTIFACTS="$(pwd -W)/$LOG/artifacts"
"$DOTNET" build test/CrashScope.Ui.Smoke/CrashScope.Ui.Smoke.csproj -c Release --artifacts-path "$ARTIFACTS" > "$LOG/ui-smoke-build.log" 2>&1
status=0
"$DOTNET" "$LOG/artifacts/bin/CrashScope.Ui.Smoke/release/CrashScope.Ui.Smoke.dll" "$(pwd -W)" "$(pwd -W)/$LOG/screenshots" > "$LOG/ui-smoke.log" 2>&1 || status=$?
echo "evidence: $LOG"
cat "$LOG/ui-smoke.log"
exit "$status"
