using System.ComponentModel;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using IIoT.Edge.Application.Features.Updates;
using IIoT.Edge.Launcher.Services;
using IIoT.Edge.Launcher.ViewModels;
using IIoT.Edge.Module.Contracts.Updates;
using IIoT.Edge.SharedKernel.Configuration;
using IIoT.Edge.SharedKernel.Runtime;
using IIoT.Edge.UI.Shared.Avalonia.Controls;
using IIoT.Edge.UI.Shared.Localization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IIoT.Edge.Launcher.UiTests;

public sealed class LauncherLoopbackUpdateUiTests
{
    [AvaloniaFact]
    public async Task RealLauncher_WithIsolatedDataAndLoopbackCatalog_ShouldRenderSuccessAndFailureStates()
    {
        await using var server = new LoopbackReleaseServer();
        using var fixture = LauncherRuntimeFixture.Create(server);
        using var services = new ServiceCollection()
            .AddLauncherServices(fixture.LauncherDirectory)
            .BuildServiceProvider(
                new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
        services.GetRequiredService<IAppLanguageService>().Initialize();
        services.GetRequiredService<ILauncherAccountCatalogInitializer>().EnsureCatalogExists();
        services.GetRequiredService<IEdgeUpdateConfigInitializer>().EnsureConfigExists();
        Assert.True(App.TryCompleteUpdateStartup(services));

        var viewModel = services.GetRequiredService<LauncherMainViewModel>();
        var initialCheckCompleted = NewCompletion();
        void ObserveInitialCheck(object? _, PropertyChangedEventArgs args)
        {
            if (args.PropertyName == nameof(LauncherClientReleasePanelViewModel.IsBusy)
                && !viewModel.ClientReleasePanel.IsBusy
                && viewModel.ClientReleasePanel.Components.Count > 0)
            {
                initialCheckCompleted.TrySetResult();
            }
        }

        viewModel.ClientReleasePanel.PropertyChanged += ObserveInitialCheck;
        try
        {
            var initialized = await viewModel.InitializeLocalAccountAsync(
                "operator",
                "现场操作员",
                "Local-Test-Password-2026!",
                "Local-Test-Password-2026!");
            Assert.True(initialized, viewModel.ErrorMessage);
            if (!viewModel.ClientReleasePanel.IsBusy
                && viewModel.ClientReleasePanel.Components.Count > 0)
            {
                initialCheckCompleted.TrySetResult();
            }

            await initialCheckCompleted.Task.WaitAsync(TestContext.Current.CancellationToken);
        }
        finally
        {
            viewModel.ClientReleasePanel.PropertyChanged -= ObserveInitialCheck;
        }

        var activeProfiles = viewModel.Profiles
            .Select(static profile => profile.Profile)
            .ToArray();
        await viewModel.ClientReleasePanel.ReportProfilesSilentlyAsync(activeProfiles);

        var window = services.GetRequiredService<MainWindow>();
        try
        {
            window.Show();
            window.UpdateLayout();

            var grid = window.FindControl<EdgeDataGrid>("UpdateCenterRowsGrid");
            Assert.NotNull(grid);
            Assert.Same(viewModel.UpdateRows, grid.ItemsSource);
            Assert.True(window.FindControl<Control>("UpdateCenterPanelRoot")?.IsVisible);
            AssertSuccessRows(viewModel.UpdateRows, fixture.HostVersion);

            server.ReturnCatalogFailure = true;
            await viewModel.ClientReleasePanel.CheckAsync(activeProfiles);
            window.UpdateLayout();

            AssertUnavailableRows(viewModel.UpdateRows, "无法检查");
            services.GetRequiredService<IAppLanguageService>()
                .Change(CultureInfo.GetCultureInfo("en-US"));
            window.UpdateLayout();
            AssertUnavailableRows(viewModel.UpdateRows, "Unable to check");
        }
        finally
        {
            window.Close();
        }

        Assert.True(server.BootstrapRequestCount >= 4);
        Assert.True(server.CatalogRequestCount >= 4);
        Assert.True(server.VersionReportRequestCount >= 2);
        Assert.All(
            server.ObservedRemoteAddresses,
            static address => Assert.True(IPAddress.IsLoopback(address)));
    }

    private static TaskCompletionSource NewCompletion()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void AssertSuccessRows(
        IEnumerable<LauncherClientPluginItem> rows,
        string hostVersion)
    {
        var snapshot = rows.ToArray();
        var host = Assert.Single(snapshot, static row =>
            row.VersionComponent?.ComponentKind == EdgeComponentKind.Host);
        Assert.Equal(hostVersion, host.CurrentVersion);
        Assert.Equal(hostVersion, host.TargetVersion);
        Assert.Equal("已最新", host.StatusText);
        Assert.False(host.CanInstallOrUpdate);

        foreach (var moduleId in new[] { "AP", "CP" })
        {
            var plugin = Assert.Single(snapshot, row => row.ModuleId == moduleId);
            Assert.Equal("2.0.10", plugin.CurrentVersion);
            Assert.Equal("2.0.11", plugin.TargetVersion);
            Assert.Equal("可更新", plugin.StatusText);
            Assert.Equal("更新", plugin.ActionText);
            Assert.True(plugin.CanInstallOrUpdate);
            Assert.NotEqual("-", plugin.PackageSizeText);
            Assert.Contains(
                $"{moduleId} 2.0.11",
                plugin.ReleaseNotesText,
                StringComparison.Ordinal);
        }
    }

    private static void AssertUnavailableRows(
        IEnumerable<LauncherClientPluginItem> rows,
        string unavailableText)
    {
        var snapshot = rows.ToArray();
        Assert.Equal(3, snapshot.Length);
        foreach (var row in snapshot)
        {
            Assert.Equal(unavailableText, row.TargetVersion);
            Assert.Equal(unavailableText, row.StatusText);
            Assert.Equal(unavailableText, row.ActionText);
            Assert.False(row.CanInstallOrUpdate);
        }

        Assert.Equal(
            "2.0.10",
            Assert.Single(snapshot, static row => row.ModuleId == "AP").CurrentVersion);
        Assert.Equal(
            "2.0.10",
            Assert.Single(snapshot, static row => row.ModuleId == "CP").CurrentVersion);
    }

    private sealed class LauncherRuntimeFixture : IDisposable
    {
        private LauncherRuntimeFixture(
            string root,
            string launcherDirectory,
            string hostVersion)
        {
            Root = root;
            LauncherDirectory = launcherDirectory;
            HostVersion = hostVersion;
        }

        public string Root { get; }

        public string LauncherDirectory { get; }

        public string HostVersion { get; }

        public static LauncherRuntimeFixture Create(LoopbackReleaseServer server)
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                "iiot-launcher-loopback-ui-tests",
                Guid.NewGuid().ToString("N"));
            var layoutRoot = Path.Combine(root, "layout");
            var launcherDirectory = Path.Combine(layoutRoot, "launcher");
            var hostDirectory = Path.Combine(layoutRoot, "host");
            var pluginsRoot = Path.Combine(layoutRoot, "plugins");
            Directory.CreateDirectory(launcherDirectory);
            Directory.CreateDirectory(hostDirectory);
            Directory.CreateDirectory(pluginsRoot);

            var shellAssemblyPath = Path.Combine(hostDirectory, "IIoT.Edge.Shell.dll");
            File.Copy(typeof(App).Assembly.Location, shellAssemblyPath);
            var hostVersion = EdgeClientHostRuntime.FormatHostVersion(
                typeof(App).Assembly.GetName().Version);
            server.HostVersion = hostVersion;

            WriteText(
                Path.Combine(launcherDirectory, "launcher.profiles.json"),
                """
                [
                  {
                    "profileId": "AP",
                    "displayName": "AP 工序",
                    "description": "AP",
                    "machineProfile": "AP",
                    "executablePath": "../host/IIoT.Edge.Shell",
                    "iconKind": "Cog",
                    "accentColor": "#0F766E"
                  },
                  {
                    "profileId": "CP",
                    "displayName": "CP 工序",
                    "description": "CP",
                    "machineProfile": "CP",
                    "executablePath": "../host/IIoT.Edge.Shell",
                    "iconKind": "Cog",
                    "accentColor": "#0F766E"
                  }
                ]
                """);

            foreach (var moduleId in new[] { "AP", "CP" })
            {
                WriteText(
                    Path.Combine(pluginsRoot, moduleId, "plugin.json"),
                    $$"""
                    {
                      "moduleId": "{{moduleId}}",
                      "supportedProcessType": "{{moduleId}}",
                      "displayName": "{{moduleId}}",
                      "version": "2.0.10",
                      "hostApiVersion": "{{EdgeClientHostRuntime.HostApiVersion}}",
                      "minHostVersion": "1.0.0",
                      "maxHostVersion": "99.0.0",
                      "entryAssembly": "IIoT.Edge.Module.{{moduleId}}.dll",
                      "entryType": "IIoT.Edge.Module.{{moduleId}}.DependencyInjection",
                      "dependencies": []
                    }
                    """);
                WriteText(
                    EdgeClientProgramDataPaths.ResolveMachineProfileConfigPath(
                        moduleId,
                        hostDirectory),
                    CreateMachineProfileJson(server.BaseUrl, moduleId));
                WriteText(
                    EdgeClientProgramDataPaths.ResolveProfileCloudSwitchProjectionPath(
                        moduleId,
                        hostDirectory),
                    """{"version":1,"enabled":false}""");
            }

            WriteText(
                EdgeClientProgramDataPaths.ResolveLauncherUpdateConfigPath(
                    launcherDirectory),
                $$"""
                {
                  "source": "{{server.UpdateSource}}",
                  "channel": "stable",
                  "targetRuntime": "win-x64"
                }
                """);
            WriteText(
                Path.Combine(
                    EdgeClientProgramDataPaths.ResolveLauncherDirectory(
                        launcherDirectory),
                    "iiot-enabled-plugins.json"),
                """
                {
                  "plugins": [
                    { "moduleId": "AP" },
                    { "moduleId": "CP" }
                  ]
                }
                """);

            return new LauncherRuntimeFixture(root, launcherDirectory, hostVersion);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }

