using System.Text;
using System.Text.Json;

namespace IronPrintTest.Shared;

public static class ResultsReport
{
    public static void WriteMarkdown(string path, IReadOnlyList<ScenarioResult> results, string platformLabel)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# IronPrint .NET 11 results -- {platformLabel}");
        sb.AppendLine();
        sb.AppendLine($"Generated: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        sb.AppendLine("| Id | Scenario | Status | Expected on paper | Message |");
        sb.AppendLine("|----|----------|--------|--------------------|---------|");
        foreach (var r in results)
        {
            sb.AppendLine($"| {r.Id} | {Escape(r.Name)} | {r.StatusLabel} | {Escape(r.ExpectedOnPaper ?? "")} | {Escape(r.Message ?? "")} |");
        }

        File.WriteAllText(path, sb.ToString());
    }

    public static void WriteJson(string path, IReadOnlyList<ScenarioResult> results, string platformLabel)
    {
        var payload = new
        {
            platform = platformLabel,
            generatedAt = DateTimeOffset.Now,
            results = results.Select(r => new
            {
                r.Id,
                r.Name,
                status = r.StatusLabel,
                r.ExpectedOnPaper,
                r.Message,
            }),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static string Escape(string s) => s.Replace("|", "\\|").Replace("\n", " ").Replace("\r", "");
}
