namespace IIoT.Edge.Application.Features.Updates;

/// <summary>
/// EdgeClient 内部发布控制面契约。它不属于插件 SDK 公共接口。
/// </summary>
public interface IEdgeReleaseSourceValidator
{
    string? ValidateConfiguredSource();

    string? ValidateCatalogSource(string? catalogSource);
}
