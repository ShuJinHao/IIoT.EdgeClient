using System.Collections.ObjectModel;
using IIoT.Edge.UI.Shared.Mvvm;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public sealed class IoDataSectionModel : BaseNotifyPropertyChanged
{
    private static readonly HashSet<string> GenericCategories =
    [
        "单点读数据",
        "连续读数据"
    ];

    public string Category { get; init; } = "单点读数据";

    public string GroupName { get; init; } = string.Empty;

    public int SortOrder { get; set; }

    public string Title
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GroupName)
                || string.Equals(Category, GroupName, StringComparison.OrdinalIgnoreCase))
            {
                return LocalizeCategory(Category);
            }

            return GenericCategories.Contains(Category)
                ? GroupName
                : $"{LocalizeCategory(Category)} - {GroupName}";
        }
    }

    public ObservableCollection<IoSignalModel> Signals { get; } = [];

    public void NotifyLocalizationChanged()
        => OnPropertyChanged(nameof(Title));

    private static string LocalizeCategory(string category)
        => category switch
        {
            "单点读数据" => GetText("Navigation_Io_Category_SingleRead", category),
            "连续读数据" => GetText("Navigation_Io_Category_ContinuousRead", category),
            _ => category
        };

    private static string GetText(string key, string fallback)
        => System.Windows.Application.Current?.TryFindResource(key) as string ?? fallback;
}
