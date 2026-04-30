namespace IIoT.Edge.Module.Homogenization.Payload;

/// <summary>
/// 匀浆配方快照，由配方上传任务在 PLC 触发时读取整组工艺参数，并上传到 MES 配方接口。
/// </summary>
public sealed class HomogenizationRecipeSnapshot
{
    /// <summary>
    /// 配方参数采集时间，用于 UI 最近上传状态和问题追踪。
    /// </summary>
    public DateTime CapturedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 各步骤搅拌转速数组，按 PLC 配方顺序上传为带序号的 MES item。
    /// </summary>
    public IReadOnlyList<int> StirringSpeed { get; set; } = [];

    /// <summary>
    /// 各步骤分散转速数组，按 PLC 配方顺序上传为带序号的 MES item。
    /// </summary>
    public IReadOnlyList<int> DispersionSpeed { get; set; } = [];

    /// <summary>
    /// 各步骤 NCM 投料目标数组，来源于 PLC 配方浮点区。
    /// </summary>
    public IReadOnlyList<double> Ncm { get; set; } = [];

    /// <summary>
    /// 各步骤 SP1 投料目标数组，来源于 PLC 配方浮点区。
    /// </summary>
    public IReadOnlyList<double> Sp1 { get; set; } = [];

    /// <summary>
    /// 各步骤 NMP 投料目标数组，来源于 PLC 配方浮点区。
    /// </summary>
    public IReadOnlyList<double> Nmp { get; set; } = [];

    /// <summary>
    /// 各步骤胶液投料目标数组，来源于 PLC 配方浮点区。
    /// </summary>
    public IReadOnlyList<double> GlueSolution { get; set; } = [];

    /// <summary>
    /// 各步骤 CNT 投料目标数组，来源于 PLC 配方浮点区。
    /// </summary>
    public IReadOnlyList<double> Cnt { get; set; } = [];

    /// <summary>
    /// 各步骤真空启停标识，上传 MES 前转换为 0/1 item 值。
    /// </summary>
    public IReadOnlyList<bool> Vacuum { get; set; } = [];

    /// <summary>
    /// 各步骤工艺时间数组，来源于 PLC 配方时间区。
    /// </summary>
    public IReadOnlyList<int> Time { get; set; } = [];

    /// <summary>
    /// 各步骤温度目标数组，来源于 PLC 配方温度区。
    /// </summary>
    public IReadOnlyList<double> Temperature { get; set; } = [];

    /// <summary>
    /// 各步骤停机标识，上传 MES 前转换为 0/1 item 值。
    /// </summary>
    public IReadOnlyList<bool> StopStep { get; set; } = [];
}
