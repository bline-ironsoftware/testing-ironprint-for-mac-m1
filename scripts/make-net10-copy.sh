#!/bin/bash
# Creates a .NET 10 copy of this repo for comparison runs: same code, net10.0 targets.
# usage: scripts/make-net10-copy.sh [destination]   (default: ../ironprint-net10)
set -e
src="$(cd "$(dirname "$0")/.." && pwd)"
dst="${1:-$src/../ironprint-net10}"
if [ -e "$dst" ]; then echo "$dst already exists; delete it or pass another path"; exit 1; fi
mkdir -p "$dst"
git -C "$src" archive HEAD | tar -x -C "$dst"
cd "$dst"
printf '{\n  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }\n}\n' > global.json
perl -pi -e 's/net11\.0/net10.0/g' src/*/*.csproj
perl -pi -e 's/IronPrint \.NET 11/IronPrint .NET 10/g' src/MauiTest/MainPage.xaml src/MauiTest/MauiTest.csproj
echo "Created $dst"
dotnet --version
