using IIoT.Edge.Module.Contracts.Config;
using IIoT.Edge.Application.Features.Config.CloudApi;
using IIoT.Edge.SharedKernel.Messaging;
using IIoT.Edge.SharedKernel.Result;

namespace IIoT.Edge.Application.Features.Config.UseCases.SystemConfig.Commands;

public sealed record CloudApiConfigParamDto(
    string Key,
    string Value,
    string? Description = null);

public sealed record SaveCloudApiConfigParamsCommand(
    List<CloudApiConfigParamDto> Params) : ICommand<Result>;

public sealed class SaveCloudApiConfigParamsHandler(
    ILocalParameterConfigChangePublisher changePublisher,
    ILocalSystemRuntimeConfigService runtimeConfig,
    ICloudProfileSwitchProjectionWriter projectionWriter)
    : ICommandHandler<SaveCloudApiConfigParamsCommand, Result>
{
    public async Task<Result> Handle(
        SaveCloudApiConfigParamsCommand request,
        CancellationToken cancellationToken)
    {
        var parameters = request.Params ?? [];
        if (parameters.Count == 0)
        {
            return Result.Success();
        }

        if (parameters.Count != 1
            || !string.Equals(
                parameters[0].Key?.Trim(),
                CloudApiConfigParamSchema.Enabled,
                StringComparison.OrdinalIgnoreCase)
            || !bool.TryParse(parameters[0].Value?.Trim(), out var requestedCloudEnabled))
        {
            return Result.Failure("BINDING_CLOUD_CONFIGURATION_READ_ONLY");
        }

        if (!await TryWriteProjectionAsync(requestedCloudEnabled, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure("Cloud 系统开关投影写入失败，已保持原配置。");
        }

        try
        {
            await runtimeConfig.RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            if (requestedCloudEnabled)
            {
                _ = await TryWriteProjectionAsync(false, cancellationToken).ConfigureAwait(false);
                try
                {
                    await runtimeConfig.RefreshAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch
                {
                    // 投影已经按失败关闭写成 false；运行快照不会被提升为启用。
                }

                return Result.Failure("Cloud 系统开关刷新失败，已回滚为关闭。");
            }

            return Result.Failure("Cloud 系统开关已关闭，但运行快照刷新失败。");
        }

        changePublisher.NotifyModuleChanged();
        return Result.Success();
    }

    private async Task<bool> TryWriteProjectionAsync(
        bool enabled,
        CancellationToken cancellationToken)
    {
        try
        {
            await projectionWriter.WriteAsync(enabled, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

}
