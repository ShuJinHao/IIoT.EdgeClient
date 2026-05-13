namespace IIoT.Edge.SharedKernel.Result;

/// <summary>
/// 通用操作结果。
/// 适用于边缘端的简化结果模型，只关注成功状态、错误信息与返回数据。
/// </summary>
public class Result<T>
{
    protected Result(T? value, bool isSuccess, string? errorMessage)
    {
        Value = value;
        IsSuccess = isSuccess;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 返回的数据；失败时为默认值。
    /// </summary>
    public T? Value { get; }

    /// <summary>
    /// 是否执行成功。
    /// </summary>
    public bool IsSuccess { get; }

    /// <summary>
    /// 错误信息；成功时为 <c>null</c>。
    /// </summary>
    public string? ErrorMessage { get; }

    /// <summary>
    /// 内部工厂方法，供无泛型结果类型调用。
    /// </summary>
    internal static Result<T> Create(T? value, bool isSuccess, string? errorMessage)
        => new(value, isSuccess, errorMessage);

    /// <summary>
    /// 允许从无泛型 <see cref="Result"/> 隐式转换。
    /// </summary>
    public static implicit operator Result<T>(Result result)
        => new(default, result.IsSuccess, result.ErrorMessage);
}

/// <summary>
/// 无返回数据的操作结果。
/// </summary>
public class Result : Result<object>
{
    private Result(bool isSuccess, string? errorMessage)
        : base(null, isSuccess, errorMessage)
    {
    }

    public static Result Success()
        => new(true, null);

    public static Result<T> Success<T>(T value)
        => Result<T>.Create(value, true, null);

    public static Result Failure(string errorMessage)
        => new(false, errorMessage);
}
