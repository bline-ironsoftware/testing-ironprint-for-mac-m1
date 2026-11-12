# ironprint-net11-mac

Checks IronPrint 2026.11.1 compatibility with .NET 11 RC3 SDK
(`11.0.100-rc.3.26522.148`, pinned in `global.json`) on macOS and MAUI
(MacCatalyst / iOS / Android). Windows was already covered separately.

## Known constraints (read before running)

- **Version**: all four apps pin `IronPrintTestVersion` (and, for Android/iOS,
  `IronPrintXamarinTestVersion`) to `2026.11.1` -- MSBuild properties in
  `IronPrintTest.Shared.csproj`, consumed by `ConsoleTest`, `MacOSConsoleTest`,
  and `MauiTest` via `ProjectReference` -- override with
  `-p:IronPrintTestVersion=<other version>` if you need to test a different
  one. Note: as of this writing, the published `2026.11.1` package ships
  `net8.0` / `net8.0-maccatalyst11.0` / `net8.0-ios10.0` / `net8.0-android21` /
  `net8.0-macos10.14` / `net8.0-windows7.0` assets (no `net11.0`-specific
  build yet) -- NuGet's standard same-family roll-down resolves these fine
  under our `net11.0`/`net11.0-macos`/`net11.0-maccatalyst`/etc. TargetFrameworks.
  `net11.0-android` instead uses `IronPrint.Xamarin`; `net11.0-ios` uses plain
  `IronPrint` by default (`-p:IosUseXamarin=true` switches it) -- see below.
- **Printing requires an OS-specific TargetFramework outside Windows.**
  `ConsoleTest` targets plain `net11.0` (no OS suffix), and on Windows that's
  fine -- but on macOS, IronPrint falls back to `System.Drawing.Printing`,
  which has no real printing backend in .NET's cross-platform build (not even
  with `libgdiplus` installed). Every scenario on `ConsoleTest` run from
  macOS will report `FAIL` with a `PlatformNotSupportedException` telling you
  to use a TFM like `net7.0-macos`/`net7.0-maccatalyst`/`net7.0-android` --
  this is IronPrint's own documented limitation, not a bug in this harness.
  The real Mac print-path test is `MauiTest` on `net11.0-maccatalyst`, which
  **is** one of those supported OS-specific TFMs.
- **MacCatalyst/iOS builds on this SDK need Xcode 27.0, not just any Xcode.**
  The `net11.0-maccatalyst`/`net11.0-ios` workload packs bundled with
  `11.0.100-rc.3.26522.148` are versioned `...net11.0_27.0`, and MSBuild
  hard-fails before compiling anything if `xcodebuild -version` reports an
  older Xcode (confirmed on a real Mac mini: Xcode 26.1.1 installed ->
  immediate `error : This version of .NET for MacCatalyst ... requires
  Xcode 27.0`). Update Xcode via the App Store (or
  https://developer.apple.com/download/all/) to 27.x, then
  `sudo xcode-select -s /Applications/Xcode.app/Contents/Developer` and
  `sudo xcodebuild -license accept` again, before attempting MacCatalyst or
  iOS. Android is unaffected -- it has no Xcode dependency.
- **`Printer.GetPrinterNamesAsync()` / `GetPrinterNames()` do not exist at all**
  outside true Windows desktop builds -- confirmed absent from macOS,
  MacCatalyst, iOS, and Android alike (every mention of it in those
  TargetFrameworks' XML docs is an unresolved `!:Printer.GetPrinterNamesAsync()`
  doc-comment cross-reference, not an actual member; only present in
  `net11.0`/`net11.0-windows`). So:
  - On **ConsoleTest** (Windows only) and **MacOSConsoleTest**, S1 will report
    FAIL with a `PlatformNotSupportedException` when run on macOS -- expected,
    not a bug in this harness.
  - On **MauiTest**, same thing on all three targets. There is no printer
    picker backed by that API on MauiTest; instead there's a plain
    printer-name text field (blank = let IronPrint use the OS default).
- **IronPrint 2026.11.1 cannot build for Mac Catalyst at all -- confirmed
  packaging gap, not fixable from this repo.** The package's native
  `IronPrintInterop.dylib` ships for `ios-arm64`, `iossimulator-x64`,
  `osx-arm64`, and `osx-x64` -- but **not** `maccatalyst-arm64`/`maccatalyst-x64`.
  .NET's RID graph falls `maccatalyst-arm64` back to `ios-arm64` (not
  `osx-arm64`, which would actually be ABI-compatible), so the linker pulls in
  the iOS-built dylib and fails: `ld: building for 'macCatalyst', but linking
  in dylib (...IronPrintInterop.dylib) built for 'iOS'`. There is no
  MSBuild property that overrides which native asset NuGet selects here, and
  we have no source for the dylib to rebuild it ourselves. **This is the
  actual, final answer for MacCatalyst: it does not work, due to a missing
  native asset in the IronPrint package itself** -- report to IronSoftware.
  `MacOSConsoleTest` (a plain, non-MAUI `net11.0-macos` console app) exists
  specifically to test IronPrint on native Mac independently of this gap,
  since its RID resolves straight to `osx-arm64`, which does have a correct
  native asset.
