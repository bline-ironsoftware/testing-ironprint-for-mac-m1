using IronPrint;
using IronPrintTest.Shared;

namespace MauiTest;

public partial class MainPage : ContentPage
{
    private ScenarioContext? _context;

    public MainPage()
    {
        InitializeComponent();
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        try
        {
            var licenseKey = Environment.GetEnvironmentVariable("SUITE_LICENSE_KEY");
            if (string.IsNullOrEmpty(licenseKey))
            {
                // Mobile/MacCatalyst processes can't see the build machine's env vars at
                // runtime -- fall back to the value baked in at build time (see MauiTest.csproj).
                licenseKey = BuildTimeLicenseKey.Value;
            }
            IronPrint.License.LicenseKey = licenseKey;

            var pdfPath = await CopyAppPackageFileToCacheAsync("TestFiles/sample.pdf");
            var jpgPath = await CopyAppPackageFileToCacheAsync("TestFiles/sample.jpg");

            _context = new ScenarioContext
            {
                PrinterName = null,
                PdfPath = pdfPath,
                JpgPath = jpgPath,
            };

            PlatformLabel.Text =
                $"IronPrint {typeof(Printer).Assembly.GetName().Version} on {DeviceInfo.Platform} " +
                $"{DeviceInfo.VersionString} ({DeviceInfo.Idiom}) -- Licensed: {License.IsLicensed}";
            AppendLog("Ready. Test files copied into the app sandbox. Tap a scenario below.");
        }
        catch (Exception ex)
        {
            PlatformLabel.Text = "Setup failed -- see log";
            AppendLog($"Setup failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// IronPrint.Printer.PrintAsync needs a real file path; bundled MauiAsset files are only
    /// reachable through FileSystem.OpenAppPackageFileAsync (a stream), so each one is copied
    /// once into FileSystem.CacheDirectory and that copy's path is reused for every scenario.
    /// </summary>
    private static async Task<string> CopyAppPackageFileToCacheAsync(string logicalName)
    {
        var targetPath = Path.Combine(FileSystem.CacheDirectory, Path.GetFileName(logicalName));
        await using var source = await FileSystem.OpenAppPackageFileAsync(logicalName);
        await using var dest = File.Create(targetPath);
        await source.CopyToAsync(dest);
        return targetPath;
    }

    private async void OnScenarioClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button || button.CommandParameter is not string scenarioId)
        {
            return;
        }

        if (_context is null)
        {
            AppendLog("Not ready yet -- setup is still running or failed above.");
            return;
        }

        var scenario = IronPrintScenarios.All.Single(s => s.Id == scenarioId);
        var printerName = string.IsNullOrWhiteSpace(PrinterNameEntry.Text) ? null : PrinterNameEntry.Text;
        var context = _context with { PrinterName = printerName };

        button.IsEnabled = false;
        AppendLog($"Running {scenario.Id}: {scenario.Name}...");
        try
        {
            var result = await scenario.RunAsync(context);
            var line = $"[{result.StatusLabel}] {result.Id}: {result.Name}";
            if (result.Status == ScenarioStatus.Fail)
            {
                line += $" -> {result.Message}";
            }
            else if (!string.IsNullOrEmpty(result.ExpectedOnPaper))
            {
                line += $" -> {result.ExpectedOnPaper}";
            }

            AppendLog(line);
        }
        catch (Exception ex)
        {
            AppendLog($"[FAIL] {scenario.Id}: {scenario.Name} -> unhandled {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private void AppendLog(string line)
    {
        LogEditor.Text += $"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}";
    }
}
