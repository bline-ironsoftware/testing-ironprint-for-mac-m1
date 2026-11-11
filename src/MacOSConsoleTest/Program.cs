using System.Text.Json;
using IronPrint;
using IronPrintTest.Shared;

IronPrint.License.LicenseKey = Environment.GetEnvironmentVariable("IRONPRINT_LICENSE_KEY");

Console.WriteLine($"=== IronPrint {typeof(Printer).Assembly.GetName().Version} .NET 11 Compatibility Test (native macOS console) ===");
Console.WriteLine($"Licensed: {IronPrint.License.IsLicensed}");
Console.WriteLine($"Runtime: {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription} / {System.Runtime.InteropServices.RuntimeInformation.OSDescription}");
Console.WriteLine();

var baseDir = AppContext.BaseDirectory;
var pdfPath = Path.Combine(baseDir, "TestFiles", "sample.pdf");
var jpgPath = Path.Combine(baseDir, "TestFiles", "sample.jpg");

var printerName = await ResolvePrinterNameAsync(args, baseDir);
Console.WriteLine(printerName is null
    ? "Printer: (none resolved -- IronPrint will use the OS default printer)"
    : $"Printer: {printerName}");
Console.WriteLine();

var context = new ScenarioContext
{
    PrinterName = printerName,
    PdfPath = pdfPath,
    JpgPath = jpgPath,
};

var results = new List<ScenarioResult>();
foreach (var scenario in IronPrintScenarios.All)
{
    var result = await scenario.RunAsync(context);
    results.Add(result);

    var line = $"[{result.StatusLabel}] {result.Id}: {result.Name}";
    if (result.Status == ScenarioStatus.Fail)
    {
        line += $" -> {result.Message}";
    }
    else if (!string.IsNullOrEmpty(result.ExpectedOnPaper))
    {
        line += $" -> {result.ExpectedOnPaper}";
    }

    Console.WriteLine(line);
}

var platformLabel = $"Native macOS console / {System.Runtime.InteropServices.RuntimeInformation.OSDescription}";
ResultsReport.WriteMarkdown(Path.Combine(baseDir, "results.md"), results, platformLabel);
ResultsReport.WriteJson(Path.Combine(baseDir, "results.json"), results, platformLabel);

Console.WriteLine();
Console.WriteLine($"Results written to {Path.Combine(baseDir, "results.md")} and {Path.Combine(baseDir, "results.json")}");

var failCount = results.Count(r => r.Status == ScenarioStatus.Fail);
var passCount = results.Count(r => r.Status == ScenarioStatus.Pass);
var sentCount = results.Count(r => r.Status == ScenarioStatus.Sent);
Console.WriteLine($"=== {passCount} passed, {sentCount} sent (check paper), {failCount} failed ===");

Environment.Exit(failCount == 0 ? 0 : 1);

static async Task<string?> ResolvePrinterNameAsync(string[] args, string baseDir)
{
    // 1. Command-line argument: --printer "Name" (or -p "Name")
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (args[i] is "--printer" or "-p")
        {
            return args[i + 1];
        }
    }

    // 2. appsettings.json: { "PrinterName": "..." }
    var appsettingsPath = Path.Combine(baseDir, "appsettings.json");
    if (File.Exists(appsettingsPath))
    {
        try
        {
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(appsettingsPath));
            if (doc.RootElement.TryGetProperty("PrinterName", out var printerNameElement)
                && printerNameElement.ValueKind == JsonValueKind.String)
            {
                var configured = printerNameElement.GetString();
                if (!string.IsNullOrWhiteSpace(configured))
                {
                    return configured;
                }
            }
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Warning: could not parse appsettings.json ({ex.Message}); falling back to first available printer.");
        }
    }

    // 3. First printer found.
    try
    {
        var names = await PlatformPrinterNames.GetPrinterNamesAsync();
        return names.Count > 0 ? names[0] : null;
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Warning: could not enumerate printers ({ex.GetType().Name}: {ex.Message}); letting IronPrint use the OS default.");
        return null;
    }
}
