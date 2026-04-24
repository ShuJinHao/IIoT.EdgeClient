namespace IIoT.Edge.Module.Homogenization.Payload;

public sealed class HomogenizationRecipeSnapshot
{
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    public IReadOnlyList<int> StirringSpeed { get; set; } = [];

    public IReadOnlyList<int> DispersionSpeed { get; set; } = [];

    public IReadOnlyList<double> Ncm { get; set; } = [];

    public IReadOnlyList<double> Sp1 { get; set; } = [];

    public IReadOnlyList<double> Nmp { get; set; } = [];

    public IReadOnlyList<double> GlueSolution { get; set; } = [];

    public IReadOnlyList<double> Cnt { get; set; } = [];

    public IReadOnlyList<bool> Vacuum { get; set; } = [];

    public IReadOnlyList<int> Time { get; set; } = [];

    public IReadOnlyList<double> Temperature { get; set; } = [];

    public IReadOnlyList<bool> StopStep { get; set; } = [];
}
