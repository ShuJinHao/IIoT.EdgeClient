namespace IIoT.Edge.Application.Common.Crud;

/// <summary>
/// 单条校验问题。
/// </summary>
public sealed record ValidationIssue(string Message, string? Field = null);
