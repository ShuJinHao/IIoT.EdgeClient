using IIoT.Edge.Application.Common.Crud;
using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.UI.Shared.PluginSystem;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace IIoT.Edge.Presentation.Navigation.Common.Crud;

public abstract class CrudPageViewModelBase : ViewModelBase
{
    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            _statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

    protected ICommand CreateBusyCommand(Func<Task> execute, Func<bool>? canExecute = null)
        => new AsyncCommand(
            () => ExecuteBusyAsync(execute),
            () => !IsBusy && (canExecute?.Invoke() ?? true));

    protected ICommand CreateBusyCommand(
        Func<Task<CrudOperationResult>> execute,
        Func<bool>? canExecute = null)
        => new AsyncCommand(
            () => RunOperationAsync(execute),
            () => !IsBusy && (canExecute?.Invoke() ?? true));

    protected ICommand CreateAddCommand<TItem>(
        ObservableCollection<TItem> items,
        Func<TItem> factory,
        Func<bool>? canExecute = null)
        => new BaseCommand(
            _ => items.Add(factory()),
            _ => canExecute?.Invoke() ?? true);

    protected ICommand CreateDeleteCommand<TItem>(
        ObservableCollection<TItem> items,
        Func<bool>? canExecute = null)
        => new BaseCommand(
            param => RemoveItem(items, param),
            param => (canExecute?.Invoke() ?? true) && param is TItem);

    protected ICommand CreateScopedAddCommand<TItem>(
        Func<ObservableCollection<TItem>?> itemsResolver,
        Func<TItem> factory,
        Func<bool>? canExecute = null)
        => new BaseCommand(
            _ =>
            {
                var items = itemsResolver();
                if (items is null)
                {
                    return;
                }

                items.Add(factory());
            },
            _ => (canExecute?.Invoke() ?? true) && itemsResolver() is not null);

    protected ICommand CreateScopedDeleteCommand<TItem>(
        Func<ObservableCollection<TItem>?> itemsResolver,
        Func<bool>? canExecute = null)
        => new BaseCommand(
            param =>
            {
                var items = itemsResolver();
                if (items is null)
                {
                    return;
                }

                RemoveItem(items, param);
            },
            param => (canExecute?.Invoke() ?? true) && itemsResolver() is not null && param is TItem);

    protected static void ReplaceItems<TItem>(ObservableCollection<TItem> target, IEnumerable<TItem> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    protected static void RemoveItem<TItem>(ObservableCollection<TItem> items, object? candidate)
    {
        if (candidate is TItem item)
        {
            items.Remove(item);
        }
    }

    protected void ClearFeedback()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }

    protected void SetError(string message)
    {
        ErrorMessage = message;
        StatusMessage = string.Empty;
    }

    protected void SetStatus(string message)
    {
        StatusMessage = message;
        ErrorMessage = string.Empty;
    }

    protected async Task RunSaveAsync(Func<Task<CrudOperationResult>> execute)
    {
        await RunOperationAsync(execute);
    }

    protected async Task RunDeleteAsync(Func<Task<CrudOperationResult>> execute)
    {
        await RunOperationAsync(execute);
    }

    protected async Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync<TModel>(
        IEnumerable<TModel> models,
        IEditorValidator<TModel> validator,
        CancellationToken cancellationToken = default)
    {
        var issues = new List<ValidationIssue>();

        foreach (var model in models)
        {
            var validationIssues = await validator.ValidateAsync(model, cancellationToken);
            issues.AddRange(validationIssues);
        }

        return issues;
    }

    protected async Task<IReadOnlyCollection<ValidationIssue>> ValidateAsync<TModel>(
        TModel model,
        IEditorValidator<TModel> validator,
        CancellationToken cancellationToken = default)
    {
        return await validator.ValidateAsync(model, cancellationToken);
    }

    protected CrudOperationResult CreateValidationResult(
        IEnumerable<ValidationIssue> issues,
        string? message = null)
    {
        var validationIssues = issues
            .Where(issue => !string.IsNullOrWhiteSpace(issue.Message))
            .Distinct()
            .ToArray();

        return validationIssues.Length == 0
            ? CrudOperationResult.Success()
            : CrudOperationResult.ValidationFailure(validationIssues, message ?? GetDefaultValidationMessage());
    }

    protected async Task ExecuteBusyAsync(Func<Task> execute)
    {
        if (IsBusy)
        {
            return;
        }

        ClearFeedback();

        try
        {
            IsBusy = true;
            await execute();
        }
        catch (Exception ex)
        {
            SetError(FormatOperationFailedMessage(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunOperationAsync(Func<Task<CrudOperationResult>> execute)
    {
        if (IsBusy)
        {
            return;
        }

        ClearFeedback();

        try
        {
            IsBusy = true;
            var result = await execute();
            ApplyOperationResult(result);
        }
        catch (Exception ex)
        {
            SetError(FormatOperationFailedMessage(ex));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ApplyOperationResult(CrudOperationResult result)
    {
        if (result.IsSuccess)
        {
            if (!string.IsNullOrWhiteSpace(result.Message))
            {
                SetStatus(result.Message);
            }

            return;
        }

        var validationMessage = string.Join(
            Environment.NewLine,
            result.ValidationIssues
                .Select(issue => issue.Message)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .Distinct());

        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            SetError(validationMessage);
            return;
        }

        SetError(string.IsNullOrWhiteSpace(result.Message)
            ? GetDefaultOperationFailedMessage()
            : result.Message);
    }

    protected virtual string GetDefaultValidationMessage()
        => "Please fix the invalid form fields first.";

    protected virtual string FormatOperationFailedMessage(Exception ex)
        => $"Operation failed: {ex.Message}";

    protected virtual string GetDefaultOperationFailedMessage()
        => "Operation failed. Please try again later.";
}