- **Android: `Printer.Initialize(Android.Content.Context)` must be called
  before any other `Printer` method** -- confirmed via a real emulator run
  (`NullReferenceException: MainActivity is undefined` without it); see
  `Platforms/Android/MainActivity.cs`. **The file-path overload
  `PrintAsync(string path, ...)` fails on Android** with
  `FileNotFoundException: Resource not found: 0`, thrown inside IronPrint
  (`Iron.Print.Extensions...` directly under `Printer.PrintAsync(String,
  PrintSettings)`), for PDF and JPG. **The `byte[]` overload works**: S3
  opens the Android print dialog ("Save as PDF") with a correct preview
  (licensed run, `IronPrint.Xamarin`, Android 15 emulator). Workaround: read
  the file and pass the bytes (PDF only). S8-S11 repeat S4-S7 through the
  `byte[]` overload: JPG bytes fail with `InvalidDataException: Failed to
  decode bitmap resource` (S8, S9, S11; the JPEG is valid and intact in the
  sandbox), and S10 (2 copies, PDF) opens the dialog but shows "Copies: 1",
  which is inconclusive because "Save as PDF" is the only printer.
- **Android on .NET 10 (comparison run): no .NET 11 regression.** The print
  dialog appears for the same cases (PDF as `byte[]`) and no others. On
  .NET 10 every `PrintAsync` call additionally throws
  `PlatformNotSupportedException` from
  `GCSettings.set_LargeObjectHeapCompactionMode` inside IronPrint, and in a
  default Debug build `License.LicenseKey` did not return within two minutes
  (about 3 seconds with `-p:UseInterpreter=false`). Details in
  `BUG_REPORT.md`.
- **Mac and iOS on .NET 10 (comparison run): no .NET 11 regression.**
  Native macOS, Mac Catalyst and the iOS simulator (plain IronPrint) fail
  with the same link errors on .NET 10 (SDK 10.0.404). With Xcode 27.1 the
  .NET 10 Catalyst/iOS builds need `-p:MtouchLink=SdkOnly` to get past the
  Xcode 27.2 requirement (`MT0180`).
