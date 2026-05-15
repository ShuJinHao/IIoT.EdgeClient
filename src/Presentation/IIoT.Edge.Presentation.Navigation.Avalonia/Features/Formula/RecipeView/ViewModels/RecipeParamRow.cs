using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace IIoT.Edge.Presentation.Navigation.Avalonia.Features.Formula.RecipeView;

public sealed partial class RecipeParamRow : ObservableObject
{
    private readonly Func<string, Task> _deleteAsync;
    private readonly AsyncRelayCommand _deleteCommand;

    public RecipeParamRow(
        string name,
        string min,
        string max,
        string unit,
        bool canDelete,
        Func<string, Task> deleteAsync)
    {
        Name = name;
        Min = min;
        Max = max;
        Unit = unit;
        this.canDelete = canDelete;
        _deleteAsync = deleteAsync;
        _deleteCommand = new AsyncRelayCommand(DeleteAsync, () => CanDelete);
    }

    public string Name { get; }

    public string Min { get; }

    public string Max { get; }

    public string Unit { get; }

    [ObservableProperty]
    private bool canDelete;

    public IAsyncRelayCommand DeleteCommand => _deleteCommand;

    partial void OnCanDeleteChanged(bool value)
        => _deleteCommand.NotifyCanExecuteChanged();

    private Task DeleteAsync()
        => _deleteAsync(Name);
}
