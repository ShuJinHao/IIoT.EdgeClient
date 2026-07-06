using IIoT.Edge.SharedKernel.DataPipeline.CellData;

namespace IIoT.Edge.Application.Common.DataPipeline;

public static class DataPipelineUploadTargetPolicy
{
    public static DataPipelineUploadTargets Resolve(bool mesEnabled, bool cloudEnabled)
    {
        var targets = DataPipelineUploadTargets.None;
        if (mesEnabled)
        {
            targets |= DataPipelineUploadTargets.Mes;
        }

        if (cloudEnabled)
        {
            targets |= DataPipelineUploadTargets.Cloud;
        }

        return targets;
    }

    public static string Format(DataPipelineUploadTargets uploadTargets)
        => uploadTargets switch
        {
            DataPipelineUploadTargets.Mes => "MES",
            DataPipelineUploadTargets.Cloud => "Cloud",
            DataPipelineUploadTargets.All => "MES/Cloud",
            _ => "未配置目标"
        };
}
