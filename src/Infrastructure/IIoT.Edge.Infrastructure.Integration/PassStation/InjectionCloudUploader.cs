using AutoMapper;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Mappings.Cloud.Injection;
using IIoT.Edge.SharedKernel.DataPipeline;
using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Infrastructure.Integration.PassStation;

public sealed class InjectionCloudUploader : IProcessCloudUploader
{
    private readonly ICloudHttpClient _cloudHttp;
    private readonly ICloudApiEndpointProvider _endpointProvider;
    private readonly IMapper _mapper;
    private readonly ILogService _logger;

    public InjectionCloudUploader(
        ICloudHttpClient cloudHttp,
        ICloudApiEndpointProvider endpointProvider,
        IMapper mapper,
        ILogService logger)
    {
        _cloudHttp = cloudHttp;
        _endpointProvider = endpointProvider;
        _mapper = mapper;
        _logger = logger;
    }

    public string ProcessType => "Injection";

    public ProcessUploadMode UploadMode => ProcessUploadMode.Batch;

    public async Task<bool> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return true;
        }

        var items = new List<InjectionCloudDto>(records.Count);
        foreach (var record in records)
        {
            if (record.CellData is not InjectionCellData injection)
            {
                _logger.Error(
                    $"[Cloud] Injection uploader received unexpected process type '{record.CellData.ProcessType}'.");
                return false;
            }

            items.Add(_mapper.Map<InjectionCloudDto>(injection));
        }

        var payload = new
        {
            deviceId = context.Device.DeviceId,
            items
        };

        var success = await _cloudHttp.PostAsync(
            _endpointProvider.GetPassStationInjectionBatchPath(),
            payload).ConfigureAwait(false);

        if (!success)
        {
            _logger.Error($"[Cloud] Injection batch upload failed. Count:{records.Count}");
        }

        return success;
    }
}
