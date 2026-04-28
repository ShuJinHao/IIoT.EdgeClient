using Microsoft.Extensions.DependencyInjection;

namespace IIoT.Edge.UI.Shared;

/// <summary>
/// UI.Shared 依赖注入扩展。
/// 共享 UI 库只提供控件、样式和转换器，不注册宿主或插件业务服务。
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddUiShared(this IServiceCollection services)
        => services;
}
