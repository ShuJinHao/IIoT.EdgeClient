using System.Collections.ObjectModel;

namespace IIoT.Edge.UI.Shared.PluginSystem;

public abstract class PresentationViewModelBase : ViewModelBase
{
    private bool _isBusy;
    private string _errorMessage = string.Empty;
    private string _statusMessage = string.Empty;

    public bool IsBusy
    {
        get => _isBusy;
        protected set
        {
            _isBusy = value;
            OnPropertyChanged();
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        protected set
        {
            _errorMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasError));
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        protected set
        {
            _statusMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusMessage);

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

    protected static void ReplaceItems<TItem>(ObservableCollection<TItem> target, IEnumerable<TItem> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    protected static void SyncItemsByKey<TTarget, TSource, TKey>(
        ObservableCollection<TTarget> target,
        IEnumerable<TSource> sources,
        Func<TTarget, TKey> targetKeySelector,
        Func<TSource, TKey> sourceKeySelector,
        Func<TSource, TTarget> create,
        Action<TTarget, TSource> update)
        where TKey : notnull
    {
        var sourceList = sources.ToList();
        var existingByKey = target.ToDictionary(targetKeySelector);

        foreach (var source in sourceList)
        {
            var key = sourceKeySelector(source);
            if (existingByKey.TryGetValue(key, out var existing))
            {
                update(existing, source);
                continue;
            }

            target.Add(create(source));
        }

        var sourceKeys = sourceList.Select(sourceKeySelector).ToHashSet();
        var staleItems = target.Where(item => !sourceKeys.Contains(targetKeySelector(item))).ToList();
        foreach (var staleItem in staleItems)
        {
            target.Remove(staleItem);
        }
    }

    protected Task RunViewTaskAsync(
        Func<Task> execute,
        string errorPrefix = "操作失败",
        bool trackBusy = true,
        bool clearFeedback = true)
        => ExecuteViewTaskAsync(execute, errorPrefix, trackBusy, clearFeedback);

    protected void RunViewTaskInBackground(
        Func<Task> execute,
        string errorPrefix = "操作失败",
        bool trackBusy = false,
        bool clearFeedback = false)
    {
        _ = ExecuteViewTaskAsync(execute, errorPrefix, trackBusy, clearFeedback);
    }

    private async Task ExecuteViewTaskAsync(
        Func<Task> execute,
        string errorPrefix,
        bool trackBusy,
        bool clearFeedback)
    {
        if (trackBusy && IsBusy)
        {
            return;
        }

        if (clearFeedback)
        {
            ClearFeedback();
        }

        try
        {
            if (trackBusy)
            {
                IsBusy = true;
            }

            await execute();
        }
        catch (Exception ex)
        {
            SetError($"{errorPrefix}: {ex.Message}");
        }
        finally
        {
            if (trackBusy)
            {
                IsBusy = false;
            }
        }
    }
}
