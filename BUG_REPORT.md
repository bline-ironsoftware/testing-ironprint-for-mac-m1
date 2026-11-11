# IronPrint 2026.11.1 / .NET 11 RC3 compatibility findings

Four confirmed, reproducible issues found while testing whether IronPrint
2026.11.1 works with .NET 11 RC3 (`11.0.100-rc.3.26522.148`) on macOS
(native, Mac Catalyst) and mobile (Android, iOS). Three belong to
IronSoftware (IronPrint itself); one belongs to Microsoft (.NET
MAUI/Microsoft.iOS). Full repro repo:
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

## 4. Not IronSoftware -- Microsoft .NET MAUI / Microsoft.iOS: iOS app never renders any UI

**This one is for Microsoft/the dotnet team, not IronSoftware** -- it
blocks testing IronPrint on iOS at all, but has nothing to do with
IronPrint itself.

**Environment:** .NET 11 RC3 SDK, `Microsoft.Maui.Sdk` `11.0.0-rc.2.26505.5`,
iOS Simulator runtime 27.0/27.1, iPhone 15/18 Pro/Duo simulators all tested

**Step 1 -- app fails to launch at all without a scene manifest:**
```
Error (UIKitCore) Application failed to launch: UIScene life cycle is
required for apps built with this SDK. See "Transitioning to the UIKit
scene-based life cycle" in the UIKit documentation for more information
on migration.
```
confirmed via the simulator's unified system log
(`xcrun simctl spawn <udid> log show --predicate 'process == "MauiTest"'`).
This iOS SDK apparently now mandates the UIKit scene-based app lifecycle;
the default MAUI iOS project scaffold's `Info.plist` has no
`UIApplicationSceneManifest` entry at all.

**Step 2 -- after adding the minimal scene manifest, the app launches
cleanly but renders nothing:**
```xml
<key>UIApplicationSceneManifest</key>
<dict>
    <key>UIApplicationSupportsMultipleScenes</key>
    <false/>
</dict>
```
With this added, the app launches with **zero errors or exceptions** --
confirmed via `xcrun simctl launch --console <udid> <bundle-id>`, which
showed only two unrelated, harmless warnings (an IDE debugger socket
connection refusal, and a background-fetch `Info.plist` note) -- but the
screen stays permanently black. No MAUI content (labels, buttons, the
app's own log view) ever appears, confirmed via `simctl io ... screenshot`
across three different device types (iPhone 15, iPhone 18 Pro, iPhone
Duo) and two iOS runtime versions (27.0, 27.1).

**Root cause (suspected, not confirmed from source):** Mac Catalyst has
required scene-based lifecycle since its inception, so MAUI's scene
delegate wiring for Mac Catalyst is mature and works. Plain iOS never
required scenes until this SDK, so MAUI's iOS-specific scene delegate
(window/root-view-controller setup in response to
`scene:willConnectToSession:options:`) may simply not be fully
implemented yet in this preview MAUI build.

**Impact:** no .NET MAUI iOS app's UI can be exercised at all on this
.NET 11 RC3 + iOS 27 SDK combination -- this blocks testing *any*
MAUI/iOS app, not just IronPrint's.
