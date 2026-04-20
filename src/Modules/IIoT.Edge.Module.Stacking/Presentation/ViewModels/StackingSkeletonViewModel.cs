using IIoT.Edge.Application.Abstractions.Context;
using IIoT.Edge.Application.Abstractions.DataPipeline.Stores;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Domain.Hardware.Aggregates;
using IIoT.Edge.Module.Stacking.Constants;
using IIoT.Edge.Module.Stacking.Payload;
using IIoT.Edge.SharedKernel.Enums;
using IIoT.Edge.SharedKernel.Repository;
using IIoT.Edge.UI.Shared.PluginSystem;
using Microsoft.Extensions.Configuration;
using System.Windows.Threading;

namespace IIoT.Edge.Module.Stacking.Presentation.ViewModels;

public sealed class StackingSkeletonViewModel : PresentationViewModelBase
{
    private readonly IConfiguration _configuration;
    private readonly IReadRepository<NetworkDeviceEntity> _networkDevices;
    private readonly IProductionContextStore _contextStore;
    private readonly IStationRuntimeRegistry _runtimeRegistry;
    private readonly ICloudRetryRecordStore _cloudRetryRecordStore;
    private readonly DispatcherTimer _refreshTimer;

    public override string ViewId => StackingViewIds.PlaceholderDashboard;

    public override string ViewTitle => "Stacking Skeleton";

    public string ModuleId => StackingModuleConstants.ModuleId;

    public string EnablementHint => "Enable this module only in Development or test configurations.";

    public string RuntimeHint => "The first Stacking runtime slice captures a minimal PLC signal sample and pushes it into the shared production context.";

    private string _moduleStatus = "Checking module configuration...";
    public string ModuleStatus
    {
        get => _moduleStatus;
        private set
        {
            _moduleStatus = value;
            OnPropertyChanged();
        }
    }

    private string _deviceBindingStatus = "Checking device bindings...";
    public string DeviceBindingStatus
    {
        get => _deviceBindingStatus;
        private set
        {
            _deviceBindingStatus = value;
            OnPropertyChanged();
        }
    }

    private string _runtimeBindingStatus = "Checking runtime registration...";
    public string RuntimeBindingStatus
    {
        get => _runtimeBindingStatus;
        private set
        {
            _runtimeBindingStatus = value;
            OnPropertyChanged();
        }
    }

    private string _sampleDataSummary = "Checking runtime sample data...";
    public string SampleDataSummary
    {
        get => _sampleDataSummary;
        private set
        {
            _sampleDataSummary = value;
            OnPropertyChanged();
        }
    }

    private string _cloudUploadStatus = "Checking cloud upload switch...";
    public string CloudUploadStatus
    {
        get => _cloudUploadStatus;
        private set
        {
            _cloudUploadStatus = value;
            OnPropertyChanged();
        }
    }

    private string _cloudUploadResultStatus = "Checking last cloud upload result...";
    public string CloudUploadResultStatus
    {
        get => _cloudUploadResultStatus;
        private set
        {
            _cloudUploadResultStatus = value;
            OnPropertyChanged();
        }
    }

    private string _cloudUploadFailureStatus = "Checking last cloud failure...";
    public string CloudUploadFailureStatus
    {
        get => _cloudUploadFailureStatus;
        private set
        {
            _cloudUploadFailureStatus = value;
            OnPropertyChanged();
        }
    }

    private string _cloudRetryStatus = "Checking pending cloud retries...";
    public string CloudRetryStatus
    {
        get => _cloudRetryStatus;
        private set
        {
            _cloudRetryStatus = value;
            OnPropertyChanged();
        }
    }

    public StackingSkeletonViewModel(
        IConfiguration configuration,
        IReadRepository<NetworkDeviceEntity> networkDevices,
        IProductionContextStore contextStore,
        IStationRuntimeRegistry runtimeRegistry,
        ICloudRetryRecordStore cloudRetryRecordStore)
    {
        _configuration = configuration;
        _networkDevices = networkDevices;
        _contextStore = contextStore;
        _runtimeRegistry = runtimeRegistry;
        _cloudRetryRecordStore = cloudRetryRecordStore;

        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (_, _) => RunViewTaskInBackground(RefreshAsync, "Stacking diagnostics refresh failed");
        _refreshTimer.Start();
    }

