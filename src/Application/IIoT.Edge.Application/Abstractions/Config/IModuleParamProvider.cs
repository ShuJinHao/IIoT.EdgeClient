namespace IIoT.Edge.Application.Abstractions.Config;

/// <summary>
/// 插件运行时读取自身三类参数的统一入口。
/// </summary>
public interface IModuleParamProvider<TMes, TCloud, TBusiness>
    where TMes : struct, Enum
    where TCloud : struct, Enum
    where TBusiness : struct, Enum
{
    Task<ModuleParamSnapshot<TMes, TCloud, TBusiness>> GetAsync(
        CancellationToken cancellationToken = default);
}
