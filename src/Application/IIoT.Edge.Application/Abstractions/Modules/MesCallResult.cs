namespace IIoT.Edge.Application.Abstractions.Modules;

public enum MesCallOutcome
{
    Success = 0,
    BusinessRejected = 1,
    TransportFailure = 2,
    InvalidContext = 3,
    Disabled = 4
}

public sealed record MesCallResult(
    MesCallOutcome Outcome,
    string Message)
{
    public bool IsSuccess => Outcome == MesCallOutcome.Success || Outcome == MesCallOutcome.Disabled;

    public static MesCallResult Success(string message = "MES 调用成功。")
        => new(MesCallOutcome.Success, message);

    public static MesCallResult BusinessRejected(string message)
        => new(MesCallOutcome.BusinessRejected, message);

    public static MesCallResult TransportFailure(string message)
        => new(MesCallOutcome.TransportFailure, message);

    public static MesCallResult InvalidContext(string message)
        => new(MesCallOutcome.InvalidContext, message);

    public static MesCallResult Disabled(string message = "MES 上传已关闭。")
        => new(MesCallOutcome.Disabled, message);
}
