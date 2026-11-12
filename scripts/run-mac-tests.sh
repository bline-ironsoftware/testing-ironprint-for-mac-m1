#!/bin/bash
# Runs every macOS/iOS test in this repo on .NET 11 and .NET 10, then writes
# test-results/<timestamp>/summary.md plus one log per test.
#
# usage:
#   export SUITE_LICENSE_KEY="<your key>"
#   git pull && ./scripts/run-mac-tests.sh
#
# Needs: the .NET 11 RC3 SDK (see global.json) and, for the comparison, any
# .NET 10 SDK, each with the maui and macos workloads. Xcode is needed for the
# native macOS, Mac Catalyst and iOS tests; without it only the console runs.
set -u

REPO="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$REPO/test-results/$(date +%Y%m%d-%H%M%S)"
SUMMARY="$OUT/summary.md"
BUNDLE_ID=com.ironsoftware.ironprinttest
SCENARIOS="S1,S2,S3,S4,S5,S6,S7,S8,S9,S10,S11"
RUN_TIMEOUT=240

if [ -z "${SUITE_LICENSE_KEY:-}" ]; then
  echo 'Set the licence key first:  export SUITE_LICENSE_KEY="<your key>"'
  exit 1
fi
export IRONPRINT_LICENSE_KEY="$SUITE_LICENSE_KEY"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
mkdir -p "$OUT"

say()  { echo "$*" | tee -a "$SUMMARY"; }
row()  { echo "| $1 | $2 | $3 |" >> "$SUMMARY"; printf '  %-40s %s\n' "$1" "$2"; }
cell() { tr '\n' ' ' | sed 's/|/\\|/g; s/[[:space:]]\{1,\}/ /g' | cut -c1-260; }

first_error() {
  grep -m1 -E "ld: building for|Undefined symbols for|error MT[0-9]+|error NETSDK[0-9]+|error CS[0-9]+|error NU[0-9]+|error IL[0-9]+|A compatible .NET SDK was not found|error :" "$1" \
    | sed 's/^[[:space:]]*//' | cell
}

clean() {
  rm -rf "$1"/src/MauiTest/bin "$1"/src/MauiTest/obj \
         "$1"/src/IronPrintTest.Shared/bin "$1"/src/IronPrintTest.Shared/obj \
         "$1"/src/MacOSConsoleTest/bin "$1"/src/MacOSConsoleTest/obj \
         "$1"/src/ConsoleTest/bin "$1"/src/ConsoleTest/obj
}

# console_test <label> <root> <tfm>
console_test() {
  local log="$OUT/$1-console.log"
  clean "$2"
  ( cd "$2/src/ConsoleTest" && dotnet run "-p:TargetFrameworks=$3" ) > "$log" 2>&1
  local total lic
  total=$(grep -m1 -E "^=== [0-9]+ passed" "$log" | cell)
  lic=$(grep -m1 "^Licensed:" "$log" | cell)
  if [ -n "$total" ]; then row "$1 console ($3)" "RAN" "$lic; $total"
  else row "$1 console ($3)" "FAILED TO RUN" "$(first_error "$log")"; fi
}

# build_test <label> <name> <root> <project dir> <dotnet build args...>
build_test() {
  local label="$1" name="$2" root="$3" dir="$4"; shift 4
  local log="$OUT/$label-$name.log"
  clean "$root"
  ( cd "$root/$dir" && dotnet build -tl:off "$@" ) > "$log" 2>&1
  if [ $? -eq 0 ]; then row "$label $name" "BUILD OK" ""; return 0; fi
  row "$label $name" "BUILD FAILED" "$(first_error "$log")"; return 1
}

pick_simulator() {
  local list udid
  list=$(xcrun simctl list devices available 2>/dev/null)
  udid=$(echo "$list" | grep -E "iPhone|iPad" | grep "(Booted)" | grep -m1 -oE "[0-9A-F]{8}-([0-9A-F]{4}-){3}[0-9A-F]{12}")
  [ -z "$udid" ] && udid=$(echo "$list" | grep -E "iPhone" | grep -m1 -oE "[0-9A-F]{8}-([0-9A-F]{4}-){3}[0-9A-F]{12}")
  [ -z "$udid" ] && return 1
  xcrun simctl boot "$udid" >/dev/null 2>&1
  xcrun simctl bootstatus "$udid" -b >/dev/null 2>&1
  echo "$udid"
}

