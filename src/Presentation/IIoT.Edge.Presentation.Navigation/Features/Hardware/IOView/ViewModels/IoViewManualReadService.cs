using IIoT.Edge.Application.Abstractions.Plc;
using IIoT.Edge.Application.Abstractions.Plc.Store;
using IIoT.Edge.Application.Features.Hardware.IoMappings;

namespace IIoT.Edge.Presentation.Navigation.Features.Hardware.IOView;

public sealed record IoViewManualReadResult(bool ShouldRefreshValues, string? ErrorMessage = null);

public interface IIoViewManualReadService
{
    Task<IoViewManualReadResult> ReadAsync(
        int networkDeviceId,
        IEnumerable<IoDataSectionModel> dataSections,
        IEnumerable<IoContinuousReadMatrixSectionModel> arraySections);
}

public sealed class IoViewManualReadService(
    IPlcConnectionManager plcConnectionManager,
    IPlcDataStore dataStore) : IIoViewManualReadService
{
    public async Task<IoViewManualReadResult> ReadAsync(
        int networkDeviceId,
        IEnumerable<IoDataSectionModel> dataSections,
        IEnumerable<IoContinuousReadMatrixSectionModel> arraySections)
    {
        var plc = plcConnectionManager.GetPlc(networkDeviceId);
        var buffer = dataStore.GetBuffer(networkDeviceId);
        if (plc is null || buffer is null)
        {
            return new IoViewManualReadResult(ShouldRefreshValues: false);
        }

        try
        {
            foreach (var signal in dataSections.SelectMany(static section => section.Signals)
                         .Concat(arraySections.SelectMany(static section => section.Columns))
                         .Where(static signal => string.Equals(
                             signal.Direction,
                             IoMappingOptionCatalog.DirectionRead,
                             StringComparison.OrdinalIgnoreCase)))
            {
                var length = checked((ushort)Math.Max(1, signal.AddressCount));
                var words = await plc.ReadDataAsync<ushort>(signal.PlcAddress, length);
                buffer.UpdateReadSignal(signal.SignalKey, words);
            }

            return new IoViewManualReadResult(ShouldRefreshValues: true);
        }
        catch (Exception ex)
        {
            return new IoViewManualReadResult(
                ShouldRefreshValues: false,
                ErrorMessage: $"读取 IO 数据失败：{ex.Message}");
        }
    }
}
