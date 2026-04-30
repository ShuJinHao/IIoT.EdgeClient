namespace IIoT.Edge.Module.Stacking.Constants;

/// <summary>
/// 叠片 PLC 结果码，决定采集记录上传云端时的 OK/NG 结果。
/// </summary>
public enum StackingResultCode
{
    /// <summary>
    /// PLC 未给出有效结果码。
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// 叠片结果正常。
    /// </summary>
    Ok = 1,

    /// <summary>
    /// 叠片结果异常。
    /// </summary>
    Ng = 2
}
