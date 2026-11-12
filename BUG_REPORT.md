# IronPrint 2026.11.1 / .NET 11 RC3 compatibility findings

Four confirmed, reproducible issues found while testing whether IronPrint
2026.11.1 works with .NET 11 RC3 (`11.0.100-rc.3.26522.148`) on macOS
(native, Mac Catalyst) and mobile (Android, iOS). All four belong to
IronSoftware (IronPrint itself). An earlier draft attributed the iOS
problem to .NET MAUI; that was wrong, see the note at the end of section 4.
Full repro repo:
https://github.com/bline-ironsoftware/testing-ironprint-for-mac-m1

---

## Test coverage

- **Apple Silicon Mac mini (macOS, Xcode 27.1):** plain console, native
  macOS, Mac Catalyst and iOS simulator (plain IronPrint and
  IronPrint.Xamarin).
- **Windows PC:** Android 15 emulator (IronPrint.Xamarin and plain
  IronPrint, licensed).
- **Windows itself is out of scope** (covered separately). The MAUI test
  app does build and run on `net11.0-windows10.0.19041.0`, and listing
  printers and printing the PDF to "Microsoft Print to PDF" worked, but no
  further Windows scenarios were run.
- **Intel Mac (macOS 15.8.3, no Xcode):** plain console only, on .NET 11
  and .NET 10 (SDK 10.0.404), licensed. On both, all 11 scenarios fail with
  the same `PlatformNotSupportedException` as on Apple Silicon, which is
  IronPrint's documented limitation for a plain target outside Windows.
  Native macOS, Mac Catalyst and iOS could not be built on Intel because
  Xcode is not installed there.
- **.NET 10 comparison:** Android, the console app (Apple Silicon and Intel), native macOS, Mac Catalyst and iOS (both packages); see the section after finding 3. No .NET 11 regressions found.
- **Not tested anywhere:** a physical iOS device, and a real (non
  "Save as PDF") printer.

---

## 1. IronSoftware -- Mac Catalyst: missing native asset (`maccatalyst-arm64`/`maccatalyst-x64`)

**Package:** IronPrint 2026.11.1
**Target:** `net11.0-maccatalyst`, Apple Silicon (`maccatalyst-arm64`)
**Environment:** .NET 11 RC3 SDK `11.0.100-rc.3.26522.148`, Xcode 27.1, macOS, M1 Mac mini

**Repro:** reference IronPrint 2026.11.1 from a `net11.0-maccatalyst`
project and build.

**Actual result:**
```
error: clang++ exited with code 1:
ld: building for 'macCatalyst', but linking in dylib
(.../obj/.../nativelibraries/Contents/MonoBundle/IronPrintInterop.dylib)
built for 'iOS'
```

