using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;

namespace IIoT.Edge.UI.Shared.Avalonia.Controls;

public class EdgeFilterDatePicker : CalendarDatePicker
{
    public static readonly StyledProperty<DateTimeOffset?> SelectedDateOffsetProperty =
        AvaloniaProperty.Register<EdgeFilterDatePicker, DateTimeOffset?>(
            nameof(SelectedDateOffset),
            defaultBindingMode: BindingMode.TwoWay);

    private bool _isSynchronizingDate;

    public EdgeFilterDatePicker()
    {
        Classes.Add("edge-filter-date");
        SelectedDateFormat = CalendarDatePickerFormat.Custom;
        CustomDateFormatString = "yyyy-MM-dd";
        UseFloatingPlaceholder = false;
    }

    protected override Type StyleKeyOverride => typeof(CalendarDatePicker);

    public DateTimeOffset? SelectedDateOffset
    {
        get => GetValue(SelectedDateOffsetProperty);
        set => SetValue(SelectedDateOffsetProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (_isSynchronizingDate)
        {
            return;
        }

        if (change.Property == SelectedDateOffsetProperty)
        {
            SyncSelectedDateFromOffset();
            return;
        }

        if (change.Property == SelectedDateProperty)
        {
            SyncOffsetFromSelectedDate();
        }
    }

    private void SyncSelectedDateFromOffset()
    {
        try
        {
            _isSynchronizingDate = true;
            SetCurrentValue(SelectedDateProperty, SelectedDateOffset?.DateTime.Date);
        }
        finally
        {
            _isSynchronizingDate = false;
        }
    }

    private void SyncOffsetFromSelectedDate()
    {
        try
        {
            _isSynchronizingDate = true;
            SetCurrentValue(SelectedDateOffsetProperty, SelectedDate is null ? null : new DateTimeOffset(SelectedDate.Value.Date));
        }
        finally
        {
            _isSynchronizingDate = false;
        }
    }
}
