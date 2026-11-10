namespace IronPrintTest.Shared;

public enum ScenarioStatus
{
    /// <summary>No physical output produced; pass/fail is fully determined in code (e.g. S1).</summary>
    Pass,
    Fail,

    /// <summary>The print job was accepted without throwing; a human must check the paper to confirm it's correct.</summary>
    Sent,
}

public sealed class ScenarioResult
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required ScenarioStatus Status { get; init; }
    public string? Message { get; init; }

    /// <summary>What to look for on the printed page, for scenarios that produce paper (S2-S7).</summary>
    public string? ExpectedOnPaper { get; init; }

    public string StatusLabel => Status switch
    {
        ScenarioStatus.Pass => "PASS",
        ScenarioStatus.Fail => "FAIL",
        ScenarioStatus.Sent => "SENT (check paper)",
        _ => Status.ToString(),
    };
}
