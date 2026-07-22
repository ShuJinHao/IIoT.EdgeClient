using System.Text.Json;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;
using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace IIoT.Edge.Infrastructure.Update.Host;

public sealed class VelopackHostUpdateService : IEdgeHostUpdateService
{
    public const string UpdateSourceEnvironmentVariable = "IIOT_EDGE_UPDATE_URL";

    private readonly Func<string?> _sourceProvider;
    private readonly Func<string, UpdateManager> _managerFactory;
    private UpdateInfo? _lastUpdate;
    private string? _lastSource;

    public VelopackHostUpdateService(string baseDirectory)
        : this(() => ResolveUpdateSource(baseDirectory), CreateUpdateManager)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
    }

    internal VelopackHostUpdateService(
        Func<string?> sourceProvider,
        Func<string, UpdateManager> managerFactory)
    {
        _sourceProvider = sourceProvider ?? throw new ArgumentNullException(nameof(sourceProvider));
        _managerFactory = managerFactory ?? throw new ArgumentNullException(nameof(managerFactory));
    }

    public async Task<EdgeHostUpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        var source = _sourceProvider()?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            ClearCachedUpdate();
            return new EdgeHostUpdateCheckResult(EdgeHostUpdateCheckState.NotConfigured);
        }

        try
        {
            var manager = _managerFactory(source);
            if (!manager.IsInstalled)
            {
                ClearCachedUpdate();
                return new EdgeHostUpdateCheckResult(
                    EdgeHostUpdateCheckState.NotInstalled,
                    CurrentVersion: manager.CurrentVersion?.ToString());
            }

            var pending = manager.UpdatePendingRestart;
            if (pending is not null)
            {
                return CreateCheckResult(
                    EdgeHostUpdateCheckState.PendingRestart,
                    manager.CurrentVersion?.ToString(),
                    pending);
            }

            var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                ClearCachedUpdate();
                return new EdgeHostUpdateCheckResult(
                    EdgeHostUpdateCheckState.NoUpdate,
                    CurrentVersion: manager.CurrentVersion?.ToString());
            }

            _lastUpdate = update;
            _lastSource = source;
            return CreateCheckResult(
                EdgeHostUpdateCheckState.UpdateAvailable,
                manager.CurrentVersion?.ToString(),
                update.TargetFullRelease);
        }
        catch (NotInstalledException)
        {
            ClearCachedUpdate();
            return new EdgeHostUpdateCheckResult(EdgeHostUpdateCheckState.NotInstalled);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ClearCachedUpdate();
            return new EdgeHostUpdateCheckResult(
                EdgeHostUpdateCheckState.Failed,
                ErrorMessage: ex.Message);
        }
    }

    public async Task<EdgeHostUpdateApplyResult> DownloadAndApplyUpdateAsync(
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var source = _sourceProvider()?.Trim();
        if (string.IsNullOrWhiteSpace(source))
        {
            return new EdgeHostUpdateApplyResult(false, "Update source is not configured.");
        }

        try
        {
            var manager = _managerFactory(source);
            if (!manager.IsInstalled)
            {
                return new EdgeHostUpdateApplyResult(false, "Application is not installed by Velopack.");
            }

            var pending = manager.UpdatePendingRestart;
            if (pending is not null)
            {
                manager.ApplyUpdatesAndRestart(pending, []);
                return new EdgeHostUpdateApplyResult(true);
            }

            var update = string.Equals(_lastSource, source, StringComparison.OrdinalIgnoreCase)
                ? _lastUpdate
                : null;
            update ??= await manager.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                ClearCachedUpdate();
                return new EdgeHostUpdateApplyResult(false, "No update is available.");
            }

            await manager
                .DownloadUpdatesAsync(update, value => progress?.Report(value), cancellationToken)
                .ConfigureAwait(false);
            manager.ApplyUpdatesAndRestart(update.TargetFullRelease, []);
            return new EdgeHostUpdateApplyResult(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new EdgeHostUpdateApplyResult(false, ex.Message);
        }
    }

    public async Task<EdgeHostUpdateApplyResult> ApplyVersionAsync(
        EdgeHostVersionRelease release,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        var check = await CheckForUpdatesAsync(cancellationToken).ConfigureAwait(false);
        if (check.State is EdgeHostUpdateCheckState.Failed
            or EdgeHostUpdateCheckState.NotConfigured
            or EdgeHostUpdateCheckState.NotInstalled)
        {
            return new EdgeHostUpdateApplyResult(false, check.ErrorMessage ?? "宿主更新源不可用。");
        }

        if (check.State == EdgeHostUpdateCheckState.NoUpdate
            || !string.Equals(check.TargetVersion, release.Version.Version, StringComparison.OrdinalIgnoreCase))
        {
            return new EdgeHostUpdateApplyResult(
                false,
                $"当前宿主更新源没有提供指定版本 {release.Version.Version}。");
        }

        return await DownloadAndApplyUpdateAsync(progress, cancellationToken).ConfigureAwait(false);
    }

    internal static UpdateManager CreateUpdateManager(string source)
    {
        var options = new UpdateOptions
        {
            AllowVersionDowngrade = true
        };

        var localDirectory = TryResolveLocalDirectory(source);
        return localDirectory is null
            ? new UpdateManager(source, options)
            : new UpdateManager(new SimpleFileSource(localDirectory), options);
    }

    public static DirectoryInfo? TryResolveLocalDirectory(string source)
    {
        var trimmedSource = source.Trim();
        if (string.IsNullOrWhiteSpace(trimmedSource))
        {
            return null;
        }

        if (Uri.TryCreate(trimmedSource, UriKind.Absolute, out var uri) && uri.IsFile)
        {
            var fileDirectory = new DirectoryInfo(uri.LocalPath);
            return fileDirectory.Exists ? fileDirectory : null;
        }

        var directory = new DirectoryInfo(trimmedSource);
        return directory.Exists ? directory : null;
    }

    private static EdgeHostUpdateCheckResult CreateCheckResult(
        EdgeHostUpdateCheckState state,
        string? currentVersion,
        VelopackAsset asset)
        => new(
            state,
            CurrentVersion: currentVersion,
            TargetVersion: asset.Version?.ToString(),
            ReleaseNotes: asset.NotesMarkdown);

    private static string? ResolveUpdateSource(string baseDirectory)
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(UpdateSourceEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
        {
            return fromEnvironment.Trim();
        }

        var configPath = EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(baseDirectory);
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(configPath));
            var root = document.RootElement;
            return ReadString(root, "Source")
                ?? ReadString(root, "source")
                ?? ReadString(root, "UpdateSource")
                ?? ReadString(root, "Url");
        }
        catch (JsonException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim()
            : null;

    private void ClearCachedUpdate()
    {
        _lastUpdate = null;
        _lastSource = null;
    }
}
