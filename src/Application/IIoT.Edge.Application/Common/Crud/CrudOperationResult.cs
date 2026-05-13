namespace IIoT.Edge.Application.Common.Crud;

/// <summary>
/// CRUD operation result.
/// </summary>
public sealed class CrudOperationResult
{
    private CrudOperationResult(
        bool isSuccess,
        string message,
        IReadOnlyCollection<ValidationIssue> validationIssues)
    {
        IsSuccess = isSuccess;
        Message = message;
        ValidationIssues = validationIssues;
    }

    public bool IsSuccess { get; }

    public string Message { get; }

    public IReadOnlyCollection<ValidationIssue> ValidationIssues { get; }

    public static CrudOperationResult Success(string message = "")
        => new(true, message, Array.Empty<ValidationIssue>());

    public static CrudOperationResult Failure(string message)
        => new(false, message, Array.Empty<ValidationIssue>());

    public static CrudOperationResult ValidationFailure(
        IEnumerable<ValidationIssue> issues,
        string message = "Please fix the invalid form fields first.")
        => new(
            false,
            message,
            issues
                .Where(issue => !string.IsNullOrWhiteSpace(issue.Message))
                .Distinct()
                .ToArray());
}
