using IronPrint;

namespace IronPrintTest.Shared;

public sealed record ScenarioContext
{
    /// <summary>Printer to target. Null/empty means "let IronPrint use the OS default".</summary>
    public string? PrinterName { get; init; }
    public required string PdfPath { get; init; }
    public required string JpgPath { get; init; }
}

public sealed class ScenarioDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string? ExpectedOnPaper { get; init; }
    public required Func<ScenarioContext, Task<ScenarioResult>> RunAsync { get; init; }
}

public static class IronPrintScenarios
{
    public static IReadOnlyList<ScenarioDefinition> All { get; } = new List<ScenarioDefinition>
    {
        new()
        {
            Id = "S1",
            Name = "GetPrinterNamesAsync: list the printers",
            RunAsync = RunS1,
        },
        new()
        {
            Id = "S2",
            Name = "PrintAsync with a PDF file path",
            RunAsync = RunS2,
        },
        new()
        {
            Id = "S3",
            Name = "PrintAsync with the PDF as byte[]",
            RunAsync = RunS3,
        },
        new()
        {
            Id = "S4",
            Name = "PrintAsync with a JPG file path",
            RunAsync = RunS4,
        },
        new()
        {
            Id = "S5",
            Name = "Grayscale = true",
            ExpectedOnPaper = "Expect the JPG's colour bars to print as shades of grey.",
            RunAsync = RunS5,
        },
        new()
        {
            Id = "S6",
            Name = "2 copies",
            ExpectedOnPaper = "Expect 2 full copies of the 2-page PDF (page order: 1, 2, 1, 2).",
            RunAsync = RunS6,
        },
        new()
        {
            Id = "S7",
            Name = "Landscape orientation",
            ExpectedOnPaper = "Expect the JPG's TOP arrow to point along the long edge of the paper.",
            RunAsync = RunS7,
        },
    };

    private static ScenarioResult Pass(string id, string name, string message) =>
        new() { Id = id, Name = name, Status = ScenarioStatus.Pass, Message = message };

    private static ScenarioResult Sent(string id, string name, string? expectedOnPaper) =>
        new() { Id = id, Name = name, Status = ScenarioStatus.Sent, ExpectedOnPaper = expectedOnPaper };

    private static ScenarioResult Fail(string id, string name, Exception ex)
    {
        // Full stack trace goes to stderr (logcat on Android, device console on iOS).
        Console.Error.WriteLine($"[{id}] {ex}");
        return new() { Id = id, Name = name, Status = ScenarioStatus.Fail, Message = $"{ex.GetType().Name}: {ex.Message}" };
    }

    private static async Task<ScenarioResult> RunS1(ScenarioContext ctx)
    {
        var def = All.Single(s => s.Id == "S1");
        try
        {
            var names = await PlatformPrinterNames.GetPrinterNamesAsync();
            return Pass(def.Id, def.Name, $"{names.Count} printer(s) found: {string.Join(", ", names)}");
        }
        catch (Exception ex)
        {
            return Fail(def.Id, def.Name, ex);
        }
    }

    private static async Task<ScenarioResult> RunS2(ScenarioContext ctx)
    {
        var def = All.Single(s => s.Id == "S2");
        try
        {
            await Printer.PrintAsync(ctx.PdfPath, new PrintSettings { PrinterName = ctx.PrinterName });
            return Sent(def.Id, def.Name, def.ExpectedOnPaper);
        }
        catch (Exception ex)
        {
            return Fail(def.Id, def.Name, ex);
        }
    }

    private static async Task<ScenarioResult> RunS3(ScenarioContext ctx)
    {
        var def = All.Single(s => s.Id == "S3");
        try
        {
            var bytes = await File.ReadAllBytesAsync(ctx.PdfPath);
            await Printer.PrintAsync(bytes, new PrintSettings { PrinterName = ctx.PrinterName });
            return Sent(def.Id, def.Name, def.ExpectedOnPaper);
        }
        catch (Exception ex)
        {
            return Fail(def.Id, def.Name, ex);
        }
    }

    private static async Task<ScenarioResult> RunS4(ScenarioContext ctx)
    {
        var def = All.Single(s => s.Id == "S4");
        try
        {
            await Printer.PrintAsync(ctx.JpgPath, new PrintSettings { PrinterName = ctx.PrinterName });
            return Sent(def.Id, def.Name, def.ExpectedOnPaper);
        }
        catch (Exception ex)
        {
            return Fail(def.Id, def.Name, ex);
        }
    }

    private static async Task<ScenarioResult> RunS5(ScenarioContext ctx)
    {
        var def = All.Single(s => s.Id == "S5");
        try
        {
            await Printer.PrintAsync(ctx.JpgPath, new PrintSettings { PrinterName = ctx.PrinterName, Grayscale = true });
            return Sent(def.Id, def.Name, def.ExpectedOnPaper);
        }
        catch (Exception ex)
        {
            return Fail(def.Id, def.Name, ex);
        }
    }

    private static async Task<ScenarioResult> RunS6(ScenarioContext ctx)
    {
        var def = All.Single(s => s.Id == "S6");
        try
        {
            await Printer.PrintAsync(ctx.PdfPath, new PrintSettings { PrinterName = ctx.PrinterName, NumberOfCopies = 2 });
            return Sent(def.Id, def.Name, def.ExpectedOnPaper);
        }
        catch (Exception ex)
        {
            return Fail(def.Id, def.Name, ex);
        }
    }

    private static async Task<ScenarioResult> RunS7(ScenarioContext ctx)
    {
        var def = All.Single(s => s.Id == "S7");
        try
        {
            await Printer.PrintAsync(ctx.JpgPath, new PrintSettings { PrinterName = ctx.PrinterName, PaperOrientation = PaperOrientation.Landscape });
            return Sent(def.Id, def.Name, def.ExpectedOnPaper);
        }
        catch (Exception ex)
        {
            return Fail(def.Id, def.Name, ex);
        }
    }
}
