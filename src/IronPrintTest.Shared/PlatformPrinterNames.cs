using IronPrint;

namespace IronPrintTest.Shared;

/// <summary>
/// Wraps <see cref="Printer.GetPrinterNamesAsync"/>, which does not exist at all in the
/// macOS/MacCatalyst/iOS/Android builds of IronPrint (confirmed by inspecting the installed
/// IronPrint.dll/IronPrint.xml for those TargetFrameworks -- every mention on those platforms
/// is an unresolved "!:Printer.GetPrinterNamesAsync()" doc-comment cross-reference, not an
/// actual member; the member is present only in the net11.0 / net11.0-windows builds). Calling
/// it directly from code that multi-targets those TFMs would fail to compile there, so this
/// wrapper synthesizes the same PlatformNotSupportedException the scenario list expects (see S1
/// in the design) uniformly, keeping ConsoleTest, MacOSConsoleTest, and MauiTest free of
/// per-platform #if blocks.
/// </summary>
public static class PlatformPrinterNames
{
#if !ANDROID && !IOS && !MACCATALYST && !MACOS
    public static Task<List<string>> GetPrinterNamesAsync() => Printer.GetPrinterNamesAsync();
#else
    public static Task<List<string>> GetPrinterNamesAsync() =>
        throw new PlatformNotSupportedException(
            "IronPrint.Printer.GetPrinterNamesAsync() is not available on this platform's IronPrint build.");
#endif
}
