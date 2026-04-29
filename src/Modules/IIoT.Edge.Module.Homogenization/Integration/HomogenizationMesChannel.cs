using IIoT.Edge.Application.Abstractions.Config;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Application.Modules.Mes;
using IIoT.Edge.Module.Homogenization.Abstractions;
using IIoT.Edge.Module.Homogenization.Config;
using IIoT.Edge.Module.Homogenization.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;
using Microsoft.Extensions.Options;

namespace IIoT.Edge.Module.Homogenization.Integration;

public sealed class HomogenizationMesChannel
    : MesUploadChannelBase<HomogenizationCellData>, IHomogenizationMesChannel
{
    private readonly HomogenizationMesOptions _mesOptions;
    private readonly HomogenizationMesCodeOptions _mesCodes;

    public HomogenizationMesChannel(
        MesRequestExecutor requestExecutor,
        ILocalParameterConfigService parameterConfigService,
        ILogService logger,
        IOptions<HomogenizationMesOptions> mesOptions,
        IOptions<HomogenizationCodeOptions> codeOptions)
        : base(DependencyInjection.ModuleKey, logger, requestExecutor, parameterConfigService)
    {
        _mesOptions = mesOptions.Value;
        _mesCodes = codeOptions.Value.Mes;
    }

    protected override string SignToken => _mesOptions.SignToken;

    public Task<MesCallResult> UploadInboundAsync(
        DeviceSession? device,
        string trayCode,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(trayCode))
        {
            return Task.FromResult(MesCallResult.InvalidContext("托盘码不能为空。"));
        }

        return ExecuteMesAsync(
            device,
            _mesOptions.Paths.Inbound,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                data = new
                {
                    stackTrayNo = trayCode,
                    weldTrayNo = trayCode,
                    productNo = trayCode,
                    devices = (object?)null,
                    boms = (object?)null
                }
            },
            cancellationToken);
    }

    public Task<MesCallResult> UploadOutboundAsync(
        DeviceSession? device,
        HomogenizationCellData cellData,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(cellData);

        if (string.IsNullOrWhiteSpace(cellData.TrayCode))
        {
            return Task.FromResult(MesCallResult.InvalidContext("出料托盘码不能为空。"));
        }

        return ExecuteMesAsync(
            device,
            _mesOptions.Paths.Outbound,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                outboundTime = FormatTimestamp(cellData.CompletedTime ?? DateTime.UtcNow),
                serialNumber = cellData.TrayCode,
                data = new
                {
                    boundNo = cellData.TrayCode,
                    lastBoundNo = cellData.TrayCode,
                    produce = BuildOutboundProduce(cellData)
                }
            },
            cancellationToken);
    }

    public Task<MesCallResult> UploadRealtimeAsync(
        DeviceSession? device,
        HomogenizationRealtimeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return ExecuteMesAsync(
            device,
            _mesOptions.Paths.Realtime,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                data = new
                {
                    devices = new[]
                    {
                        new
                        {
                            stationNo = envelope.StationNo,
                            collectTime = FormatTimestamp(snapshot.CapturedAt),
                            data = BuildRealtimeItems(snapshot)
                        }
                    }
                }
            },
            cancellationToken);
    }

    public Task<MesCallResult> UploadRecipeAsync(
        DeviceSession? device,
        HomogenizationRecipeSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return ExecuteMesAsync(
            device,
            _mesOptions.Paths.Recipe,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                stationNo = envelope.StationNo,
                data = new
                {
                    devices = BuildRecipeItems(snapshot)
                }
            },
            cancellationToken);
    }

    public Task<MesCallResult> UploadEquipmentStatusAsync(
        DeviceSession? device,
        HomogenizationEquipmentStatusSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return ExecuteMesAsync(
            device,
            _mesOptions.Paths.EquipmentStatus,
            envelope => new
            {
                upperComputerNo = envelope.UpperComputerNo,
                timestamp = envelope.Timestamp,
                sign = envelope.Sign,
                data = new
                {
                    devices = new[]
                    {
                        new
                        {
                            stationNo = envelope.StationNo,
                            status = snapshot.StatusCode,
                            msg = snapshot.Messages
                        }
                    }
                }
            },
            cancellationToken);
    }

    protected override Task<MesCallResult> UploadOutboundRecordAsync(
        DeviceSession? device,
        HomogenizationCellData cellData,
        CellCompletedRecord record,
        CancellationToken cancellationToken)
        => UploadOutboundAsync(device, cellData, cancellationToken);

    private IReadOnlyList<object> BuildRealtimeItems(HomogenizationRealtimeSnapshot snapshot)
        =>
        [
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.StirringSpeed)), snapshot.StirringSpeed),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.StirringCurrent)), snapshot.StirringCurrent),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.DispersionSpeed)), snapshot.DispersionSpeed),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.DispersionCurrent)), snapshot.DispersionCurrent),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.Temperature)), snapshot.Temperature),
            CreateItem(_mesCodes.GetRealtimeItem(nameof(HomogenizationRealtimeSnapshot.Vacuum)), snapshot.Vacuum)
        ];

    private IReadOnlyList<object> BuildRecipeItems(HomogenizationRecipeSnapshot snapshot)
    {
        var items = new List<object>();

        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.StirringSpeed)), snapshot.StirringSpeed);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.DispersionSpeed)), snapshot.DispersionSpeed);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Ncm)), snapshot.Ncm);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Sp1)), snapshot.Sp1);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Nmp)), snapshot.Nmp);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.GlueSolution)), snapshot.GlueSolution);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Cnt)), snapshot.Cnt);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Vacuum)), snapshot.Vacuum.Select(static value => value ? 1 : 0).ToArray());
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Time)), snapshot.Time);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.Temperature)), snapshot.Temperature);
        AddIndexedRecipeItems(items, _mesCodes.GetRecipeItem(nameof(HomogenizationRecipeSnapshot.StopStep)), snapshot.StopStep.Select(static value => value ? 1 : 0).ToArray());

        return items;
    }

    private IReadOnlyList<object> BuildOutboundProduce(HomogenizationCellData cellData)
    {
        var produce = new List<object>();

        AddProduceItem(produce, _mesCodes.GetOutboundItem("DeviceCode"), cellData.DeviceCode);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("DeviceName"), cellData.DeviceName);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("StartTime"), cellData.InboundTime);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CompleteTime"), cellData.CompletedTime);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("StirringSpeed"), cellData.RealtimeSnapshot?.StirringSpeed);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("Temperature"), cellData.RealtimeSnapshot?.Temperature);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("Vacuum"), cellData.RealtimeSnapshot?.Vacuum);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CntActual"), cellData.CntActualKg);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CntTarget"), cellData.CntTargetKg);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CntTankAWeight"), cellData.CntTankAWeightKg);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("CntTankBWeight"), cellData.CntTankBWeightKg);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("NmpActual"), cellData.NmpActualKg);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("NmpTarget"), cellData.NmpTargetKg);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("GlueActual"), cellData.GlueActualKg);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("SetStirringTime"), cellData.SetStirringTimeMinutes);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("RemainingStirringTime"), cellData.RemainingStirringTimeMinutes);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("SetDispersionTime"), cellData.SetDispersionTimeMinutes);
        AddProduceItem(produce, _mesCodes.GetOutboundItem("RemainingDispersionTime"), cellData.RemainingDispersionTimeMinutes);

        return produce;
    }

    private static object CreateItem(HomogenizationMesItemCodeOptions item, object? value)
        => new
        {
            code = item.Code,
            name = item.Name,
            type = item.Type,
            unit = item.Unit,
            val = value?.ToString() ?? string.Empty
        };

    private static void AddIndexedRecipeItems<T>(
        ICollection<object> items,
        HomogenizationMesItemCodeOptions item,
        IReadOnlyList<T> values)
    {
        for (var index = 0; index < values.Count; index++)
        {
            items.Add(new
            {
                code = $"{item.Code}_{index + 1:D2}",
                name = $"{item.Name}_{index + 1:D2}",
                type = item.Type,
                unit = item.Unit,
                val = values[index]?.ToString() ?? string.Empty
            });
        }
    }

    private static void AddProduceItem(ICollection<object> produce, HomogenizationMesItemCodeOptions item, object? value)
    {
        if (value is null)
        {
            return;
        }

        var text = value switch
        {
            DateTime time => FormatTimestamp(time),
            DateTimeOffset timeOffset => FormatTimestamp(timeOffset.DateTime),
            _ => value.ToString()
        };

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        produce.Add(new
        {
            code = item.Code,
            name = item.Name,
            val = text
        });
    }
}
