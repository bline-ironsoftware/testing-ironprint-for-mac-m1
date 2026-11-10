# ironprint-net11-mac

Checks IronPrint 2026.11.2-prerelease compatibility with .NET 11 RC3 SDK
(`11.0.100-rc.3.26522.148`, pinned in `global.json`) on macOS and MAUI
(MacCatalyst / iOS / Android). Windows was already covered separately.

## Known constraints (read before running)

- **Version**: the spec that produced this repo named "IronPrint 2026.11.1",
  but only `2026.11.2-prerelease` was resolvable from the feed/local NuGet
  cache at the time this was built. All three projects pin
  `IronPrintTestVersion` to `2026.11.2-prerelease` (set as an MSBuild property
  in each `.csproj`) -- override it with `-p:IronPrintTestVersion=2026.11.1`
  if that version turns out to be available and you want to test it instead.
- **`Printer.GetPrinterNamesAsync()` / `GetPrinterNames()` do not exist at all**
  in the MacCatalyst, iOS, or Android builds of IronPrint (confirmed by
  inspecting the installed package's `IronPrint.xml`/`IronPrint.dll` per
  TargetFramework -- present only in the desktop/Windows builds). So:
  - On **ConsoleTest** (net11.0, desktop), S1 behaves as originally specced.
  - On **MauiTest**, S1 will always report FAIL with a `PlatformNotSupportedException`
    on all three targets -- that's expected, not a bug in this harness. There is
    no printer picker backed by that API on MauiTest; instead there's a plain
    printer-name text field (blank = let IronPrint use the OS default).

## Repo layout

```
src/
  IronPrintTest.Shared/   Scenario definitions (S1-S7) + results.md/json writer,
                          shared by both test apps. Multi-targets net11.0 (for
                          ConsoleTest) and net11.0-maccatalyst/-ios/-android
                          (for MauiTest).
  ConsoleTest/            net11.0 console app. Runs all 7 scenarios in order.
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

### 2. Install the .NET 11 RC3 SDK + MAUI workload

```bash
curl -sSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --version 11.0.100-rc.3.26522.148
export PATH="$HOME/.dotnet:$PATH"   # or wherever --install-dir pointed, if you used one

dotnet workload install maui

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
export IRONPRINT_LICENSE_KEY="..."   # never hard-code this
```

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
(`bin/<config>/net11.0/`).

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

## Report

Fill this in (or paste `results.md`'s table) once you've run both apps and
checked the physical output of S2-S7:

| Platform | Build | Printer list (S1) | PDF path (S2) | PDF bytes (S3) | JPG (S4) | Grayscale (S5) | 2 copies (S6) | Landscape (S7) |
|----------|-------|--------------------|----------------|-----------------|----------|-----------------|----------------|-----------------|
| Console (macOS) | | | | | | | | |
| Mac Catalyst | | | | | | | | |
| iOS simulator | | | | | | | | |
| Android emulator | | | | | | | | |
