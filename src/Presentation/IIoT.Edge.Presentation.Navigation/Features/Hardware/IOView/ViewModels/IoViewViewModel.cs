using IIoT.Edge.UI.Shared.Mvvm;
using IIoT.Edge.Application.Features.Hardware.Queries;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Presentation.Navigation;
using IIoT.Edge.UI.Shared.PluginSystem;
using MediatR;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

/// <summary>
/// IO 交互页面视图模型。
/// 负责设备列表加载、IO 映射展示、读缓冲刷新和写缓冲下发。
/// </summary>
public class IoViewViewModel : ViewModelBase
{
    public override string ViewId => InjectionViewIds.IoView;
    public override string ViewTitle => "IO交互";

    private readonly IPlcDataStore _dataStore;
    private readonly ISender _sender;
    private readonly DispatcherTimer _refreshTimer;

    public ObservableCollection<NetworkDeviceEntity> Devices { get; } = new();

    private NetworkDeviceEntity? _selectedDevice;
    public NetworkDeviceEntity? SelectedDevice
    {
        get => _selectedDevice;
        set
        {
            _selectedDevice = value;
            OnPropertyChanged();
            _ = LoadMappingsAsync();
        }
    }

    public ObservableCollection<IoSignalModel> ReadSignals { get; } = new();
    public ObservableCollection<IoSignalModel> WriteSignals { get; } = new();

    private bool _isConnected;
    public bool IsConnected
    {
        get => _isConnected;
        set { _isConnected = value; OnPropertyChanged(); }
    }

    private string _statusText = "未连接";
    public string StatusText
    {
        get => _statusText;
        set { _statusText = value; OnPropertyChanged(); }
    }

    public ICommand WriteCommand { get; }
    public ICommand RefreshDevicesCommand { get; }

    public IoViewViewModel(IPlcDataStore dataStore, ISender sender)
    {
        _dataStore = dataStore;
        _sender = sender;

        WriteCommand = new BaseCommand(ExecuteWrite);
        RefreshDevicesCommand = new AsyncCommand(LoadDevicesAsync);

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _refreshTimer.Tick += OnRefreshTick;
    }

    private async Task LoadDevicesAsync()
    {
        var result = await _sender.Send(new GetAllNetworkDevicesQuery());

        Devices.Clear();
        if (result.IsSuccess && result.Value != null)
        {
            foreach (var d in result.Value.Where(x => x.IsEnabled))
                Devices.Add(d);
        }

        if (Devices.Count > 0 && SelectedDevice is null)
            SelectedDevice = Devices[0];
    }

    private async Task LoadMappingsAsync()
    {
        ReadSignals.Clear();
        WriteSignals.Clear();

        if (SelectedDevice is null) return;

        var result = await _sender.Send(new GetIoMappingsByDeviceQuery(SelectedDevice.Id, 0, int.MaxValue));
        if (!result.IsSuccess || result.Value is null) return;

        int readIdx = 0, writeIdx = 0;
        foreach (var m in result.Value.Items)
        {
            for (int i = 0; i < m.AddressCount; i++)
            {
                var signal = new IoSignalModel
                {
                    Label = m.AddressCount == 1 ? m.Label : $"{m.Label}[{i}]",
                    PlcAddress = m.PlcAddress,
                    Direction = m.Direction,
                    DataType = m.DataType,
                    Value = 0
                };

                if (m.Direction == "Read")
                {
                    signal.BufferIndex = readIdx++;
                    ReadSignals.Add(signal);
                }
                else
                {
                    signal.BufferIndex = writeIdx++;
                    WriteSignals.Add(signal);
                }
            }
        }

        UpdateConnectionStatus();
    }

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (SelectedDevice is null) return;
        var buffer = _dataStore.GetBuffer(SelectedDevice.Id);
        if (buffer is null) return;

        foreach (var s in ReadSignals)
            s.Value = buffer.GetReadValue(s.BufferIndex);

        UpdateConnectionStatus();
    }

    private void ExecuteWrite(object? param)
    {
        if (SelectedDevice is null) return;
        var buffer = _dataStore.GetBuffer(SelectedDevice.Id);
        if (buffer is null) return;

        foreach (var s in WriteSignals)
            buffer.SetWriteValue(s.BufferIndex, (ushort)s.Value);
    }

    private void UpdateConnectionStatus()
    {
        if (SelectedDevice is null)
        {
            IsConnected = false;
            StatusText = "未选择设备";
            return;
        }

        var hasBuffer = _dataStore.HasDevice(SelectedDevice.Id);
        IsConnected = hasBuffer;
        StatusText = hasBuffer ? "已连接" : "未连接";
    }

    public override async Task OnActivatedAsync()
    {
        if (!_refreshTimer.IsEnabled)
        {
            _refreshTimer.Start();
        }

        await LoadDevicesAsync();
    }

    public override Task OnDeactivatedAsync()
    {
        _refreshTimer.Stop();
        return Task.CompletedTask;
    }
}