        private static string CreateMachineProfileJson(
            string baseUrl,
            string moduleId)
            => $$"""
               {
                 "InstanceId": "{{moduleId}}",
                 "Shell": {
                   "MachineProfile": "{{moduleId}}"
                 },
                 "CloudApi": {
                   "BaseUrl": "{{baseUrl}}",
                   "TimeoutSecs": 5,
                   "ClientCode": "EDGE-LOOPBACK",
                   "BootstrapSecret": "loopback-secret",
                   "Paths": {
                     "DeviceInstance": "/api/v1/bootstrap/device-instance",
                     "ClientReleaseCatalogTemplate": "/api/v1/edge/client-releases/device/{deviceId}/catalog",
                     "ClientVersionReport": "/api/v1/edge/client-releases/version-reports",
                     "RuntimeHeartbeat": "/api/v1/edge/runtime-heartbeats"
                   }
                 },
                 "Modules": {
                   "Enabled": [ "{{moduleId}}" ]
                 }
               }
               """;

        private static void WriteText(string path, string content)
        {
            Directory.CreateDirectory(
                Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("测试文件路径缺少目录。"));
            File.WriteAllText(path, content);
        }
    }

    private sealed class LoopbackReleaseServer : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _shutdown = new();
        private readonly Task _acceptLoop;
        private readonly List<IPAddress> _observedRemoteAddresses = [];
        private readonly object _syncRoot = new();
        private int _returnCatalogFailure;
        private int _bootstrapRequestCount;
        private int _catalogRequestCount;
        private int _versionReportRequestCount;

        public LoopbackReleaseServer()
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            var endpoint = (IPEndPoint)_listener.LocalEndpoint;
            BaseUrl = $"http://127.0.0.1:{endpoint.Port}";
            UpdateSource = $"{BaseUrl}/velopack/stable/";
            _acceptLoop = AcceptLoopAsync();
        }

        public string BaseUrl { get; }

        public string UpdateSource { get; }

        public string HostVersion { get; set; } = "2.0.0";

        public bool ReturnCatalogFailure
        {
            get => Volatile.Read(ref _returnCatalogFailure) == 1;
            set => Volatile.Write(ref _returnCatalogFailure, value ? 1 : 0);
        }

        public int BootstrapRequestCount => Volatile.Read(ref _bootstrapRequestCount);

        public int CatalogRequestCount => Volatile.Read(ref _catalogRequestCount);

        public int VersionReportRequestCount => Volatile.Read(ref _versionReportRequestCount);

        public IReadOnlyList<IPAddress> ObservedRemoteAddresses
        {
            get
            {
                lock (_syncRoot)
                {
                    return _observedRemoteAddresses.ToArray();
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _shutdown.Cancel();
            _listener.Stop();
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                _shutdown.Dispose();
            }
        }

        private async Task AcceptLoopAsync()
        {
            while (!_shutdown.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener
                        .AcceptTcpClientAsync(_shutdown.Token)
                        .ConfigureAwait(false);
                }
                catch (SocketException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }
                catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
                {
                    return;
                }

                using (client)
                {
                    if (client.Client.RemoteEndPoint is IPEndPoint remote)
                    {
                        lock (_syncRoot)
                        {
                            _observedRemoteAddresses.Add(remote.Address);
                        }
                    }

                    await HandleRequestAsync(
                            client.GetStream(),
                            _shutdown.Token)
                        .ConfigureAwait(false);
                }
            }
        }

        private async Task HandleRequestAsync(
            NetworkStream stream,
            CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(
                stream,
                Encoding.ASCII,
                detectEncodingFromByteOrderMarks: false,
                bufferSize: 4096,
                leaveOpen: true);
            var requestLine = await reader
                .ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
            {
                return;
            }

            var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            var method = parts.ElementAtOrDefault(0) ?? string.Empty;
            var path = parts.ElementAtOrDefault(1) ?? "/";
            var contentLength = 0;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } header
                   && header.Length > 0)
            {
                if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                {
                    _ = int.TryParse(
                        header["Content-Length:".Length..].Trim(),
                        out contentLength);
                }
            }

            if (contentLength > 0)
            {
                var remaining = contentLength;
                var buffer = new char[Math.Min(contentLength, 4096)];
                while (remaining > 0)
                {
                    var read = await reader
                        .ReadAsync(
                            buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                            cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    remaining -= read;
                }
            }

            if (method == "GET"
                && path.StartsWith(
                    "/api/v1/bootstrap/device-instance",
                    StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _bootstrapRequestCount);
                await WriteJsonAsync(
                    stream,
                    200,
                    new
                    {
                        id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        deviceName = "Loopback Device",
                        clientCode = "EDGE-LOOPBACK",
                        processId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        uploadAccessToken = "loopback-token"
                    },
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (method == "GET"
                && path.Contains(
                    "/api/v1/edge/client-releases/device/",
                    StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _catalogRequestCount);
                if (ReturnCatalogFailure)
                {
                    await WriteJsonAsync(
                        stream,
                        500,
                        new { error = "catalog_unavailable" },
                        cancellationToken).ConfigureAwait(false);
                    return;
                }

                await WriteJsonAsync(
                    stream,
                    200,
                    CreateCatalog(),
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            if (method == "POST"
                && path.StartsWith(
                    "/api/v1/edge/client-releases/version-reports",
                    StringComparison.Ordinal))
            {
                Interlocked.Increment(ref _versionReportRequestCount);
                await WriteJsonAsync(
                    stream,
                    200,
                    new { accepted = true },
                    cancellationToken).ConfigureAwait(false);
                return;
            }

            await WriteJsonAsync(
                stream,
                404,
                new { error = "not_found" },
                cancellationToken).ConfigureAwait(false);
        }

        private object CreateCatalog()
            => new
            {
                catalogSchemaVersion = 2,
                channel = "stable",
                targetRuntime = "win-x64",
                host = new
                {
                    componentKind = "Host",
                    displayName = "Edge Host",
                    versions = new[]
                    {
                        new
                        {
                            id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                            channel = "stable",
                            version = HostVersion,
                            hostApiVersion = EdgeClientHostRuntime.HostApiVersion,
                            targetRuntime = "win-x64",
                            targetFramework = "net10.0",
                            downloadUrl = $"{BaseUrl}/packages/host.nupkg",
                            sha256 = new string('A', 64),
                            packageSize = 2048,
                            status = "Published",
                            releaseNotes = $"Host {HostVersion}",
                            publisher = "IIoT",
                            createdAtUtc = DateTimeOffset.Parse("2026-07-28T00:00:00Z"),
                            publishedAtUtc = DateTimeOffset.Parse("2026-07-28T00:00:00Z")
                        }
                    }
                },
                plugins = new[] { CreatePlugin("AP"), CreatePlugin("CP") },
                generatedAtUtc = DateTimeOffset.Parse("2026-07-28T00:00:00Z"),
                hostUpdateSource = UpdateSource
            };

        private object CreatePlugin(string moduleId)
            => new
            {
                componentKind = "Plugin",
                moduleId,
                displayName = moduleId,
                description = $"{moduleId} process",
                iconKind = "Cog",
                accentColor = "#0F766E",
                versions = new[]
                {
                    new
                    {
                        id = moduleId == "AP"
                            ? Guid.Parse("44444444-4444-4444-4444-444444444444")
                            : Guid.Parse("55555555-5555-5555-5555-555555555555"),
                        channel = "stable",
                        version = "2.0.11",
                        hostApiVersion = EdgeClientHostRuntime.HostApiVersion,
                        minHostVersion = "1.0.0",
                        maxHostVersion = "99.0.0",
                        targetRuntime = "win-x64",
                        targetFramework = "net10.0",
                        downloadUrl = $"{BaseUrl}/packages/{moduleId}.zip",
                        sha256 = new string(moduleId == "AP" ? 'B' : 'C', 64),
                        packageSize = moduleId == "AP" ? 4096 : 8192,
                        dependencies = Array.Empty<string>(),
                        status = "Published",
                        releaseNotes = $"{moduleId} 2.0.11 loopback release",
                        publisher = "IIoT",
                        createdAtUtc = DateTimeOffset.Parse("2026-07-28T00:00:00Z"),
                        publishedAtUtc = DateTimeOffset.Parse("2026-07-28T00:00:00Z")
                    }
                }
            };

        private static async Task WriteJsonAsync(
            NetworkStream stream,
            int statusCode,
            object payload,
            CancellationToken cancellationToken)
        {
            var body = JsonSerializer.SerializeToUtf8Bytes(payload);
            var reason = statusCode == 200 ? "OK" : statusCode == 500 ? "Internal Server Error" : "Not Found";
            var header = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {statusCode} {reason}\r\n" +
                "Content-Type: application/json\r\n" +
                $"Content-Length: {body.Length}\r\n" +
                "Connection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(body, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
