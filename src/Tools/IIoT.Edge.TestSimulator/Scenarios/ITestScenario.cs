namespace IIoT.Edge.TestSimulator.Scenarios;

/// <summary>测试场景接口</summary>
public interface ITestScenario
{
    string Name { get; }
    Task<ScenarioResult> RunAsync(CancellationToken ct = default);
}