    public override async Task OnActivatedAsync()
    {
        await RunViewTaskAsync(RefreshAsync, "Stacking diagnostics load failed");
    }

    private async Task RefreshAsync()
    {
        var enabledModuleIds = _configuration
            .GetSection("Modules:Enabled")
            .GetChildren()
            .Select(x => x.Value)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Cast<string>()
            .ToArray();

        var isEnabled = enabledModuleIds.Contains(StackingModuleConstants.ModuleId, StringComparer.OrdinalIgnoreCase);
        ModuleStatus = isEnabled
            ? "Enabled in the current configuration."
            : "Not enabled in the current configuration.";

        var device = await _networkDevices.GetAsync(
            x => x.DeviceType == DeviceType.PLC
                && x.ModuleId == StackingModuleConstants.ModuleId);

        DeviceBindingStatus = device is null
            ? "No Stacking PLC device is configured yet."
            : $"Bound device: {device.DeviceName} ({device.IpAddress}:{device.Port1})";

        if (!_runtimeRegistry.HasFactory(StackingModuleConstants.ModuleId))
        {
            RuntimeBindingStatus = "Runtime factory is not registered.";
        }
        else if (device is null)
        {
            RuntimeBindingStatus = $"Factory registered: {StackingModuleConstants.RuntimeTaskName}. Waiting for a bound device.";
        }
        else
        {
            var ctx = _contextStore.GetAll().FirstOrDefault(x => x.DeviceName == device.DeviceName);
            var runtimeMarkedActive = ctx?.Get<bool>(StackingModuleConstants.RuntimeRegisteredKey) == true;
            RuntimeBindingStatus = runtimeMarkedActive
                ? $"Factory registered and runtime context is active: {StackingModuleConstants.RuntimeTaskName}."
                : $"Factory registered: {StackingModuleConstants.RuntimeTaskName}. Waiting for PLC startup.";
        }

        var targetContext = device is null
            ? null
            : _contextStore.GetAll().FirstOrDefault(x => x.DeviceName == device.DeviceName);

        var cloudUploadEnabled = bool.TryParse(
            _configuration["Modules:Stacking:CloudUploadEnabled"],
            out var enabledValue)
            && enabledValue;
        CloudUploadStatus = cloudUploadEnabled
            ? "Cloud upload is enabled for the Stacking development module."
            : "Cloud upload is disabled for the Stacking module in the current configuration.";

        var latestCell = targetContext?.CurrentCells.Values
            .OfType<StackingCellData>()
            .OrderByDescending(x => x.SequenceNo)
            .ThenByDescending(x => x.CompletedTime)
            .FirstOrDefault();

        if (latestCell is not null)
        {
            var result = latestCell.CellResult switch
            {
                true => "OK",
                false => "NG",
                _ => "Unknown"
            };

            SampleDataSummary =
                $"Latest sample: {latestCell.Barcode}, Tray:{latestCell.TrayCode}, Layers:{latestCell.LayerCount}, Result:{result}, Status:{latestCell.RuntimeStatus}.";
        }
        else
        {
            SampleDataSummary = "No Stacking sample cell is available yet.";
        }

        var lastCloudStatus = targetContext?.Get<string>(StackingModuleConstants.LastCloudUploadStatusKey);
        var lastCloudAt = targetContext?.Get<DateTime>(StackingModuleConstants.LastCloudUploadAtKey);
        CloudUploadResultStatus = string.IsNullOrWhiteSpace(lastCloudStatus)
            ? "No cloud upload attempt has been recorded yet."
            : lastCloudAt.HasValue
                ? $"Last result: {lastCloudStatus} at {lastCloudAt.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}."
                : $"Last result: {lastCloudStatus}.";

        var lastCloudError = targetContext?.Get<string>(StackingModuleConstants.LastCloudUploadErrorKey);
        CloudUploadFailureStatus = string.IsNullOrWhiteSpace(lastCloudError)
            ? "No cloud upload failure is currently recorded."
            : $"Last failure: {lastCloudError}";

        var pendingCloudRetries = await _cloudRetryRecordStore.GetCountAsync(
            StackingModuleConstants.ProcessType);
        CloudRetryStatus = $"Pending Cloud retry records for Stacking: {pendingCloudRetries}.";

        SetStatus("Development diagnostics are active for the Stacking module.");
    }
}
