namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>单条断言结果</summary>
public sealed class AssertionResult
{
    public string Description { get; init; } = string.Empty;
    public bool   Passed      { get; init; }
    public string Expected    { get; init; } = string.Empty;
    public string Actual      { get; init; } = string.Empty;

    public override string ToString()
    {
        var icon = Passed ? "通过" : "失败";
        var detail = Passed ? string.Empty : $"  （期望: {Expected}，实际: {Actual}）";
        return $"{icon} {Description}{detail}";
    }
}

/// <summary>单个场景的完整结果</summary>
public sealed class ScenarioResult
{
    public string Name   { get; init; } = string.Empty;
    public bool   Passed { get; set; }
    public string? Error { get; set; }
    public List<AssertionResult> Assertions { get; init; } = new();
}
