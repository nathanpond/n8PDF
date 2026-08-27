#!/usr/bin/env bash
# Coverage-guided fuzzing of one entry point (#263). Linux only — macOS clang ships no libFuzzer.
# Usage: ./run.sh <image|font|deobfuscate|package|document> [extra libFuzzer flags...]
set -euo pipefail

TARGET="${1:?usage: ./run.sh <image|font|deobfuscate|package|document> [libFuzzer flags...]}"
shift || true

cd "$(dirname "$0")"
CONFIG=Release
LIBRARY="bin/$CONFIG/net10.0/n8PDF.dll"
HARNESS="bin/$CONFIG/net10.0/n8PDF.Fuzz.dll"

dotnet build -c "$CONFIG" >/dev/null
[ -d "corpus/$TARGET" ] || dotnet run -c "$CONFIG" --no-build -- seed

command -v sharpfuzz >/dev/null || {
  echo "sharpfuzz not found — dotnet tool install --global SharpFuzz.CommandLine"; exit 1; }
command -v libfuzzer-dotnet >/dev/null || {
  echo "libfuzzer-dotnet not found — build it from SharpFuzz's libfuzzer-dotnet.cc (see README)"; exit 1; }

# Instrument the library so libFuzzer sees its branches; re-run after every build.
sharpfuzz "$LIBRARY"

FUZZ_TARGET="$TARGET" exec libfuzzer-dotnet \
  --target_path="$(command -v dotnet)" \
  --target_arg="$HARNESS" \
  "$@" "corpus/$TARGET"
