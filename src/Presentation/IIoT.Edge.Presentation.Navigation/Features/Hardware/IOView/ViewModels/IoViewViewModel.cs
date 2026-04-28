using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using System.Windows.Threading;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Presentation.Navigation.Localization;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.UI.Shared.Localization;
using IIoT.Edge.UI.Shared.Mvvm;
using MediatR;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public class IoViewViewModel : NavigationViewModelBase
{
    private const string InteractionCategory = "信号交互";
    private const string SingleReadCategory = "单点读数据";
    private const string ContinuousReadCategory = "连续读数据";

    private readonly IPlcDataStore _dataStore;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly ISender _sender;
    private readonly DispatcherTimer _refreshTimer;
    private readonly string? _moduleIdFilter;

    public ObservableCollection<NetworkDeviceEntity> Devices { get; } = [];

    public ObservableCollection<IoInteractionRowModel> InteractionRows { get; } = [];

    public ObservableCollection<IoDataSectionModel> DataSections { get; } = [];

    public ObservableCollection<IoContinuousReadMatrixSectionModel> ArraySections { get; } = [];

    public bool HasInteractionRows => InteractionRows.Count > 0;

    public bool HasDataSections => DataSections.Count > 0;

    public bool HasArraySections => ArraySections.Count > 0;

    public bool HasNoSignals => !HasInteractionRows && !HasDataSections && !HasArraySections && SelectedDevice is not null;

    private NetworkDeviceEntity? _selectedDevice;
    public NetworkDeviceEntity? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            if (_selectedDevice?.Id == value?.Id)
            {
                return;
            }

            _selectedDevice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedDevice));
            _ = LoadMappingsAsync();
        }
    }

    public bool HasSelectedDevice => SelectedDevice is not null;

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (_isConnected == value)
            {
                return;
            }

            _isConnected = value;
            OnPropertyChanged();
        }
    }

    public ICommand RefreshDevicesCommand { get; }

    public IoViewViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        ISender sender,
        IAppLanguageService languageService)
        : this(
            dataStore,
            plcConnectionManager,
            sender,
            languageService,
            "Hardware.IOView",
            "Navigation_Title_IoInteract",
            "IO 交互",
            moduleIdFilter: null)
    {
    }

    public IoViewViewModel(
        IPlcDataStore dataStore,
        IPlcConnectionManager plcConnectionManager,
        ISender sender,
        IAppLanguageService languageService,
        string viewId,
        string titleResourceKey,
        string titleFallback,
        string? moduleIdFilter = null)
        : base(languageService, viewId, titleResourceKey, titleFallback)
    {
        _dataStore = dataStore;
        _plcConnectionManager = plcConnectionManager;
        _sender = sender;
        _moduleIdFilter = moduleIdFilter;

        RefreshDevicesCommand = new AsyncCommand(LoadDevicesAsync);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _refreshTimer.Tick += OnRefreshTick;
    }

    public async Task LoadDevicesAsync()
    {
        var selectedDeviceId = SelectedDevice?.Id;
        var result = await _sender.Send(new GetAllNetworkDevicesQuery());

        Devices.Clear();
        if (result.IsSuccess && result.Value != null)
        {
            foreach (var device in result.Value
                         .Where(IsVisibleDevice)
                         .OrderBy(static x => x.DeviceName, StringComparer.OrdinalIgnoreCase))
            {
                Devices.Add(device);
            }
        }

        var nextSelected = selectedDeviceId is null
            ? Devices.FirstOrDefault()
            : Devices.FirstOrDefault(x => x.Id == selectedDeviceId.Value) ?? Devices.FirstOrDefault();

        if (SelectedDevice?.Id != nextSelected?.Id)
        {
            SelectedDevice = nextSelected;
            return;
        }

        await LoadMappingsAsync().ConfigureAwait(false);
        UpdateConnectionStatus();
    }

    public async Task LoadMappingsAsync()
    {
        InteractionRows.Clear();
        DataSections.Clear();
        ArraySections.Clear();
        NotifySignalCollectionsChanged();

        if (SelectedDevice is null)
        {
            UpdateConnectionStatus();
            return;
        }

        var result = await _sender.Send(new GetIoMappingsByDeviceQuery(SelectedDevice.Id, 0, int.MaxValue));
        if (!result.IsSuccess || result.Value is null)
        {
            UpdateConnectionStatus();
            return;
        }

        var readIndex = 0;
        var writeIndex = 0;
        var interactionRows = new Dictionary<string, IoInteractionRowModel>(StringComparer.OrdinalIgnoreCase);
        var dataSections = new Dictionary<string, IoDataSectionModel>(StringComparer.OrdinalIgnoreCase);
        var arraySections = new Dictionary<string, IoContinuousReadMatrixSectionModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in result.Value.Items.OrderBy(static x => x.SortOrder))
        {
            var isRead = string.Equals(mapping.Direction, "Read", StringComparison.OrdinalIgnoreCase);
            var signal = CreateSignal(mapping, isRead ? readIndex : writeIndex);

            if (isRead)
            {
                readIndex += Math.Max(1, mapping.AddressCount);
            }
            else
            {
                writeIndex += Math.Max(1, mapping.AddressCount);
            }

            var category = ResolveCategory(mapping);
            if (string.Equals(category, InteractionCategory, StringComparison.OrdinalIgnoreCase))
            {
                var row = GetOrCreateInteractionRow(interactionRows, mapping);
                row.SortOrder = Math.Min(row.SortOrder, mapping.SortOrder);
                row.WriteCommand ??= new BaseCommand(_ => WriteInteractionRow(row), _ => row.CanWrite);

                if (isRead)
                {
                    row.AddPlcSignal(signal);
                }
                else
                {
                    row.AddHostSignal(signal);
                }

                continue;
            }

            if (IsArrayMatrixSignal(signal))
            {
                var arraySection = GetOrCreateArraySection(arraySections, mapping, category);
                arraySection.SortOrder = Math.Min(arraySection.SortOrder, mapping.SortOrder);
                arraySection.Columns.Add(signal);
                continue;
            }

            var section = GetOrCreateDataSection(dataSections, mapping, category);
            section.SortOrder = Math.Min(section.SortOrder, mapping.SortOrder);
            section.Signals.Add(signal);
        }

        foreach (var row in interactionRows.Values
                     .OrderBy(static x => x.SortOrder)
                     .ThenBy(static x => x.GroupName, StringComparer.OrdinalIgnoreCase))
        {
            InteractionRows.Add(row);
        }

        foreach (var section in dataSections.Values
                     .OrderBy(static x => x.SortOrder)
                     .ThenBy(static x => x.GroupName, StringComparer.OrdinalIgnoreCase))
        {
            DataSections.Add(section);
        }

        foreach (var section in arraySections.Values
                     .OrderBy(static x => x.SortOrder)
                     .ThenBy(static x => x.GroupName, StringComparer.OrdinalIgnoreCase))
        {
            ArraySections.Add(section);
        }

        RefreshCurrentValues();
        NotifySignalCollectionsChanged();
        UpdateConnectionStatus();
    }

    public void RefreshCurrentValues()
    {
        if (SelectedDevice is null)
        {
            UpdateConnectionStatus();
            return;
        }

        var buffer = _dataStore.GetBuffer(SelectedDevice.Id);
        if (buffer is null)
        {
            UpdateConnectionStatus();
            return;
        }

        var writeSnapshot = buffer.GetWriteBuffer();

        foreach (var row in InteractionRows)
        {
            foreach (var signal in row.PlcSignals)
            {
                UpdateReadSignal(signal, buffer);
            }

            foreach (var signal in row.HostSignals)
            {
                UpdateWriteSignal(signal, writeSnapshot);
            }

            row.InitializeWriteValueFromCurrentBuffer();
            row.NotifyValuesChanged();
        }

        foreach (var signal in DataSections.SelectMany(static section => section.Signals))
        {
            UpdateReadSignal(signal, buffer);
        }

        foreach (var section in ArraySections)
        {
            foreach (var signal in section.Columns)
            {
                UpdateReadSignal(signal, buffer);
            }

            section.RebuildRows();
        }

        UpdateConnectionStatus();
    }

    private bool IsVisibleDevice(NetworkDeviceEntity device)
    {
        if (!device.IsEnabled || device.DeviceType != DeviceType.PLC)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(_moduleIdFilter)
            || string.Equals(device.ModuleId, _moduleIdFilter, StringComparison.OrdinalIgnoreCase);
    }

    private static IoInteractionRowModel GetOrCreateInteractionRow(
        IDictionary<string, IoInteractionRowModel> rows,
        IoMappingEntity mapping)
    {
        var groupName = ResolveGroupName(mapping, InteractionCategory);
        if (rows.TryGetValue(groupName, out var row))
        {
            return row;
        }

        row = new IoInteractionRowModel
        {
            GroupName = groupName,
            SortOrder = mapping.SortOrder
        };
        rows.Add(groupName, row);
        return row;
    }

    private static IoDataSectionModel GetOrCreateDataSection(
        IDictionary<string, IoDataSectionModel> sections,
        IoMappingEntity mapping,
        string category)
    {
        var groupName = ResolveGroupName(mapping, category);
        var key = $"{category}|{groupName}";
        if (sections.TryGetValue(key, out var section))
        {
            return section;
        }

        section = new IoDataSectionModel
        {
            Category = category,
            GroupName = groupName,
            SortOrder = mapping.SortOrder
        };
        sections.Add(key, section);
        return section;
    }

    private static IoContinuousReadMatrixSectionModel GetOrCreateArraySection(
        IDictionary<string, IoContinuousReadMatrixSectionModel> sections,
        IoMappingEntity mapping,
        string category)
    {
        var groupName = ResolveGroupName(mapping, category);
        var key = $"{category}|{groupName}";
        if (sections.TryGetValue(key, out var section))
        {
            return section;
        }

        section = new IoContinuousReadMatrixSectionModel
        {
            Category = category,
            GroupName = groupName,
            SortOrder = mapping.SortOrder
        };
        sections.Add(key, section);
        return section;
    }

    private static IoSignalModel CreateSignal(IoMappingEntity mapping, int startIndex)
    {
        var displayName = string.IsNullOrWhiteSpace(mapping.Remark)
            ? mapping.Label
            : mapping.Remark.Trim();

        return new IoSignalModel
        {
            Label = displayName,
            RawLabel = mapping.Label,
            PlcAddress = mapping.PlcAddress,
            Direction = mapping.Direction,
            DisplayRole = mapping.DisplayRole,
            DataType = mapping.DataType,
            StartIndex = startIndex,
            AddressCount = Math.Max(1, mapping.AddressCount),
            SortOrder = mapping.SortOrder
        };
    }

    private static string ResolveCategory(IoMappingEntity mapping)
    {
        if (!string.IsNullOrWhiteSpace(mapping.Category))
        {
            return mapping.Category.Trim();
        }

        return mapping.AddressCount > 1 ? ContinuousReadCategory : SingleReadCategory;
    }

    private static string ResolveGroupName(IoMappingEntity mapping, string category)
        => string.IsNullOrWhiteSpace(mapping.GroupName)
            ? category
            : mapping.GroupName.Trim();

    private static bool IsArrayMatrixSignal(IoSignalModel signal)
        => signal.AddressCount > 1
            && !string.Equals(signal.DataType, "Ascii", StringComparison.OrdinalIgnoreCase);

    private static void UpdateReadSignal(IoSignalModel signal, IPlcBuffer buffer)
    {
        var words = ReadWords(signal, index => buffer.GetReadValue(index));
        ApplyDecodedValue(signal, words);
    }

    private static void UpdateWriteSignal(IoSignalModel signal, IReadOnlyList<ushort> writeSnapshot)
    {
        var words = ReadWords(signal, index => index >= 0 && index < writeSnapshot.Count ? writeSnapshot[index] : (ushort)0);
        ApplyDecodedValue(signal, words);
    }

    private static ushort[] ReadWords(IoSignalModel signal, Func<int, ushort> read)
    {
        var words = new ushort[Math.Max(1, signal.AddressCount)];
        for (var offset = 0; offset < words.Length; offset++)
        {
            words[offset] = read(signal.StartIndex + offset);
        }

        return words;
    }

    private static void ApplyDecodedValue(IoSignalModel signal, IReadOnlyList<ushort> words)
    {
        var values = DecodeWords(signal.DataType, words);
        var display = values.Count == 0 ? string.Empty : string.Join("，", values);
        var preview = values.Count <= 8
            ? display
            : $"{string.Join("，", values.Take(8))} ...";

        signal.DisplayValue = string.IsNullOrWhiteSpace(display) ? "-" : display;
        signal.PreviewValue = string.IsNullOrWhiteSpace(preview) ? "-" : preview;
        signal.Value = DecodeSingleEditValue(signal.DataType, words);
        signal.ExpandedValues.Clear();

        if (signal.IsContinuous
            && values.Count > 0
            && !string.Equals(signal.DataType, "Ascii", StringComparison.OrdinalIgnoreCase))
        {
            for (var index = 0; index < values.Count; index++)
            {
                signal.ExpandedValues.Add(new IoSignalValueModel
                {
                    Index = index + 1,
                    Value = values[index]
                });
            }
        }

        signal.OnPropertyChanged(nameof(IoSignalModel.HasExpandedValues));
    }

    private static int DecodeSingleEditValue(string dataType, IReadOnlyList<ushort> words)
    {
        if (words.Count == 0)
        {
            return 0;
        }

        return string.Equals(dataType, "Int16", StringComparison.OrdinalIgnoreCase)
            ? unchecked((short)words[0])
            : words[0];
    }

    private static IReadOnlyList<string> DecodeWords(string dataType, IReadOnlyList<ushort> words)
    {
        var normalizedType = (dataType ?? string.Empty).Trim();
        if (string.Equals(normalizedType, "Ascii", StringComparison.OrdinalIgnoreCase))
        {
            return [DecodeAscii(words)];
        }

        if (string.Equals(normalizedType, "Float", StringComparison.OrdinalIgnoreCase))
        {
            var values = new List<string>();
            for (var index = 0; index + 1 < words.Count; index += 2)
            {
                values.Add(CombineToFloat(words[index + 1], words[index]).ToString("0.###", CultureInfo.InvariantCulture));
            }

            return values;
        }

        if (string.Equals(normalizedType, "Bool", StringComparison.OrdinalIgnoreCase))
        {
            return words.Select(static word => word == 0 ? "False" : "True").ToArray();
        }

        if (string.Equals(normalizedType, "Int16", StringComparison.OrdinalIgnoreCase))
        {
            return words.Select(static word => unchecked((short)word).ToString(CultureInfo.InvariantCulture)).ToArray();
        }

        return words.Select(static word => word.ToString(CultureInfo.InvariantCulture)).ToArray();
    }

    private static string DecodeAscii(IReadOnlyList<ushort> words)
    {
        var builder = new StringBuilder(words.Count * 2);
        foreach (var word in words)
        {
            var low = (byte)(word & 0xFF);
            var high = (byte)(word >> 8);

            if (low != 0)
            {
                builder.Append((char)low);
            }

            if (high != 0)
            {
                builder.Append((char)high);
            }
        }

        return builder.ToString().Trim();
    }

    private static float CombineToFloat(ushort high, ushort low)
    {
        byte[] bytes =
        [
            (byte)(high >> 8),
            (byte)high,
            (byte)(low >> 8),
            (byte)low
        ];

        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        return BitConverter.ToSingle(bytes, 0);
    }

    private void WriteInteractionRow(IoInteractionRowModel row)
    {
        if (SelectedDevice is null || row.HostSignals.Count == 0)
        {
            return;
        }

        var buffer = _dataStore.GetBuffer(SelectedDevice.Id);
        if (buffer is null)
        {
            return;
        }

        var displayValue = row.WriteValue.ToString(CultureInfo.InvariantCulture);
        foreach (var signal in row.HostSignals)
        {
            buffer.SetWriteValue(signal.StartIndex, unchecked((ushort)row.WriteValue));
            signal.DisplayValue = displayValue;
            signal.PreviewValue = displayValue;
        }

        row.NotifyValuesChanged();
    }

    private void OnRefreshTick(object? sender, EventArgs e)
        => RefreshCurrentValues();

    private void UpdateConnectionStatus()
    {
        if (SelectedDevice is null)
        {
            IsConnected = false;
            return;
        }

        var isConnected = _plcConnectionManager.GetRuntimeStatus(SelectedDevice.Id)?.IsConnected == true;
        IsConnected = isConnected;
    }

    private void NotifySignalCollectionsChanged()
    {
        OnPropertyChanged(nameof(HasInteractionRows));
        OnPropertyChanged(nameof(HasDataSections));
        OnPropertyChanged(nameof(HasArraySections));
        OnPropertyChanged(nameof(HasNoSignals));
    }

    protected override void RefreshLocalization()
    {
        base.RefreshLocalization();
        foreach (var section in DataSections)
        {
            section.NotifyLocalizationChanged();
            foreach (var signal in section.Signals)
            {
                signal.NotifyLocalizationChanged();
            }
        }

        foreach (var section in ArraySections)
        {
            section.NotifyLocalizationChanged();
            foreach (var signal in section.Columns)
            {
                signal.NotifyLocalizationChanged();
            }
        }

        foreach (var signal in InteractionRows.SelectMany(row => row.PlcSignals.Concat(row.HostSignals)))
        {
            signal.NotifyLocalizationChanged();
        }
    }

    public override async Task OnActivatedAsync()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }

        await LoadDevicesAsync().ConfigureAwait(false);
    }

    public override Task OnDeactivatedAsync()
    {
        _refreshTimer.Stop();
        return Task.CompletedTask;
    }
}