- **`net11.0-macos` also fails to build -- confirmed, different root cause
  than MacCatalyst, also not fixable from this repo.** `clang++` fails
  linking with `Undefined symbols for architecture arm64: "_C_CancelFunction"`,
  `"_C_CloseSession"`, etc. -- the native PKCS#11 (smart-card/HSM) symbols
  that `Pkcs11Interop` (one of IronPrint's own dependencies, used for license
  activation against hardware security tokens) expects to resolve at build
  time. `UseInterpreter=true` (the standard .NET for Mac/iOS workaround for
  dependencies that can't be fully AOT-linked) was tried and confirmed to
  have **no effect** -- identical error either way, since this is the final
  native-to-native link step that bundles every referenced native library
  into one Mach-O executable, which happens regardless of managed IL
  execution mode. So: **neither native macOS nor MacCatalyst currently
  builds at all with IronPrint 2026.11.1 on .NET 11 RC3**, for two distinct,
  unrelated reasons (missing native asset vs. an unresolvable third-party
  native dependency) -- both worth reporting to IronSoftware as separate
  issues.
- **iOS needs the full UIKit scene-lifecycle setup (not a MAUI bug).** The
  iOS 27 SDK refuses to launch apps without it (`UIScene life cycle is
  required for apps built with this SDK`). A manifest with only
  `UIApplicationSupportsMultipleScenes` launches to a black screen; the fix,
  per Microsoft's migration guide
  (https://devblogs.microsoft.com/dotnet/migrate-dotnet-maui-apps-uikit-scene-lifecycle/),
  is a `SceneDelegate : MauiUISceneDelegate` plus a `UISceneConfigurations`
  entry in `Info.plist` naming it. With that in place the MAUI UI renders
  (confirmed on an iPad simulator, iOS 27.0, Apple Silicon Mac).
- **iOS: IronPrint 2026.11.1 does not print on the simulator with either
  package -- confirmed, three distinct failures:**
  - `IronPrint.Xamarin` (`-p:IosUseXamarin=true`): builds and runs, but NuGet
    resolves it to `lib/netstandard2.0` (not `lib/xamarin.ios10`; confirmed in
    `project.assets.json`) and S2-S7 all throw `NotImplementedException`
    immediately. Confirmed with a licensed run (`Licensed: True`).
  - Plain `IronPrint`, `iossimulator-arm64` (the default on Apple Silicon):
    link fails, `ld: building for 'iOS-simulator', but linking in dylib
    (...IronPrintInterop.dylib) built for 'iOS'`. The package has no
    `iossimulator-arm64` native asset, only `ios-arm64` and `iossimulator-x64`.
  - Plain `IronPrint`, `-r iossimulator-x64`: link fails with the same
    `Pkcs11Interop` undefined `_C_*` symbols as `net11.0-macos`. (This Mac
    also has no x64 simulator devices to run it on.)
  Not tested: a physical iPhone/iPad (`ios-arm64`).

## Repo layout

```
src/
  IronPrintTest.Shared/   Scenario definitions (S1-S7) + results.md/json writer,
                          shared by all three test apps. Multi-targets net11.0,
                          net11.0-macos, net11.0-maccatalyst, net11.0-ios, and
                          net11.0-android.
  ConsoleTest/            net11.0 console app (generic, no OS suffix). Runs all
                          7 scenarios in order. Printing does NOT work here on
                          macOS -- see Known Constraints.
  MacOSConsoleTest/       net11.0-macos console app (native Mac, not MAUI).
                          Same 7 scenarios -- exists to test IronPrint on Mac
                          independently of the MacCatalyst packaging gap.
  MauiTest/               net11.0-maccatalyst;net11.0-ios;net11.0-android.
                          One page, one button per scenario, a log, and a
                          printer-name field.
TestFiles/
  sample.pdf              2-page colour PDF (see TestFiles/README.md)
  sample.jpg              colour JPG with colour bars + orientation markers
```

## Mac setup

Run these on the M1 Mac mini before anything else.

### 1. Checks

```bash
sw_vers
xcodebuild -version
dotnet --list-sdks
dotnet workload list
lpstat -p
```

`lpstat -p` lists configured printers (CUPS) -- confirms there's at least one
printer (real or `ippeveprinter`, see below) before you start.

**`xcodebuild -version` must report Xcode 27.0 or newer** for the MacCatalyst
and iOS targets to build at all on this SDK -- see the Known Constraints
section below if it doesn't.

### 2. Install the .NET 11 RC3 SDK + MAUI workload

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --version 11.0.100-rc.3.26522.148
export PATH="$HOME/.dotnet:$PATH"   # or wherever --install-dir pointed, if you used one

dotnet workload install maui
dotnet workload install macos   # needed for MacOSConsoleTest (net11.0-macos)

xcode-select --install          # if Xcode command line tools aren't already present
sudo xcodebuild -license accept  # accept the Xcode license, needed for iOS/MacCatalyst builds
```

### 3. GDI+ errors

IronPrint (like other IronSoftware libraries) can need `libgdiplus` for
image/graphics handling outside Windows:

```bash
brew install mono-libgdiplus
```

### 4. Clone and configure

```bash
git clone <this-repo-url>
cd ironprint-net11-mac
export IRONPRINT_LICENSE_KEY="..."   # used by ConsoleTest -- never hard-code this
export SUITE_LICENSE_KEY="..."       # used by MauiTest -- never hard-code this
```

Note the two apps read different env var names for the license key:
`ConsoleTest` uses `IRONPRINT_LICENSE_KEY`, `MauiTest` uses `SUITE_LICENSE_KEY`.

Drop the real test assets into `TestFiles/` if they aren't already there --
see `TestFiles/README.md` for what each file should look like.

## Running it

### Console

```bash
cd src/ConsoleTest
dotnet run
# or, to pick a specific printer:
dotnet run -- --printer "Your Printer Name"
```

Writes `results.md` and `results.json` next to the built output
(`bin/<config>/net11.0/`). Expect all 7 scenarios to `FAIL` on macOS -- see
Known Constraints; this isn't a harness bug.

### Native macOS console

```bash
cd src/MacOSConsoleTest
dotnet run
```

This is the one most likely to show real printing working on Mac, since its
RID (`osx-arm64`) has a correctly-built native IronPrint asset -- unlike
MacCatalyst, which does not (see Known Constraints).

### Mac Catalyst

```bash
cd src/MauiTest
dotnet build -t:Run -f net11.0-maccatalyst
```

### iOS simulator

```bash
cd src/MauiTest
dotnet build -t:Run -f net11.0-ios
```

If you need to print from the iOS simulator and don't have a real AirPrint
printer on the network, start a fake one:

```bash
brew install cups   # ippeveprinter ships with CUPS on recent macOS/Homebrew
ippeveprinter -k -f application/pdf,image/jpeg "TestPrinter"
```

Leave that running in its own terminal tab while the simulator is up.

### Android

```bash
cd src/MauiTest
dotnet build -t:InstallAndroidDependencies -f net11.0-android
# start an arm64 emulator (e.g. via Android Studio's Device Manager) first, then:
dotnet build -t:Run -f net11.0-android
```

On a Windows PC (no `macos` workload), restrict the build to Android, and
point at the Android SDK if it is not in the default location:

```bash
dotnet build -t:Install -f net11.0-android -p:TargetFrameworks=net11.0-android "-p:AndroidSdkDirectory=%LOCALAPPDATA%\Android\Sdk"
```

Failed scenarios write their full stack trace to logcat (tag `DOTNET`).

To test the plain `IronPrint` package on Android instead of `IronPrint.Xamarin`,
add `-p:AndroidUseXamarin=false`.

### MAUI on Windows

```bash
cd src/MauiTest
dotnet build -f net11.0-windows10.0.19041.0 -p:TargetFrameworks=net11.0-windows10.0.19041.0
bin/Debug/net11.0-windows10.0.19041.0/win-x64/MauiTest.exe
```

Type a printer name (for example `Microsoft Print to PDF`) and click the
scenario buttons. For an unattended run, set `IRONPRINT_AUTORUN=S1,S2,...`,
`IRONPRINT_PRINTER` and `IRONPRINT_AUTORUN_LOG` (a log file path) before
starting the exe.

### Run every Mac test in one go

```bash
export SUITE_LICENSE_KEY="<your key>"
git pull && ./scripts/run-mac-tests.sh
```

Runs the console app, native macOS build, Mac Catalyst build, iOS build
(plain IronPrint) and an unattended iOS simulator run with IronPrint.Xamarin
(all 11 scenarios), first on .NET 11 and then on .NET 10 if a .NET 10 SDK is
installed. Results go to `test-results/<timestamp>/summary.md`, with one log
per test. Without Xcode only the console app runs.

### .NET 10 comparison

`scripts/make-net10-copy.sh` creates a sibling copy of the repo with every
target switched to `net10.0` and `global.json` pointing at a .NET 10 SDK.
Run the same commands in that copy.

## Report

Fill this in (or paste `results.md`'s table) once you've run both apps and
checked the physical output of S2-S7:

| Platform | Build | Printer list (S1) | PDF path (S2) | PDF bytes (S3) | JPG (S4) | Grayscale (S5) | 2 copies (S6) | Landscape (S7) |
|----------|-------|--------------------|----------------|-----------------|----------|-----------------|----------------|-----------------|
| Console (macOS Apple Silicon, generic net11.0) | n/a | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) |
| Console (macOS Intel x64, macOS 15.8.3, generic net11.0, licensed) | n/a | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) | FAIL (confirmed) |
| Native macOS console (net11.0-macos) | FAIL (confirmed -- Pkcs11Interop native link error, see Known Constraints) | n/a | n/a | n/a | n/a | n/a | n/a | n/a |
| Mac Catalyst | FAIL (confirmed -- missing native asset, see Known Constraints) | n/a | n/a | n/a | n/a | n/a | n/a | n/a |
| iOS simulator, plain IronPrint | FAIL (confirmed -- no `iossimulator-arm64` native asset; `iossimulator-x64` hits the Pkcs11Interop link error) | n/a | n/a | n/a | n/a | n/a | n/a | n/a |
| iOS simulator, IronPrint.Xamarin (licensed) | OK | FAIL (expected -- API absent) | FAIL (NotImplementedException) | FAIL (same) | FAIL (same) | FAIL (same) | FAIL (same) | FAIL (same) |
| Android emulator (IronPrint.Xamarin, licensed) | OK | FAIL (expected -- API absent) | FAIL (Resource not found: 0) | SENT (print dialog opens, preview correct) | FAIL (Resource not found: 0) | FAIL (same) | FAIL (same) | FAIL (same) |
| Android emulator (plain IronPrint, licensed) | OK | same as IronPrint.Xamarin | same | same | same | same | same | same |

Android `byte[]` scenarios (not in the table): S8 JPG, S9 grayscale JPG, S11 landscape JPG all FAIL (`Failed to decode bitmap resource`); S10 2 copies PDF opens the print dialog with "Copies: 1" (inconclusive).
