using IIoT.Edge.Application.Features.Updates;

namespace IIoT.Edge.Infrastructure.Update.Configuration;

public sealed class FileEdgeReleaseSourceValidator(
    EdgeUpdateConfigPaths paths) : IEdgeReleaseSourceValidator
{
    public string? ValidateConfiguredSource()
        => TryReadConfiguredSource(out _, out var error)
            ? null
            : error;

    public string? ValidateCatalogSource(string? catalogSource)
    {
        if (!TryReadConfiguredSource(out var configuredSource, out var configurationError))
        {
            return configurationError ?? "Launcher 更新配置不可用。";
        }

        if (string.IsNullOrWhiteSpace(catalogSource))
        {
            return "Cloud catalog 未声明 Host 更新源。";
        }

        return SourcesEqual(configuredSource!, catalogSource)
            ? null
            : "Cloud catalog 的 Host 更新源与本地正式配置不一致。";
    }

    internal static bool SourcesEqual(string configuredSource, string catalogSource)
    {
        var configured = NormalizeSource(configuredSource);
        var catalog = NormalizeSource(catalogSource);
        return configured is not null
               && catalog is not null
               && string.Equals(
                   configured,
                   catalog,
                   StringComparison.Ordinal);
    }

    private bool TryReadConfiguredSource(
        out string? configuredSource,
        out string? error)
    {
        configuredSource = null;
        if (!LauncherUpdateConfigurationFile.TryReadCurrent(
                paths.ConfigPath,
                out var configuration,
                out error)
            || configuration is null)
        {
            return false;
        }

        if (NormalizeSource(configuration.Source) is null)
        {
            error = "Launcher 更新源无效。";
            return false;
        }

        configuredSource = configuration.Source;
        return true;
    }

    private static string? NormalizeSource(string source)
    {
        var trimmed = source.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp
                && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        return uri.AbsoluteUri.TrimEnd('/');
    }
}
