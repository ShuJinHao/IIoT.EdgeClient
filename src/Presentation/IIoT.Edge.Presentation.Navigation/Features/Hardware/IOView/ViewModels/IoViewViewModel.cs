using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Windows.Input;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;
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
    private readonly IPlcDataStore _dataStore;
    private readonly IPlcConnectionManager _plcConnectionManager;
    private readonly ISender _sender;
    private readonly AsyncCommand _manualReadCommand;
    private readonly string? _moduleIdFilter;
    private IPlcBufferTransport? _selectedBuffer;

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

            UnbindSelectedBuffer();
            _selectedDevice = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasSelectedDevice));
            _manualReadCommand.RaiseCanExecuteChanged();
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

    public ICommand ManualReadCommand => _manualReadCommand;

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
        _manualReadCommand = new AsyncCommand(ManualReadSelectedDataAsync, () => SelectedDevice is not null);
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

        await LoadMappingsAsync();
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
            UnbindSelectedBuffer();
            UpdateConnectionStatus();
            return;
        }

        var result = await _sender.Send(new GetIoMappingsByDeviceQuery(SelectedDevice.Id, 0, int.MaxValue));
        if (!result.IsSuccess || result.Value is null)
        {
            BindSelectedBuffer();
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
            var isRead = string.Equals(mapping.Direction, IoMappingOptionCatalog.DirectionRead, StringComparison.OrdinalIgnoreCase);
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
            if (string.Equals(category, IoMappingDisplay.InteractionCategory, StringComparison.OrdinalIgnoreCase))
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

            if (IoMappingDisplay.IsContinuousMatrix(signal.DataType, signal.AddressCount))
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
                     .ThenBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase))
        {
            InteractionRows.Add(row);
        }

        foreach (var section in dataSections.Values
                     .OrderBy(static x => x.SortOrder)
                     .ThenBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase))
        {
            DataSections.Add(section);
        }

        foreach (var section in arraySections.Values
                     .OrderBy(static x => x.SortOrder)
                     .ThenBy(static x => x.BusinessGroup, StringComparer.OrdinalIgnoreCase))
        {
            ArraySections.Add(section);
        }

        BindSelectedBuffer();
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

        foreach (var row in InteractionRows)
        {
            foreach (var signal in row.PlcSignals)
            {
                UpdateReadSignal(signal, buffer);
            }

            foreach (var signal in row.HostSignals)
            {
                UpdateWriteSignal(signal, buffer);
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

    private async Task ManualReadSelectedDataAsync()
    {
        if (SelectedDevice is null)
        {
            return;
        }

        var plc = _plcConnectionManager.GetPlc(SelectedDevice.Id);
        var buffer = _dataStore.GetBuffer(SelectedDevice.Id);
        if (plc is null || buffer is null)
        {
            UpdateConnectionStatus();
            return;
        }

        try
        {
            foreach (var signal in DataSections.SelectMany(static section => section.Signals)
                         .Concat(ArraySections.SelectMany(static section => section.Columns)))
            {
                var length = checked((ushort)Math.Max(1, signal.AddressCount));
                var words = await plc.ReadDataAsync<ushort>(signal.PlcAddress, length);
                buffer.UpdateReadSignal(signal.SignalKey, words);
            }

            RefreshCurrentValues();
            ClearFeedback();
        }
        catch (Exception ex)
        {
            SetError($"读取 IO 数据失败：{ex.Message}");
        }
        finally
        {
            UpdateConnectionStatus();
        }
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
        var businessGroup = ResolveBusinessGroup(mapping, IoMappingDisplay.InteractionCategory);
        if (rows.TryGetValue(businessGroup, out var row))
        {
            return row;
        }

        row = new IoInteractionRowModel
        {
            BusinessGroup = businessGroup,
            SortOrder = mapping.SortOrder
        };
        rows.Add(businessGroup, row);
        return row;
    }

    private static IoDataSectionModel GetOrCreateDataSection(
        IDictionary<string, IoDataSectionModel> sections,
        IoMappingEntity mapping,
        string category)
    {
        var businessGroup = ResolveBusinessGroup(mapping, category);
        var key = category;
        if (sections.TryGetValue(key, out var section))
        {
            return section;
        }

        section = new IoDataSectionModel
        {
            Category = category,
            BusinessGroup = businessGroup,
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
        var businessGroup = ResolveBusinessGroup(mapping, category);
        var key = category;
        if (sections.TryGetValue(key, out var section))
        {
            return section;
        }

        section = new IoContinuousReadMatrixSectionModel
        {
            Category = category,
            BusinessGroup = businessGroup,
            SortOrder = mapping.SortOrder
        };
        sections.Add(key, section);
        return section;
    }

    private static IoSignalModel CreateSignal(IoMappingEntity mapping, int startIndex)
    {
        return new IoSignalModel
        {
            SignalKey = mapping.SignalKey,
            PlcAddress = mapping.PlcAddress,
            Direction = mapping.Direction,
            SignalName = mapping.SignalName,
            Remark = mapping.Remark,
            DataType = mapping.DataType,
            StartIndex = startIndex,
            AddressCount = Math.Max(1, mapping.AddressCount),
            SortOrder = mapping.SortOrder
        };
    }

    private static string ResolveCategory(IoMappingEntity mapping)
        => IoMappingDisplay.ResolveCategory(mapping.Category, mapping.AddressCount);

    private static string ResolveBusinessGroup(IoMappingEntity mapping, string category)
        => IoMappingDisplay.ResolveBusinessGroup(mapping.BusinessGroup, category);

    private static void UpdateReadSignal(IoSignalModel signal, IPlcBuffer buffer)
    {
        var words = buffer.TryGetReadWords(signal.SignalKey, out var signalWords)
            ? EnsureLength(signalWords, signal.AddressCount)
            : ReadWords(signal, index => buffer.GetReadValue(index));
        ApplyDecodedValue(signal, words);
    }

    private static void UpdateWriteSignal(IoSignalModel signal, IPlcBuffer buffer)
    {
        var words = buffer.TryGetWriteWords(signal.SignalKey, out var signalWords)
            ? EnsureLength(signalWords, signal.AddressCount)
            : ReadWords(signal, index => buffer.GetWriteBufferValue(index));
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

    private static ushort[] EnsureLength(IReadOnlyList<ushort> source, int addressCount)
    {
        var length = Math.Max(1, addressCount);
        if (source.Count == length && source is ushort[] array)
        {
            return array;
        }

        var words = new ushort[length];
        for (var index = 0; index < words.Length && index < source.Count; index++)
        {
            words[index] = source[index];
        }

        return words;
    }

    private static void ApplyDecodedValue(IoSignalModel signal, IReadOnlyList<ushort> words)
    {
        var values = DecodeWords(signal.DataType, words);
        var display = values.Count == 0 ? string.Empty : string.Join(", ", values);
        var preview = values.Count <= 8
            ? display
            : $"{string.Join(", ", values.Take(8))} ...";

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
            buffer.SetWriteValue(signal.SignalKey, 0, unchecked((ushort)row.WriteValue));
            buffer.SetWriteValue(signal.StartIndex, unchecked((ushort)row.WriteValue));
            signal.DisplayValue = displayValue;
            signal.PreviewValue = displayValue;
        }

        row.NotifyValuesChanged();
    }

    private void BindSelectedBuffer()
    {
        UnbindSelectedBuffer();
        if (SelectedDevice is null)
        {
            return;
        }

        _selectedBuffer = _dataStore.GetBuffer(SelectedDevice.Id);
        if (_selectedBuffer is not null)
        {
            _selectedBuffer.SignalValuesChanged += OnBufferSignalValuesChanged;
        }
    }

    private void UnbindSelectedBuffer()
    {
        if (_selectedBuffer is not null)
        {
            _selectedBuffer.SignalValuesChanged -= OnBufferSignalValuesChanged;
            _selectedBuffer = null;
        }
    }

    private void OnBufferSignalValuesChanged(object? sender, PlcSignalBufferChangedEventArgs e)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            RefreshCurrentValues();
            return;
        }

        dispatcher.BeginInvoke(new Action(RefreshCurrentValues));
    }

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

        foreach (var row in InteractionRows)
        {
            row.NotifyLocalizationChanged();
            foreach (var signal in row.PlcSignals.Concat(row.HostSignals))
            {
                signal.NotifyLocalizationChanged();
            }
        }
    }

    public override async Task OnActivatedAsync()
        => await LoadDevicesAsync();

    public override Task OnDeactivatedAsync()
    {
        UnbindSelectedBuffer();
        return Task.CompletedTask;
    }
}

internal static class PlcBufferReadExtensions
{
    public static ushort GetWriteBufferValue(this IPlcBuffer buffer, int index)
    {
        if (buffer is not IPlcBufferTransport transport)
        {
            return 0;
        }

        var snapshot = transport.GetWriteBuffer();
        return index >= 0 && index < snapshot.Length ? snapshot[index] : (ushort)0;
    }
}
