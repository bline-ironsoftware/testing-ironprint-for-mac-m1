# IronPrint 2026.11.1 / .NET 11 RC3 compatibility findings

Four confirmed, reproducible issues found while testing whether IronPrint
2026.11.1 works with .NET 11 RC3 (`11.0.100-rc.3.26522.148`) on macOS
(native, Mac Catalyst) and mobile (Android, iOS). All four belong to
IronSoftware (IronPrint itself). An earlier draft attributed the iOS
problem to .NET MAUI; that was wrong, see the note at the end of section 4.
Full repro repo:
https://github.com/bline-ironsoftware/testing-ironprint-for-mac-m1

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

## 3. IronSoftware -- Android: every `PrintAsync` call fails with `Resource not found: 0`

**Package:** IronPrint 2026.11.1 **and** IronPrint.Xamarin 2026.11.1 (both tested, identical result)
**Target:** `net11.0-android`, API 24+, tested on an API 35 emulator (Pixel-class device)
**Environment:** .NET 11 RC3 SDK, Android SDK platform 37 (Microsoft.Android.Sdk.Windows 37.0.0-preview.6.59)

**Repro:**
```csharp
Printer.Initialize(this); // in MainActivity.OnCreate, after base.OnCreate
// ... later, after licensing:
await Printer.PrintAsync(pdfPath); // or jpgPath, or the byte[] overload
```

**Actual result:**
```
FileNotFoundException: Resource not found: 0
```

**Confirmed, ruled out:**
- Not a missing-file issue -- the harness logs confirm both test files
  (`sample.pdf`, `sample.jpg`) were successfully copied into the app's
  sandbox cache directory *before* the error, and the same error occurs
  for both the PDF-path and JPG-path scenarios identically.
- Not fixed by `Printer.Initialize(Android.Content.Context)` -- that call
  is required (without it: `NullReferenceException: MainActivity is
  undefined`) but doesn't change this subsequent error once added.
- Not package-specific -- identical error, verbatim, on both the plain
  `IronPrint` package and the legacy `IronPrint.Xamarin` package.
- Doesn't appear in Android's own `logcat` at all (it's a purely
  .NET-level exception, never surfaces as a Java/Android system error).
- The literal string `"Resource not found"` does not appear anywhere in
  `IronPrint.dll`, `IronPrint.Xamarin.dll`, or `Microsoft.Maui.Graphics.dll`
  (checked via a raw UTF-16 string scan of each assembly), and no bundled
  `androidx.print` or other print-related native library exists in the
  build output -- so the exact throw site couldn't be identified without
  a working IL decompiler (blocked in our environment).

**Impact:** `PrintAsync` is completely non-functional on Android with this
IronPrint version, for both PDF and image input.

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
This run was unlicensed (`Licensed: False`); a licensed re-run was not done.

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
