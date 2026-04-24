using System.Collections.ObjectModel;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public sealed class IoDataSectionModel
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
                return Category;
            }

            return GenericCategories.Contains(Category)
                ? GroupName
                : $"{Category} - {GroupName}";
        }
    }

    public ObservableCollection<IoSignalModel> Signals { get; } = [];
}
