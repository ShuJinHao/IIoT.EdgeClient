using AutoMapper;
using IIoT.Edge.Application.Abstractions.Device;
using IIoT.Edge.Application.Abstractions.Logging;
using IIoT.Edge.Application.Abstractions.Modules;
using IIoT.Edge.Application.Modules;
using IIoT.Edge.Module.Injection.Payload;
using IIoT.Edge.SharedKernel.DataPipeline;

namespace IIoT.Edge.Module.Injection.Integration;

public sealed class InjectionCloudUploader : ProcessCloudUploaderBase<InjectionCellData, object>
{
    private const string UploadPathValue = "/api/v1/edge/pass-stations/injection/batch";

    private readonly IMapper _mapper;

    public InjectionCloudUploader(
        ICloudHttpClient cloudHttp,
        IMapper mapper,
        ILogService logger)
        : base(cloudHttp, logger)
    {
        _mapper = mapper;
    }

    public override string ProcessType => InjectionModule.ModuleKey;

    public override ProcessUploadMode UploadMode => ProcessUploadMode.Batch;

    protected override string UploadPath => UploadPathValue;

    protected override object BuildPayload(
        ProcessCloudUploadContext context,
        IReadOnlyList<InjectionCellData> cellData,
        IReadOnlyList<CellCompletedRecord> records)
        => new
        {
            deviceId = context.Device.DeviceId,
            items = cellData.Select(_mapper.Map<InjectionCloudDto>).ToArray()
        };
}
