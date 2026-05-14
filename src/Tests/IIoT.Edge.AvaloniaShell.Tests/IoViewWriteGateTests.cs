using IIoT.Edge.Application.Abstractions.Auth;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Common.Models;
using IIoT.Edge.Presentation.Navigation.Avalonia.Features.Hardware.IOView;
using IIoT.Edge.SharedKernel.Context;
using IIoT.Edge.UI.Avalonia.Localization;
using IIoT.Edge.UI.Avalonia.Services;
using Xunit;

namespace IIoT.Edge.AvaloniaShell.Tests;

public sealed class IoViewWriteGateTests
{
    [Fact]
    public async Task Write_rejects_when_runtime_not_started_without_confirm_or_buffer_write()
    {
        var fixture = CreateFixture();

        var result = await fixture.Port.WriteAsync(fixture.Device, fixture.Row, 1, CancellationToken.None);

        Assert.Equal(IoViewWriteResultKind.RuntimeNotStarted, result.Kind);
        Assert.Equal(0, fixture.Dialog.ConfirmCalls);
        Assert.Empty(fixture.Buffer.SignalWrites);
        Assert.Empty(fixture.Buffer.IndexWrites);
    }

    [Fact]
    public async Task Write_rejects_without_permission_before_confirm()
    {
        var fixture = CreateFixture(runtimeStarted: true, canEditHardware: false);

        var result = await fixture.Port.WriteAsync(fixture.Device, fixture.Row, 1, CancellationToken.None);

        Assert.Equal(IoViewWriteResultKind.NoPermission, result.Kind);
        Assert.Equal(0, fixture.Dialog.ConfirmCalls);
        Assert.Empty(fixture.Buffer.SignalWrites);
    }

    [Fact]
    public async Task Write_rejects_when_user_cancels_confirm()
    {
        var fixture = CreateFixture(runtimeStarted: true, confirmResult: false);

        var result = await fixture.Port.WriteAsync(fixture.Device, fixture.Row, 1, CancellationToken.None);

        Assert.Equal(IoViewWriteResultKind.RejectedByUser, result.Kind);
        Assert.Equal(1, fixture.Dialog.ConfirmCalls);
        Assert.Empty(fixture.Buffer.SignalWrites);
    }

    [Fact]
    public async Task Write_accepts_to_runtime_buffer_without_direct_plc_write()
    {
        var fixture = CreateFixture(runtimeStarted: true);

        var result = await fixture.Port.WriteAsync(fixture.Device, fixture.Row, 12, CancellationToken.None);

        Assert.Equal(IoViewWriteResultKind.AcceptedToRuntimeBuffer, result.Kind);
        Assert.Equal(12, fixture.Buffer.SignalWrites["Start.Reply"]);
        Assert.Equal(12, fixture.Buffer.IndexWrites[5]);
        Assert.Equal(0, fixture.PlcConnectionManager.GetPlcCalls);
        Assert.Contains("已进入运行时缓冲，等待扫描任务按块写入", result.Message);
        Assert.Contains("已进入运行时缓冲，等待扫描任务按块写入", Assert.Single(fixture.AuditStore.GetRecent()).Message);
    }

    private static WriteGateFixture CreateFixture(
        bool runtimeStarted = false,
        bool canEditHardware = true,
        bool confirmResult = true)
    {
        var runtimeState = new AvaloniaRuntimeState();
        runtimeState.SetRuntimeStarted(runtimeStarted);
        var permission = new FakePermissionService { CanEditHardwareValue = canEditHardware };
        var plcConnectionManager = new FakePlcConnectionManager
        {
            Snapshot = new PlcConnectionRuntimeSnapshot
            {
                NetworkDeviceId = 1,
                DeviceName = "PLC-01",
                IsConnected = true
            }
        };
        var buffer = new FakePlcBuffer();
        var plcDataStore = new FakePlcDataStore(buffer);
        var dialog = new FakeAvaloniaDialogService { ConfirmResult = confirmResult };
        var language = new FakeAvaloniaLanguageService();
        var log = new FakeLogService();
        var audit = new IoViewWriteGateAuditStore();
        var port = new RuntimeBufferIoViewSafeInteractionPort(
            runtimeState,
            permission,
            plcConnectionManager,
            plcDataStore,
            dialog,
            language,
            log,
            audit);

        var row = new IoInteractionRowModel { BusinessGroup = "Start" };
        row.AddHostSignal(new IoSignalModel
        {
            SignalKey = "Start.Reply",
            SignalName = "启动应答",
            PlcAddress = "D101",
            StartIndex = 5,
            Direction = "Write"
        });

        return new WriteGateFixture(
            port,
            new IoNetworkDeviceModel { Id = 1, DeviceName = "PLC-01" },
            row,
            dialog,
            plcConnectionManager,
            buffer,
            audit);
    }

    private sealed record WriteGateFixture(
        RuntimeBufferIoViewSafeInteractionPort Port,
        IoNetworkDeviceModel Device,
        IoInteractionRowModel Row,
        FakeAvaloniaDialogService Dialog,
        FakePlcConnectionManager PlcConnectionManager,
        FakePlcBuffer Buffer,
        IoViewWriteGateAuditStore AuditStore);