# ios_run_test <label> <root> <tfm> <extra build args...>
ios_run_test() {
  local label="$1" root="$2" tfm="$3"; shift 3
  build_test "$label" "ios-xamarin-build" "$root" src/MauiTest -f "$tfm" -p:IosUseXamarin=true "$@" || return
  local app udid log="$OUT/$label-ios-xamarin-run.log"
  app=$(find "$root/src/MauiTest/bin/Debug/$tfm" -maxdepth 2 -name MauiTest.app -type d | head -1)
  udid=$(pick_simulator) || { row "$label ios-xamarin-run" "NOT RUN" "no iOS simulator found"; return; }
  xcrun simctl install "$udid" "$app" > "$log" 2>&1 || { row "$label ios-xamarin-run" "NOT RUN" "install failed, see log"; return; }
  SIMCTL_CHILD_IRONPRINT_AUTORUN="$SCENARIOS" \
    xcrun simctl launch --console --terminate-running-process "$udid" "$BUNDLE_ID" >> "$log" 2>&1 &
  local pid=$! waited=0
  while [ $waited -lt $RUN_TIMEOUT ] && ! grep -q "Auto-run finished" "$log"; do sleep 2; waited=$((waited + 2)); done
  xcrun simctl io "$udid" screenshot "$OUT/$label-ios-xamarin-run.png" >/dev/null 2>&1
  xcrun simctl terminate "$udid" "$BUNDLE_ID" >/dev/null 2>&1
  kill $pid >/dev/null 2>&1
  local header status
  header=$(grep -m1 -oE "IronPrint [0-9.]+ on .*Licensed: [A-Za-z]+" "$log" | cell)
  if grep -q "Auto-run finished" "$log"; then status="RAN"; else status="TIMED OUT after ${RUN_TIMEOUT}s"; fi
  row "$label ios-xamarin-run" "$status" "$header"
  grep -E "\[APPLOG\].*\[(PASS|FAIL|SENT)" "$log" | sed 's/^.*\[APPLOG\] [0-9:]* /    /' >> "$SUMMARY"
}

# run_suite <label> <root> <tfm prefix> <extra Apple build args...>
run_suite() {
  local label="$1" root="$2" n="$3"; shift 3
  say ""
  say "## $label ($(cd "$root" && dotnet --version 2>&1 | head -1))"
  say ""
  say "| Test | Result | Detail |"
  say "|---|---|---|"
  console_test "$label" "$root" "$n"
  if [ "$HAS_XCODE" != yes ]; then
    row "$label Apple targets" "SKIPPED" "no Xcode on this Mac"
    return
  fi
  build_test "$label" "native-macos-build" "$root" src/MacOSConsoleTest
  build_test "$label" "maccatalyst-build" "$root" src/MauiTest -f "$n-maccatalyst" "$@"
  build_test "$label" "ios-plain-build" "$root" src/MauiTest -f "$n-ios" "$@"
  ios_run_test "$label" "$root" "$n-ios" "$@"
}

HAS_XCODE=no; xcodebuild -version >/dev/null 2>&1 && HAS_XCODE=yes
say "# IronPrint Mac test run, $(date '+%Y-%m-%d %H:%M')"
say ""
say "- Machine: $(uname -m), macOS $(sw_vers -productVersion)"
say "- Xcode: $( [ "$HAS_XCODE" = yes ] && xcodebuild -version | head -1 || echo none )"
say "- Repo commit: $(git -C "$REPO" rev-parse --short HEAD)"
say "- SDKs: $(dotnet --list-sdks 2>/dev/null | cut -d' ' -f1 | tr '\n' ' ')"
echo "Writing results to $OUT (this takes 15 to 30 minutes)"

run_suite "net11" "$REPO" "net11.0"

if dotnet --list-sdks 2>/dev/null | grep -q "^10\.0\."; then
  N10="$REPO/.net10"
  rm -rf "$N10"
  "$REPO/scripts/make-net10-copy.sh" "$N10" > "$OUT/net10-setup.log" 2>&1
  # .NET 10's Apple workloads require Xcode 27.2; SdkOnly linking gets past that check on 27.1.
  run_suite "net10" "$N10" "net10.0" -p:MtouchLink=SdkOnly
else
  say ""
  say "## net10"
  say ""
  say "Skipped: no .NET 10 SDK installed."
fi

echo
echo "Done. Send this file back: $SUMMARY"
echo
cat "$SUMMARY"
