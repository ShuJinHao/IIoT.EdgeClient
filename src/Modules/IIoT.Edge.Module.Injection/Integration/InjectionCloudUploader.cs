using AutoMapper;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Infrastructure.Integration.Config;
using IIoT.Edge.Infrastructure.Integration.Http;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Injection.Integration;

public sealed class InjectionCloudUploader : IProcessCloudUploader
{
    private const string UploadPath = "/api/v1/edge/pass-stations/injection/batch";

    private readonly ICloudHttpClient _cloudHttp;
    private readonly IMapper _mapper;
    private readonly ILogService _logger;

    public InjectionCloudUploader(
        ICloudHttpClient cloudHttp,
        IMapper mapper,
        ILogService logger)
    {
        _cloudHttp = cloudHttp;
        _mapper = mapper;
        _logger = logger;
    }

    public string ProcessType => InjectionModule.ModuleKey;

    public ProcessUploadMode UploadMode => ProcessUploadMode.Batch;

    public async Task<CloudCallResult> UploadAsync(
        ProcessCloudUploadContext context,
        IReadOnlyList<CellCompletedRecord> records,
        CancellationToken cancellationToken = default)
    {
        if (records.Count == 0)
        {
            return CloudCallResult.Success();
        }

        var items = new List<InjectionCloudDto>(records.Count);
        foreach (var record in records)
        {
            if (record.CellData is not InjectionCellData injection)
            {
                _logger.Error(
                    $"[Cloud] Injection uploader received unexpected process type '{record.CellData.ProcessType}'.");
                return CloudCallResult.Failure(CloudCallOutcome.Exception, "unexpected_process_type");
            }

            items.Add(_mapper.Map<InjectionCloudDto>(injection));
        }

        var payload = new
        {
            deviceId = context.Device.DeviceId,
            items
        };

        var result = await _cloudHttp.PostAsync(
            UploadPath,
            payload,
            new CloudRequestOptions
            {
                IdempotencyKey = CloudIdempotencyKeyBuilder.ForBatch(
                    ProcessType,
                    nameof(InjectionCloudUploader),
                    records)
            }).ConfigureAwait(false);

        if (!result.IsSuccess)
        {
            _logger.Error(
                $"[Cloud] Injection batch upload failed. Count:{records.Count}, Outcome:{result.Outcome}, Reason:{result.ReasonCode}");
        }

        return result;
    }
}