    private sealed class FakePermissionService : IClientPermissionService
    {
        public bool CanEditParams => false;

        public bool CanEditHardwareValue { get; init; }

        public bool CanEditHardware => CanEditHardwareValue;

        public bool IsLocalAdmin => false;

        public event Action? PermissionStateChanged
        {
            add { }
            remove { }
        }

        public bool HasPermission(string permission) => CanEditHardwareValue;
    }

    private sealed class FakeAvaloniaDialogService : IAvaloniaDialogService
    {
        public event EventHandler<AvaloniaDialogRequest>? DialogRequested;

        public bool ConfirmResult { get; init; }

        public int ConfirmCalls { get; private set; }

        public Task ShowInfoAsync(string title, string message)
        {
            DialogRequested?.Invoke(this, AvaloniaDialogRequest.CreateInfo(title, message));
            return Task.CompletedTask;
        }

        public Task<bool> ConfirmAsync(string title, string message)
        {
            ConfirmCalls++;
            DialogRequested?.Invoke(this, AvaloniaDialogRequest.CreateConfirm(title, message));
            return Task.FromResult(ConfirmResult);
        }
    }

    private sealed class FakeAvaloniaLanguageService : IAvaloniaLanguageService
    {
        public string CultureName => "zh-CN";

        public string ToggleLabel => "English";

        public string GetText(string key) => key;

        public void Apply(string cultureName)
        {
        }

        public void Toggle()
        {
        }
    }

    private sealed class FakePlcConnectionManager : IPlcConnectionManager
    {
        public PlcConnectionRuntimeSnapshot? Snapshot { get; init; }

        public int GetPlcCalls { get; private set; }

        public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task StopAsync(CancellationToken ct = default) => Task.CompletedTask;

        public Task ReloadAsync(string deviceName, CancellationToken ct = default) => Task.CompletedTask;

        public Task StopDeviceAsync(int networkDeviceId, CancellationToken ct = default) => Task.CompletedTask;

        public void RegisterTasks(string deviceName, Func<IPlcBuffer, ProductionContext, List<IPlcTask>> factory)
        {
        }

        public IPlcService? GetPlc(int networkDeviceId)
        {
            GetPlcCalls++;
            return null;
        }

        public ProductionContext? GetContext(string deviceName) => null;

        public void MarkRuntimeFault(int networkDeviceId, string deviceName, string error)
        {
        }

        public PlcConnectionRuntimeSnapshot? GetRuntimeStatus(int networkDeviceId) => Snapshot;

        public IReadOnlyCollection<PlcConnectionRuntimeSnapshot> GetRuntimeStatuses()
            => Snapshot is null ? [] : [Snapshot];

        public void Dispose()
        {
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class FakePlcDataStore(FakePlcBuffer buffer) : IPlcDataStore
    {
        public void Register(int networkDeviceId, int readSize, int writeSize)
        {
        }

        public void Register(
            int networkDeviceId,
            int readSize,
            int writeSize,
            IReadOnlyCollection<PlcBufferSignalBinding> signalBindings)
        {
        }

        public IPlcBufferTransport? GetBuffer(int networkDeviceId) => buffer;

        public bool HasDevice(int networkDeviceId) => true;
    }

    private sealed class FakePlcBuffer : IPlcBufferTransport
    {
        public event EventHandler<PlcSignalBufferChangedEventArgs>? SignalValuesChanged;

        public Dictionary<string, ushort> SignalWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<int, ushort> IndexWrites { get; } = [];

        public ushort GetReadValue(int index) => 0;

        public bool TryGetReadWords(string signalKey, out ushort[] values)
        {
            values = [];
            return false;
        }

        public bool TryGetWriteWords(string signalKey, out ushort[] values)
        {
            values = SignalWrites.TryGetValue(signalKey, out var value) ? [value] : [];
            return values.Length > 0;
        }

        public void SetWriteValue(int index, ushort value)
        {
            IndexWrites[index] = value;
        }

        public void SetWriteValue(string signalKey, int offset, ushort value)
        {
            SignalWrites[signalKey] = value;
            SignalValuesChanged?.Invoke(this, new PlcSignalBufferChangedEventArgs(signalKey, "Write"));
        }

        public void UpdateReadBuffer(ushort[] data)
        {
        }

        public void UpdateReadSignal(string signalKey, IReadOnlyList<ushort> data)
        {
        }

        public ushort[] GetWriteBuffer() => [];

        public void SetSignalBindings(IReadOnlyCollection<PlcBufferSignalBinding> bindings)
        {
        }
    }

    private sealed class FakeLogService : ILogService
    {
        public event Action<LogEntry>? EntryAdded;

        public void Debug(string message) => Add("DEBUG", message);

        public void Info(string message) => Add("INFO", message);

        public void Warn(string message) => Add("WARN", message);

        public void Error(string message) => Add("ERROR", message);

        public void Fatal(string message) => Add("FATAL", message);

        private void Add(string level, string message)
            => EntryAdded?.Invoke(new LogEntry { Time = DateTime.Now, Level = level, Message = message });
    }
}