**Root cause:** the package's native runtime assets for `IronPrintInterop`
cover `ios-arm64`, `iossimulator-x64`, `osx-arm64`, `osx-x64`, `win-x64`,
and `win-x86` -- but **no `maccatalyst-arm64` or `maccatalyst-x64`** at
all (confirmed by listing the `.nupkg`'s `runtimes/` folder directly).
.NET's RID compatibility graph falls `maccatalyst-arm64` back to
`ios-arm64` (not `osx-arm64`, which would actually be ABI-compatible),
so the build picks the iOS-built dylib, which the linker correctly
rejects since Mac Catalyst binaries use a different target triple
(`arm64-apple-ios-macabi`) than plain iOS (`arm64-apple-ios`).

**Impact:** IronPrint cannot be used in a Mac Catalyst app at all on this
SDK -- it fails at build time, every time.

**Suggested fix:** publish a genuine `runtimes/maccatalyst-arm64/native/IronPrintInterop.dylib`
(and `maccatalyst-x64`) built with the Mac Catalyst target triple, the
same way the `osx-arm64`/`osx-x64` variants are already built correctly
for native macOS.

---

## 2. IronSoftware -- native macOS: `Pkcs11Interop` native link failure

**Package:** IronPrint 2026.11.1
**Target:** `net11.0-macos` (native, non-Catalyst), `osx-arm64`
**Environment:** same as above

**Repro:** reference IronPrint 2026.11.1 from a plain `net11.0-macos`
console app (not MAUI) and build.

**Actual result:**
```
error: clang++ exited with code 1:
Undefined symbols for architecture arm64:
  "_C_CancelFunction", referenced from: <initial-undefines>
  "_C_CloseAllSessions", referenced from: <initial-undefines>
  "_C_CloseSession", referenced from: <initial-undefines>
  "_C_CopyObject", referenced from: <initial-undefines>
  "_C_CreateObject", referenced from: <initial-undefines>
  "_C_Decrypt", referenced from: <initial-undefines>
  ... (full list is the standard PKCS#11 C_* function table)
```

**Root cause:** IronPrint depends on `Pkcs11Interop` (used for PKCS#11
smart-card/HSM-based license activation). Its native PKCS#11 function
symbols are declared `extern`, expecting to resolve dynamically at
runtime once an actual PKCS#11 provider module is loaded via `dlopen` --
a pattern that works fine on Windows/Linux/plain desktop macOS, but fails
under Apple's mandatory static-link-everything-into-one-Mach-O-executable
model for distributable Mac/iOS/Catalyst app bundles.

**Ruled out:** `UseInterpreter=true` (the standard .NET for Mac/iOS
workaround for dependencies that can't be fully AOT-linked) was tried and
confirmed to have **zero effect** -- the build log explicitly states
`"UseInterpreter" has no effect when not using the Mono runtime (for
instance when using CoreCLR)`, and this target uses CoreCLR. This is also
the final native *linker* step bundling every native library into one
executable, which happens regardless of managed IL execution mode, so no
managed-runtime-level setting can route around it.

**Impact:** IronPrint cannot be used in any native macOS app on this SDK
at all -- it fails at build/link time, every time, regardless of whether
the app ever calls any PKCS#11-related functionality.

**Suggested fix:** make the PKCS#11/HSM license-activation code path
optional (feature-flagged out of the default build), or ship it as a
separate opt-in package/assembly, so apps that don't need HSM-based
licensing don't pull in a dependency that can't be statically linked for
Apple's app-bundle model.

---

## 3. IronSoftware -- Android: only PDF-as-`byte[]` prints; file paths fail with `Resource not found: 0`, JPG bytes fail to decode

**Package:** IronPrint.Xamarin 2026.11.1 and plain IronPrint 2026.11.1 (`-p:AndroidUseXamarin=false`). Both were run through all 11 scenarios, licensed, with identical results
**Target:** `net11.0-android`, Android 15 (API 35) emulator, licensed (`Licensed: True`)
**Environment:** .NET 11 RC3 SDK, Android SDK platform 37 (Microsoft.Android.Sdk.Windows 37.0.0-preview.6.59)

**Repro:**
```csharp
Printer.Initialize(this); // in MainActivity.OnCreate, after base.OnCreate
await Printer.PrintAsync(pdfPath, new PrintSettings());            // FAILS
await Printer.PrintAsync(File.ReadAllBytes(pdfPath), new PrintSettings()); // works
```

**Actual result (path overload, PDF and JPG, with or without grayscale /
copies / landscape settings):**
```
System.IO.FileNotFoundException: Resource not found: 0
   at Iron.Print.Extensions.geiala.kqnhlv.MoveNext()
   at IronPrint.Printer.PrintAsync(String path, PrintSettings settings)
```
The exception is thrown inside IronPrint's own code, directly under the
path overload.

**Works:** `PrintAsync(byte[], PrintSettings)` with the same PDF opens the
Android system print dialog ("Save as PDF") with a correct 2-page preview.

**Ruled out:**
- Not a missing file: the same file is read successfully with
  `File.ReadAllBytes` and prints through the `byte[]` overload.
- Not licensing: the run was licensed.
- `Printer.Initialize(Android.Content.Context)` is required (without it:
  `NullReferenceException: MainActivity is undefined`) but does not change
  this error.

**Not determined:** why the path overload fails. The message suggests an
Android resource-ID lookup returning 0, but that is a guess; the code is
obfuscated and was not decompiled.

**JPG as `byte[]` also fails** (S8, S9, S11: plain, grayscale, landscape):
```
System.IO.InvalidDataException: Failed to decode bitmap resource
   at Iron.Print.Extensions.geiale.vfopli(Byte[] nwgeho, Options nwgehp)
   at Iron.Print.Extensions.geiala.kqnhlw.MoveNext()
   at IronPrint.Printer.PrintAsync(Byte[] fileData, PrintSettings settings)
```
The file is a valid baseline RGB JPEG (1600x1000), and the copy in the app
sandbox has the same MD5 as the original. So no image could be printed on
Android by either overload, and the grayscale and landscape settings could
not be exercised.

**`NumberOfCopies = 2` (S10, PDF as `byte[]`):** the print dialog opens with
a correct preview but shows "Copies: 1". Inconclusive: the only printer on
the emulator is "Save as PDF", which may not accept a copy count.

**Impact:** on Android only PDFs passed as `byte[]` reach the print dialog;
file paths and images do not.
**Workaround (PDF only):** read the file and pass the bytes.

---

## .NET 10 comparison

The same Android scenarios were run against .NET 10 (SDK 10.0.301,
`net10.0-android`, MAUI 10.0.20, same emulator, same IronPrint.Xamarin
2026.11.1, licensed) to separate .NET 11 regressions from existing behaviour.

**Result: none of the Android failures in section 3 is a .NET 11
regression.** The print dialog appears for exactly the same cases on both
(PDF as `byte[]`: S3 and S10) and for nothing else.

**.NET 10 behaves worse in two ways:**

- Every `PrintAsync` call throws, including the two that open the print
  dialog:
  ```
  System.PlatformNotSupportedException: Operation is not supported on this platform.
     at System.Runtime.GCSettings.set_LargeObjectHeapCompactionMode(GCLargeObjectHeapCompactionMode value)
     at IronPrint.Printer.vtivcz()
     at IronPrint.Printer.PrintAsync(Byte[] fileData, PrintSettings settings)
  ```
  This exception replaces the `Resource not found: 0` and `Failed to decode
  bitmap resource` errors seen on .NET 11, so those underlying errors could
  not be observed on .NET 10.
- In a default Debug build, `License.LicenseKey = ...` did not return: a
  thread stayed at 100% CPU for over two minutes and the app sat on the
  splash screen. With `-p:UseInterpreter=false` the same call took about
  3 seconds. The comparison run used that setting, and set the key on a
  background thread.

**Mac and iOS on .NET 10** (SDK 10.0.404, MAUI 10.0.110, Apple Silicon
Mac mini, Xcode 27.1): nothing that fails on .NET 11 works on .NET 10, so
none of these is a .NET 11 regression. The licence-key hang only happens on
.NET 10.

| Target | .NET 10 result |
|---|---|
| Console app (`net10.0`, .NET 10.0.15 runtime, licensed) | Same as .NET 11: all 11 scenarios fail with `PlatformNotSupportedException` ("To use Print/PrintAsync outside windows you need to set a more specific OS TargetFramework") |
| Native macOS (`net10.0-macos`) | Same `Pkcs11Interop` link error: `Undefined symbols for architecture arm64: "_C_CancelFunction", ...` |
| Mac Catalyst (`net10.0-maccatalyst`) | Same error: `ld: building for 'macCatalyst', but linking in dylib (...IronPrintInterop.dylib) built for 'iOS'` |
| iOS simulator, plain IronPrint (`net10.0-ios`) | Same error: `ld: building for 'iOS-simulator', but linking in dylib (...IronPrintInterop.dylib) built for 'iOS'` |
| iOS simulator, IronPrint.Xamarin (`net10.0-ios`, licensed) | Builds. In a default Debug build the app freezes while setting `License.LicenseKey` (same as Android on .NET 10). With `-p:UseInterpreter=false` the key is set in 0.2 s, then every print scenario fails with `PlatformNotSupportedException: Operation is not supported on this platform` (the same message as Android on .NET 10; the iOS stack trace was not inspected). On .NET 11 the same scenarios fail with `NotImplementedException` instead |

The .NET 10 Mac Catalyst and iOS workloads require Xcode 27.2, so with
Xcode 27.1 a default build stops earlier with `MT0180: This version of
Microsoft.iOS requires the iOS 27.2 SDK`. Building with
`-p:MtouchLink=SdkOnly` gets past that check, and the results above come
from those builds. That is an environment limitation of the test Mac, not
an IronPrint issue.

---

## 4. IronSoftware -- iOS: cannot build for the simulator (plain IronPrint); `IronPrint.Xamarin` throws `NotImplementedException`

**Packages:** IronPrint 2026.11.1 and IronPrint.Xamarin 2026.11.1
**Target:** `net11.0-ios`, iOS 27.0 simulator (iPad), Apple Silicon Mac
**Environment:** .NET 11 RC3 SDK, `Microsoft.Maui.Sdk` `11.0.0-rc.2.26505.5`,
`Microsoft.iOS.Sdk.net11.0_27.0` `27.0.12212-net11-rc.2`

**4a. Plain IronPrint, `iossimulator-arm64` (default on Apple Silicon):**
```
ld: building for 'iOS-simulator', but linking in dylib
(.../nativelibraries/IronPrintInterop.dylib) built for 'iOS'
```
The package ships `ios-arm64` and `iossimulator-x64` native assets but no
`iossimulator-arm64`, so the device-built dylib is selected and the linker
rejects it. Same class of gap as section 1.

**4b. Plain IronPrint, `-r iossimulator-x64`:**
```
Undefined symbols for architecture x86_64:
  "_C_CancelFunction", referenced from: <initial-undefines>
  "_C_CloseAllSessions", referenced from: <initial-undefines>
  ... (PKCS#11 C_* function table)
```
The same `Pkcs11Interop` link failure as section 2, so the one simulator
RID that does have a native asset still cannot be built.

**4c. IronPrint.Xamarin:** builds, launches and renders. NuGet resolves the
package to `lib/netstandard2.0/IronPrint.Xamarin.dll` for `net11.0-ios`
(confirmed in `project.assets.json`; `lib/xamarin.ios10` is not selected).
Every `PrintAsync` scenario (PDF path, PDF bytes, JPG path, grayscale,
2 copies, landscape) then fails immediately with:
```
NotImplementedException: The method or operation is not implemented.
```
Confirmed with a licensed run (`Licensed: True`, 2026-11-12): identical result for all 11 scenarios.

**Not tested:** a physical iOS device (`ios-arm64`), and any x64 simulator
run (4b never links; the test Mac only has arm64 simulator devices).

**Impact:** no IronPrint 2026.11.1 package could print from an iOS
simulator app on this SDK.

**Suggested fix:** ship an `iossimulator-arm64` `IronPrintInterop.dylib`,
and resolve the `Pkcs11Interop` static-link problem (section 2).

**Correction to an earlier draft:** this section previously reported a
Microsoft .NET MAUI defect ("iOS app never renders any UI"). That was a
gap in this test app, not in MAUI. The iOS 27 SDK requires the UIKit scene
lifecycle, and the app needs a `SceneDelegate : MauiUISceneDelegate` plus a
`UISceneConfigurations` entry in `Info.plist`, as Microsoft documents at
https://devblogs.microsoft.com/dotnet/migrate-dotnet-maui-apps-uikit-scene-lifecycle/.
With that added, the UI renders normally. Nothing to report to Microsoft.
