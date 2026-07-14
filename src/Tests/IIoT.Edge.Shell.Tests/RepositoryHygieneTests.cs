using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace IIoT.Edge.Shell.Tests;

public sealed class RepositoryHygieneTests
{
    private static readonly string[] UpperLayerProjectFragments =
    [
        "Application",
        "Runtime",
        "Host",
        "Infrastructure",
        "Modules",
        "Presentation"
    ];

    private static readonly string[] ForbiddenContractNames =
    [
        "IIoT.Edge.Module." + "Abstractions",
        "IIoT.Edge.Module." + "Contracts",
        "IIoT.Edge.Integration." + "Contracts",
        "IIoT.Edge.Plugin." + "Shared"
    ];

    private static readonly string[] DeletedSdkArtifactNames =
    [
        "Module" + "Samples",
        "Dry" + "Run",
        "Scan" + "Capture" + "Starter",
        "Package" + "Validation" + "Client",
        "Loading" + "Scan" + "Task",
        "IIoT.Edge.Runtime." + "Scan",
        "Pack" + "Edge" + "Packages",
        "Run" + "Single" + "Repo" + "Release" + "Rehearsal",
        "New" + "Edge" + "Module",
        "New-" + "Edge" + "Module"
    ];

    private static readonly string[] DeletedOverWrappedApiNames =
    [
        "Plugin" + "Cloud" + "Upload" + "Mode",
        "Plugin" + "Mes" + "Upload" + "Mode",
        "Plugin" + "Upload" + "Modes",
        "I" + "Edge" + "Module",
        "I" + "Module" + "Loader"
    ];

    private static readonly string[] MojibakeMarkers =
    [
        "\uFFFD",
        "\u6D93\u5D85",
        "\u6D60\u64B3",
        "\u93CD\u572D",
        "\u6D30\u8930",
        "\u6DC7\u6FE7",
        "\u93C8\uE061",
        "\u9359\u6A58",
        "\u9356\uE15C",
        "\u8FBE\u64B3",
        "\u93C3\u72B3",
        "\u7039\u6C56",
        "\uE15C",
        "\u20AC?"
    ];

    private static readonly Regex LongTaskDelayPattern = new(
        @"Task\.Delay\(\s*(?:1\d{2,}|\d{4,}|TimeSpan\.FromMilliseconds\(\s*(?:1\d{2,}|\d{4,}))",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex DirectVisibleValidationIssuePattern = new(
        @"new\s+ValidationIssue\s*\(\s*""[^""]*[\u4e00-\u9fff]",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ResourceLookupPattern = new(
        @"(?:GetText|FormatText)\(\s*""([^""]+)""",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex ApplicationUiFacingTypePattern = new(
        @"\b(?:class|record(?:\s+class|\s+struct)?|struct|interface)\s+([A-Za-z_][A-Za-z0-9_]*(?:Vm|ViewModel))\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PrivateNetworkAddressPattern = new(
        @"\b(?:10\.\d{1,3}\.\d{1,3}\.\d{1,3}|172\.(?:1[6-9]|2\d|3[01])\.\d{1,3}\.\d{1,3}|192\.168\.\d{1,3}\.\d{1,3})\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex BusinessXamlNativeVisibleControlPattern = new(
        @"<\s*(?:Button|DataGrid|ScrollViewer|ListBox|TextBox|ComboBox|CalendarDatePicker|DatePicker|CheckBox|TabControl)\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex PageLevelVisualPropertyPattern = new(
        @"\b(?:FontSize|FontWeight|Foreground|Background|BorderBrush|BorderThickness|CornerRadius|BoxShadow)\s*=",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex RemovedMapperUsagePattern = new(
        @"\b(?:Add" + "Auto" + "Mapper|I" + "Mapper|Create" + "Map" + @")\b|:\s*Profile\b",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    [Fact]
    public void SharedProjects_ShouldNotReferenceUpperLayers()
    {
        var root = FindRepositoryRoot();
        var uiSharedProject = Path.Combine(root, "src", "Shared", "IIoT.Edge.UI.Shared", "IIoT.Edge.UI.Shared.csproj");
        var sharedKernelProject = Path.Combine(root, "src", "Shared", "IIoT.Edge.SharedKernel", "IIoT.Edge.SharedKernel.csproj");

        var uiReferences = GetProjectReferences(uiSharedProject);
        Assert.All(uiReferences, reference => Assert.Contains("IIoT.Edge.SharedKernel", reference, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(uiReferences, reference =>
            UpperLayerProjectFragments.Any(fragment => reference.Contains(fragment, StringComparison.OrdinalIgnoreCase)));

        Assert.Empty(GetProjectReferences(sharedKernelProject));
    }

    [Fact]
    public void CoreLayerProjectReferences_ShouldPreserveDependencyDirection()
    {
        var root = FindRepositoryRoot();
        var projectReferences = new Dictionary<string, string[]>
        {
            ["src/Core/IIoT.Edge.Domain/IIoT.Edge.Domain.csproj"] =
            [
                "src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj"
            ],
            ["src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj"] =
            [
                "src/Core/IIoT.Edge.Domain/IIoT.Edge.Domain.csproj",
                "src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj"
            ],
            ["src/Modules/IIoT.Edge.Module.Sdk/IIoT.Edge.Module.Sdk.csproj"] =
            [
                "src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj",
                "src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj"
            ],
            ["src/Edge/IIoT.Edge.Host.DataPipeline/IIoT.Edge.Host.DataPipeline.csproj"] =
            [
                "src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj",
                "src/Modules/IIoT.Edge.Module.Sdk/IIoT.Edge.Module.Sdk.csproj",
                "src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj"
            ],
            ["src/Infrastructure/IIoT.Edge.Infrastructure.DeviceComm/IIoT.Edge.Infrastructure.DeviceComm.csproj"] =
            [
                "src/Application/IIoT.Edge.Application/IIoT.Edge.Application.csproj"
            ],
            ["src/Shared/IIoT.Edge.SharedKernel/IIoT.Edge.SharedKernel.csproj"] = []
        };

        foreach (var (projectPath, expectedReferences) in projectReferences)
        {
            var actualReferences = GetProjectReferenceRepositoryPaths(root, ToFullPath(root, projectPath))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var expected = expectedReferences
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            Assert.Equal(expected, actualReferences);
        }
    }

    [Fact]
    public void CoreLayerSource_ShouldNotReferenceForbiddenUpperLayerNamespaces()
    {
        var root = FindRepositoryRoot();
        var forbiddenByDirectory = new Dictionary<string, string[]>
        {
            ["src/Core/IIoT.Edge.Domain"] =
            [
                "IIoT.Edge.Application",
                "IIoT.Edge.Host.DataPipeline",
                "IIoT.Edge.Infrastructure",
                "IIoT.Edge.Module.Sdk",
                "IIoT.Edge.Runtime",
                "IIoT.Edge.Presentation",
                "IIoT.Edge.UI.Shared"
            ],
            ["src/Application/IIoT.Edge.Application"] =
            [
                "IIoT.Edge.Host.DataPipeline",
                "IIoT.Edge.Infrastructure",
                "IIoT.Edge.Module.Sdk",
                "IIoT.Edge.Runtime",
                "IIoT.Edge.Presentation",
                "IIoT.Edge.UI.Shared"
            ],
            ["src/Infrastructure/IIoT.Edge.Infrastructure.DeviceComm"] =
            [
                "IIoT.Edge.Host.DataPipeline",
                "IIoT.Edge.Runtime"
            ],
            ["src/Shared/IIoT.Edge.SharedKernel"] =
            [
                "IIoT.Edge.Application",
                "IIoT.Edge.Host.DataPipeline",
                "IIoT.Edge.Infrastructure",
                "IIoT.Edge.Module.Sdk",
                "IIoT.Edge.Runtime",
                "IIoT.Edge.Presentation",
                "IIoT.Edge.UI.Shared"
            ]
        };

        var matches = forbiddenByDirectory
            .SelectMany(check => EnumerateFiles(ToFullPath(root, check.Key), "*.*")
                .Where(IsTextCandidate)
                .SelectMany(path => FindForbiddenMatches(root, path, check.Value)))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void MainSolution_ShouldContainOnlyApprovedRuntimeToolAndNoSdkSamples()
    {
        var root = FindRepositoryRoot();
        var solution = File.ReadAllText(Path.Combine(root, "IIoT.EdgeClient.slnx"));

        var toolProjects = Regex
            .Matches(solution, @"src/Tools/[^""]+\.csproj", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(
            ["src/Tools/IIoT.Edge.RuntimeLayoutSync/IIoT.Edge.RuntimeLayoutSync.csproj"],
            toolProjects);
        foreach (var deletedName in DeletedSdkArtifactNames)
        {
            Assert.DoesNotContain(deletedName, solution, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void HostProductionDataFallback_ShouldNotExistInRuntimeSource()
    {
        var root = FindRepositoryRoot();
        var facadePath = ToFullPath(
            root,
            "src/Application/IIoT.Edge.Application/Features/Production/DataView/ProductionDataQueryFacade.cs");
        var pagePath = ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Production/DataView/Views/DataViewPage.axaml");
        var registrationPath = ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/PluginSystem/StandardModuleNavigationRegistration.cs");
        var registrationSource = File.ReadAllText(registrationPath);

        Assert.False(File.Exists(facadePath));
        Assert.False(File.Exists(pagePath));
        Assert.DoesNotContain("RegisterStandardDataView", registrationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("typeof(DataViewPage)", registrationSource, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardPreviewPlcStatusTable_ShouldKeepLongErrorsOutOfMainColumns()
    {
        var root = FindRepositoryRoot();
        var path = ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/Views/DashboardPreviewView.axaml");
        var xaml = File.ReadAllText(path);

        Assert.DoesNotContain("Navigation_DashboardPreview_PlcLastError", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"{Binding LastError}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding=\"{Binding LastConnectedText}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Binding=\"{Binding LastFailureText}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeTablePanel", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"fill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"{DynamicResource Navigation_DashboardPreview_PlcStatusTableTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"DashboardPreviewPlcStatusGrid\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewportMaxHeight=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsControl ItemsSource=\"{Binding PlcStatusTableItems}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnDefinitions=\"82,98,70,82,82,56\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeActionColumn", xaml, StringComparison.Ordinal);
        Assert.Contains("Command=\"{Binding DataContext.ShowPlcStatusDetailCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"PlcDetailSummaryCard\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Navigation_DashboardPreview_PlcCommunicationSection", xaml, StringComparison.Ordinal);
        Assert.Contains("Navigation_DashboardPreview_PlcActivitySection", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeStatusChip", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinitions=\"Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding SelectedPlcStatusDetail.LastErrorDetail}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceTree_ShouldNotContainGeneratedOrDuplicateArtifacts()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");

        Assert.False(Directory.Exists(Path.Combine(root, ".codex-temp")), ".codex-temp 不应留在仓库根目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "%EDGE_SHARED_NUGET_FEED%")), "不应保留未展开环境变量形成的本地 NuGet 源目录。");
        Assert.False(File.Exists(Path.Combine(root, "IIoT.EdgeClient.DevTools.slnx")), "仓库根目录只保留主方案。");
        Assert.False(File.Exists(Path.Combine(root, "PACKAGE-README.md")), "不再保留 SDK 或包化 README。");
        Assert.False(File.Exists(Path.Combine(root, "scripts", "Pack" + "Edge" + "Packages.ps1")), "不再保留 NuGet 包化脚本。");
        Assert.False(File.Exists(Path.Combine(root, "scripts", "Run" + "Single" + "Repo" + "Release" + "Rehearsal.ps1")), "不再保留包化发布演练脚本。");
        Assert.False(Directory.Exists(Path.Combine(root, "tools")), "根目录不再保留 tools 目录，正式脚本统一放入 scripts。");
        var approvedToolProjects = Directory.Exists(Path.Combine(root, "src", "Tools"))
            ? EnumerateFiles(Path.Combine(root, "src", "Tools"), "*.csproj")
                .Select(path => ToRepositoryPath(root, path))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
        Assert.Equal(
            ["src/Tools/IIoT.Edge.RuntimeLayoutSync/IIoT.Edge.RuntimeLayoutSync.csproj"],
            approvedToolProjects);
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime.DataPipeline")), "不再保留旧 DataPipeline 独立项目目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime." + "Scan")), "不再保留旧 Scan 独立项目目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Runtime", "IIoT.Edge.Runtime", "Stations")), "Runtime 不再保留旧站点示例目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Excel")), "不再保留未接入主方案的 Excel 空壳目录。");
        Assert.False(Directory.Exists(Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.DataMapping")), "不再保留未接入主方案的 DataMapping 空壳目录。");
        Assert.False(File.Exists(Path.Combine(root, "src", "Core", "domain_restore.txt")), "不应保留 dotnet restore 输出日志。");
        Assert.False(File.Exists(Path.Combine(root, "src", "Infrastructure", "full_restore_output_en.txt")), "不应保留 dotnet restore 输出日志。");

        var nugetConfig = File.ReadAllText(Path.Combine(root, "NuGet.Config"));
        Assert.DoesNotContain("%EDGE_SHARED_NUGET_FEED%", nugetConfig, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(".artifacts", nugetConfig, StringComparison.OrdinalIgnoreCase);

        var wpftmpProjects = EnumerateFiles(sourceRoot, "*_wpftmp.csproj")
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();
        Assert.Empty(wpftmpProjects);

        var fontFiles = EnumerateFiles(sourceRoot, "*.*")
            .Where(IsFontFile)
            .Select(path => ToRepositoryPath(root, path))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(
            ["src/Shared/IIoT.Edge.UI.Shared/Assets/fonts/iconfont.ttf"],
            fontFiles);

        var duplicateFontDirectories = Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => !ShouldSkip(path))
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return string.Equals(name, "Noto", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(name, "Roboto", StringComparison.OrdinalIgnoreCase);
            })
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();
        Assert.Empty(duplicateFontDirectories);
    }

    [Fact]
    public void RoundedWindowRegion_ShouldLiveInSharedUiAndBeReusedByShellLauncherAndPanels()
    {
        var root = FindRepositoryRoot();
        var allowedRegionOwner = "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Windowing/EdgeRoundedWindowRegion.cs";
        var sharedRegion = File.ReadAllText(ToFullPath(root, allowedRegionOwner));
        var regionUsers = new[]
        {
            "src/Edge/IIoT.Edge.Shell/MainWindow.axaml.cs",
            "src/Edge/IIoT.Edge.Launcher/MainWindow.axaml.cs",
            "src/Edge/IIoT.Edge.Launcher/ChangePasswordWindow.axaml.cs",
            "src/Presentation/IIoT.Edge.Presentation.Panels/Features/Equipment/Views/ProductionPlanSelectionWindow.axaml.cs"
        };
        var forbiddenRegionMarkers = new[]
        {
            "CreateRoundRectRgn",
            "SetWindowRgn",
            "DeleteObject",
            "DllImport(\"gdi32.dll\")",
            "DllImport(\"user32.dll\")"
        };

        Assert.Contains("CreateRoundRectRgn", sharedRegion, StringComparison.Ordinal);
        Assert.Contains("SetWindowRgn", sharedRegion, StringComparison.Ordinal);
        Assert.DoesNotContain("ApplyStartupLeftBias", sharedRegion, StringComparison.Ordinal);

        foreach (var regionUser in regionUsers)
        {
            var source = File.ReadAllText(ToFullPath(root, regionUser));
            Assert.Contains("EdgeRoundedWindowRegion.Attach(this, WindowCornerRadius)", source, StringComparison.Ordinal);
        }

        var duplicatedRegionMatches = new[]
            {
                Path.Combine(root, "src", "Edge"),
                Path.Combine(root, "src", "Presentation"),
                Path.Combine(root, "src", "Shared")
            }
            .SelectMany(path => EnumerateFiles(path, "*.cs"))
            .Where(path => !string.Equals(ToRepositoryPath(root, path), allowedRegionOwner, StringComparison.OrdinalIgnoreCase))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return forbiddenRegionMarkers
                    .Where(marker => source.Contains(marker, StringComparison.Ordinal))
                    .Select(marker => $"{ToRepositoryPath(root, path)} contains duplicate rounded-window region marker {marker}");
            })
            .ToArray();

        Assert.Empty(duplicatedRegionMatches);
    }

    [Fact]
    public void ShellAppSettings_ShouldNotContainCommittedLicenseOrJwtSecrets()
    {
        var root = FindRepositoryRoot();
        var appsettingsPath = Path.Combine(root, "src", "Edge", "IIoT.Edge.Shell", "appsettings.json");
        var appsettings = File.ReadAllText(appsettingsPath);

        Assert.DoesNotContain("\"LicenseKey\"", appsettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MediatR", appsettings, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(new Regex(@"eyJ[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+\.[A-Za-z0-9_-]+", RegexOptions.CultureInvariant), appsettings);
    }

    [Fact]
    public void LocalAccounts_ShouldNotCommitDefaultPasswordsOrSha256LoginCompatibility()
    {
        var root = FindRepositoryRoot();
        var shellRoot = Path.Combine(root, "src", "Edge", "IIoT.Edge.Shell");
        var launcherRoot = Path.Combine(root, "src", "Edge", "IIoT.Edge.Launcher");
        var configFiles = Directory.GetFiles(shellRoot, "appsettings*.json", SearchOption.TopDirectoryOnly)
            .Append(Path.Combine(launcherRoot, "launcher.accounts.sample.json"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var defaultHashPattern = new Regex(
            @"""PasswordHash""\s*:\s*""[0-9A-Fa-f]{64}""",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        var defaultPasswordPattern = new Regex(
            @"""Password""\s*:\s*""123456""",
            RegexOptions.CultureInvariant | RegexOptions.Compiled);
        var defaultCredentialMatches = configFiles
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                return defaultHashPattern
                    .Matches(text)
                    .Select(match => $"{ToRepositoryPath(root, path)} contains committed SHA256 password hash at offset {match.Index}")
                    .Concat(defaultPasswordPattern
                        .Matches(text)
                        .Select(match => $"{ToRepositoryPath(root, path)} contains committed default password at offset {match.Index}"));
            })
            .ToArray();

        var authSourceFiles = new[]
            {
                Path.Combine(launcherRoot, "Services"),
                Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Integration", "Auth")
            }
            .SelectMany(path => EnumerateFiles(path, "*.cs"))
            .ToArray();
        var legacyLoginMatches = authSourceFiles
            .SelectMany(path => FindForbiddenMatches(root, path, ["ComputeSha256"]))
            .ToArray();
        var passwordHashScriptMatches = EnumerateFiles(Path.Combine(root, "scripts"), "*.ps1")
            .Where(path => !string.Equals(Path.GetFileName(path), "TestEdgeDeploymentPreflight.ps1", StringComparison.Ordinal))
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                var hasPasswordContext = text.Contains("Password", StringComparison.OrdinalIgnoreCase)
                    || text.Contains("PasswordHash", StringComparison.OrdinalIgnoreCase);
                var hasSha256Hashing = text.Contains("SHA256Managed", StringComparison.Ordinal)
                    || text.Contains(".ComputeHash(", StringComparison.Ordinal);

                return hasPasswordContext && hasSha256Hashing
                    ? [$"{ToRepositoryPath(root, path)} contains SHA256 password hash generation script content"]
                    : Array.Empty<string>();
            })
            .ToArray();
        var initializer = File.ReadAllText(Path.Combine(
            launcherRoot,
            "Services",
            "LauncherAccountCatalogInitializer.cs"));
        var authService = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Integration",
            "Auth",
            "AuthService.cs"));
        var integrationRegistration = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Integration",
            "DependencyInjection.cs"));

        Assert.Empty(defaultCredentialMatches);
        Assert.Empty(legacyLoginMatches);
        Assert.Empty(passwordHashScriptMatches);
        Assert.DoesNotContain("File.Copy", initializer, StringComparison.Ordinal);
        Assert.Contains("不能静默复制 sample 账号", initializer, StringComparison.Ordinal);
        Assert.Contains("InitializeLocalAdminAsync", authService, StringComparison.Ordinal);
        Assert.Contains("ResetLocalAdminPasswordAsync", authService, StringComparison.Ordinal);
        Assert.Contains("local-admin.json", integrationRegistration, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ClientRules_ShouldDocumentLocalPasswordResetContract()
    {
        var root = FindRepositoryRoot();
        var ruleDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端规则.md"));
        var requiredText = new[]
        {
            "不得提交默认密码",
            "不得由 Launcher 自动复制样本账号文件",
            "Shell 本地紧急管理员是现场兜底红线",
            "登录弹窗必须提供首次初始化入口",
            "本地密码 hash 必须使用带版本标识的 PBKDF2 格式",
            "历史 64 位十六进制 SHA256 只允许作为旧部署识别和强制重置依据"
        };

        Assert.All(requiredText, text => Assert.Contains(text, ruleDoc, StringComparison.Ordinal));
    }

    [Fact]
    public void ShellLoginDialog_ShouldExposeLocalEmergencyInitializeAndResetFlows()
    {
        var root = FindRepositoryRoot();
        var dialogXaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Shell",
            "Views",
            "ShellLoginDialog.axaml"));
        var dialogCode = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Shell",
            "Views",
            "ShellLoginDialog.axaml.cs"));
        var zhResources = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Shell",
            "Resources",
            "Languages",
            "zh-CN.axaml"));
        var enResources = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Shell",
            "Resources",
            "Languages",
            "en-US.axaml"));

        Assert.Contains("x:Name=\"LocalSetupPanel\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"LocalResetPanel\"", dialogXaml, StringComparison.Ordinal);
        Assert.Contains("LocalAdminCredentialStatus.NotConfigured", dialogCode, StringComparison.Ordinal);
        Assert.Contains("LocalAdminCredentialStatus.RequiresPasswordReset", dialogCode, StringComparison.Ordinal);
        Assert.Contains("InitializeLocalEmergencyAdminAsync", dialogCode, StringComparison.Ordinal);
        Assert.Contains("ResetLocalEmergencyPasswordAsync", dialogCode, StringComparison.Ordinal);
        Assert.Contains("Shell_Login_LocalInitializeDescription", zhResources, StringComparison.Ordinal);
        Assert.Contains("Shell_Login_LocalInitializeDescription", enResources, StringComparison.Ordinal);
        Assert.Contains("Shell_Login_ResetSubmit", zhResources, StringComparison.Ordinal);
        Assert.Contains("Shell_Login_ResetSubmit", enResources, StringComparison.Ordinal);
    }

    [Fact]
    public void CloudJwtAuthorization_ShouldValidateTokensBeforeReadingClaims()
    {
        var root = FindRepositoryRoot();
        var authRoot = Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Integration", "Auth");
        var authFiles = EnumerateFiles(authRoot, "*.cs").ToArray();
        var readJwtTokenMatches = authFiles
            .SelectMany(path => FindForbiddenMatches(root, path, ["ReadJwtToken"]))
            .ToArray();
        var authService = File.ReadAllText(Path.Combine(authRoot, "AuthService.cs"));
        var ruleDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端规则.md"));

        Assert.Empty(readJwtTokenMatches);
        Assert.Contains("ValidateToken", authService, StringComparison.Ordinal);
        Assert.Contains("CloudJwtValidationConfig", authService, StringComparison.Ordinal);
        Assert.Contains("ValidateLifetime = true", authService, StringComparison.Ordinal);
        Assert.Contains("ValidateIssuer = true", authService, StringComparison.Ordinal);
        Assert.Contains("ValidateAudience = true", authService, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateLifetime = false", authService, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateIssuer = false", authService, StringComparison.Ordinal);
        Assert.DoesNotContain("ValidateAudience = false", authService, StringComparison.Ordinal);
        Assert.Contains("JWT 用于建立客户端授权会话前必须先验签", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("不得降级成未验签登录", ruleDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void MesSigning_ShouldUseConfiguredHmacWithoutFixedToken()
    {
        var root = FindRepositoryRoot();
        var files = EnumerateFiles(Path.Combine(root, "src", "Application", "IIoT.Edge.Application", "Modules", "Mes"), "*.cs")
            .Concat(EnumerateFiles(Path.Combine(root, "src", "Modules"), "*.cs"))
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var matches = files
            .SelectMany(path => FindForbiddenMatches(root, path,
            [
                "hdc2023",
                "DefaultMesSignToken",
                "MD5.HashData"
            ]))
            .ToArray();
        var mesBase = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Application",
            "IIoT.Edge.Application",
            "Modules",
            "Mes",
            "MesScenarioChannelBase.cs"));
        var ruleDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端规则.md"));

        Assert.Empty(matches);
        Assert.Contains("HMACSHA256.HashData", mesBase, StringComparison.Ordinal);
        Assert.Contains("未配置 MES 签名密钥", mesBase, StringComparison.Ordinal);
        Assert.Contains("MES 请求签名必须使用受控配置注入的密钥执行 HMAC-SHA256", ruleDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductionSource_ShouldNotUseDebugWriteLine()
    {
        var root = FindRepositoryRoot();
        var sourceFiles = EnumerateFiles(Path.Combine(root, "src"), "*.cs")
            .Where(path => !ToRepositoryPath(root, path).StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var matches = sourceFiles
            .SelectMany(path => FindForbiddenMatches(root, path, ["Debug.WriteLine", "System.Diagnostics.Debug.WriteLine"]))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void CapacitySyncTask_ShouldKeepBoundedConcurrency()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Integration",
            "Capacity",
            "CapacitySyncTask.cs"));
        var ruleDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端规则.md"));

        Assert.Contains("SemaphoreSlim _syncGate = new(1, 1)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.WhenAll", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Parallel.ForEach", source, StringComparison.Ordinal);
        Assert.Contains("必须有显式并发边界", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("禁止对 PLC、时间片或补传批次无界 `Task.WhenAll`", ruleDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientArchitecture_ShouldUseSharedUploadAndRetryHelpers()
    {
        var root = FindRepositoryRoot();
        var cloudConsumer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Integration",
            "PassStation",
            "CloudConsumer.cs"));
        var mesConsumer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Integration",
            "Mes",
            "MesConsumer.cs"));
        var processQueueTask = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Edge",
            "IIoT.Edge.Host.DataPipeline",
            "Tasks",
            "ProcessQueueTask.cs"));
        var productionTaskFiles = EnumerateFiles(Path.Combine(root, "src", "Modules"), "*.cs")
            .Where(path => ToRepositoryPath(root, path).Contains("/Production/Tasks/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var helperFiles = new[]
        {
            Path.Combine(root, "src", "Application", "IIoT.Edge.Application", "Common", "DataPipeline", "DataPipelineUploadTargetPolicy.cs"),
            Path.Combine(root, "src", "Application", "IIoT.Edge.Application", "Common", "DataPipeline", "DataPipelineUploadScenarioResolver.cs"),
            Path.Combine(root, "src", "Infrastructure", "IIoT.Edge.Infrastructure.Integration", "UploadDiagnosticsContextFactory.cs"),
            Path.Combine(root, "src", "Edge", "IIoT.Edge.Host.DataPipeline", "Services", "DataPipelineRetryChannelMetadata.cs")
        };

        Assert.All(helperFiles, path => Assert.True(File.Exists(path), $"缺少客户端架构 helper：{ToRepositoryPath(root, path)}"));
        Assert.Contains("UploadDiagnosticsContextFactory.CreateCloudContext", cloudConsumer, StringComparison.Ordinal);
        Assert.Contains("UploadDiagnosticsContextFactory.CreateMesContext", mesConsumer, StringComparison.Ordinal);
        Assert.Contains("DataPipelineRetryChannelMetadata.ShouldProcess", processQueueTask, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetRecordKind", cloudConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("TryGetRecordKind", mesConsumer, StringComparison.Ordinal);
        Assert.DoesNotContain("\"failed_cloud_records\"", processQueueTask, StringComparison.Ordinal);
        Assert.DoesNotContain("\"failed_mes_records\"", processQueueTask, StringComparison.Ordinal);
        Assert.Empty(productionTaskFiles.SelectMany(path =>
            FindForbiddenMatches(root, path, ["var targets = DataPipelineUploadTargets.None", "DataPipelineUploadTargets.Mes => \"MES\""])));
    }

    [Fact]
    public void OversizedViewModelsAndServices_ShouldStayOnExplicitGovernanceList()
    {
        var root = FindRepositoryRoot();
        var allowedOversizedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src/Application/IIoT.Edge.Application/Features/Updates/EdgeReleaseService.cs",
            "src/Edge/IIoT.Edge.Launcher/ViewModels/LauncherClientReleasePanelViewModel.cs",
            "src/Edge/IIoT.Edge.Launcher/ViewModels/LauncherMainViewModel.cs",
            "src/Edge/IIoT.Edge.Launcher/ViewModels/LauncherProfileCardViewModel.cs",
            "src/Infrastructure/IIoT.Edge.Infrastructure.DeviceComm/Plc/Services/Modbus/ModbusPlcService.cs",
            "src/Infrastructure/IIoT.Edge.Infrastructure.Integration/Device/DeviceService.cs",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/ViewModels/HardwareConfigViewModel.cs",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/IOView/ViewModels/IoViewViewModel.cs",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Production/Monitor/ViewModels/MonitorViewModel.cs",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/ViewModels/DashboardPreviewPageViewModels.cs",
            "src/Presentation/IIoT.Edge.Presentation.Panels/Features/Equipment/ViewModels/EquipmentViewModel.cs"
        };

        var oversizedFiles = EnumerateFiles(Path.Combine(root, "src"), "*.cs")
            .Where(path => !ToRepositoryPath(root, path).StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
            .Where(path => File.ReadAllText(path).Contains("class ", StringComparison.Ordinal))
            .Where(path =>
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                return fileName.EndsWith("ViewModel", StringComparison.Ordinal)
                       || fileName.EndsWith("Service", StringComparison.Ordinal);
            })
            .Where(path => File.ReadLines(path).Count() > 500)
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();

        Assert.Empty(oversizedFiles.Except(allowedOversizedFiles, StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void DeploymentScriptsAndDocs_ShouldNotHardcodeProductionIpOrBypassCertificates()
    {
        var root = FindRepositoryRoot();
        var productionSourceFiles = EnumerateFiles(Path.Combine(root, "src"), "*.cs")
            .Where(path => !ToRepositoryPath(root, path).StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var deploymentEntryFiles = EnumerateFiles(Path.Combine(root, "scripts"), "*.ps1")
            .Concat(
            [
                Path.Combine(root, "docs", "客户端部署.md"),
                Path.Combine(root, "docs", "Edge安装更新验收.md"),
                Path.Combine(root, "docs", "Edge客户端宿主插件分发契约.md"),
                Path.Combine(root, "docs", "客户端规则.md"),
                Path.Combine(root, "docs", "客户端架构治理清单.md")
            ])
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var certificateImplementationFiles = EnumerateFiles(Path.Combine(root, "src"), "*.cs")
            .Where(path => !ToRepositoryPath(root, path).StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
            .Concat(EnumerateFiles(Path.Combine(root, "scripts"), "*.ps1"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var productionIpMatches = productionSourceFiles
            .Concat(deploymentEntryFiles)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .SelectMany(path => FindForbiddenRegexMatches(root, path, PrivateNetworkAddressPattern, "private network address"))
            .ToArray();
        var certificateBypassMatches = certificateImplementationFiles
            .SelectMany(path => FindForbiddenMatches(root, path,
            [
                "DangerousAcceptAnyServerCertificateValidator",
                "ServerCertificateCustomValidationCallback",
                "TrustAllCertificates",
                "SkipCertificateValidation"
            ]))
            .ToArray();
        var explicitTrustAllMatches = certificateImplementationFiles
            .SelectMany(path => FindForbiddenMatches(root, path,
            [
                "忽略证书",
                "跳过 TLS 校验",
                "信任所有证书"
            ]))
            .ToArray();
        var matches = productionIpMatches
            .Concat(certificateBypassMatches)
            .Concat(explicitTrustAllMatches)
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void ClientRules_ShouldDocumentHttpAsSupportedFieldPath()
    {
        var root = FindRepositoryRoot();
        var ruleDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端规则.md"));
        var planDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端架构治理清单.md"));
        var combinedDocs = string.Join(Environment.NewLine, ruleDoc, planDoc);
        var requiredText = new[]
        {
            "EdgeClient 现场链路必须继续支持 HTTP",
            "不得在客户端侧强制改成 HTTPS",
            "不得把证书、HSTS、HTTPS redirection",
            "HTTP 不等于允许弱凭据"
        };

        Assert.All(requiredText, text => Assert.Contains(text, combinedDocs, StringComparison.Ordinal));
    }

    [Fact]
    public void LauncherDevelopmentLayout_ShouldUseCrossPlatformProfileAndDotnetSyncTool()
    {
        var root = FindRepositoryRoot();
        var profileCatalog = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Edge",
            "IIoT.Edge.Launcher",
            "launcher.profiles.json"));
        var launcherProject = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Edge",
            "IIoT.Edge.Launcher",
            "IIoT.Edge.Launcher.csproj"));

        Assert.Contains("../host/IIoT.Edge.Shell", profileCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("../homogenization/IIoT.Edge.Shell", profileCatalog, StringComparison.Ordinal);
        Assert.DoesNotContain("IIoT.Edge.Shell.exe", profileCatalog, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RuntimeLayoutSync", launcherProject, StringComparison.Ordinal);
        Assert.DoesNotContain("powershell" + " -ExecutionPolicy", launcherProject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeLayoutSync_ShouldRemoveStaleShellAssembliesFromLauncherRoot()
    {
        var root = FindRepositoryRoot();
        var fileSystem = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Tools",
            "IIoT.Edge.RuntimeLayoutSync",
            "RuntimeLayoutSyncFileSystem.cs"));

        Assert.Contains("\"IIoT.Edge.Host.DataPipeline.dll\"", fileSystem, StringComparison.Ordinal);
        Assert.Contains("\"IIoT.Edge.Module.Sdk.dll\"", fileSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("\"IIoT.Edge.Application.dll\"", fileSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("\"IIoT.Edge.Domain.dll\"", fileSystem, StringComparison.Ordinal);
        Assert.Contains("\"Modules\"", fileSystem, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeLayoutSync_ShouldPublishSingleHostAndConfiguredPluginsRoot()
    {
        var root = FindRepositoryRoot();
        var app = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Tools",
            "IIoT.Edge.RuntimeLayoutSync",
            "RuntimeLayoutSyncApp.cs"));
        var fileSystem = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Tools",
            "IIoT.Edge.RuntimeLayoutSync",
            "RuntimeLayoutSyncFileSystem.cs"));

        Assert.Contains("fileSystem.CleanDirectory(hostRoot)", app, StringComparison.Ordinal);
        Assert.Contains("Path.Combine(hostRoot, \"Modules\")", app, StringComparison.Ordinal);
        Assert.Contains("SyncPluginsLayout(repoRoot, options.Configuration, manifest, layoutRoot)", app, StringComparison.Ordinal);
        Assert.Contains("RemoveLauncherShellArtifacts(launcherRuntimeRoot)", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveProfilePluginRootPath", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveProfilePluginRootPath", fileSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgramDataRootEnvironmentVariable", app, StringComparison.Ordinal);
        Assert.DoesNotContain("ProgramDataRootEnvironmentVariable", fileSystem, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegrationDependencyInjection_ShouldNotCacheTypedHttpClientsAsSingletons()
    {
        var root = FindRepositoryRoot();
        var dependencyInjection = File.ReadAllText(Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Integration",
            "DependencyInjection.cs"));

        Assert.DoesNotContain("AddHttpClient<AuthService>", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("AddHttpClient<DeviceService>", dependencyInjection, StringComparison.Ordinal);
        Assert.DoesNotContain("GetRequiredService<AuthService>", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient(AuthService.HttpClientName", dependencyInjection, StringComparison.Ordinal);
        Assert.Contains("AddHttpClient(DeviceService.HttpClientName", dependencyInjection, StringComparison.Ordinal);
    }

    [Fact]
    public void EfCoreSqliteConnection_ShouldEnableWalMode()
    {
        var root = FindRepositoryRoot();
        var efCoreRoot = Path.Combine(
            root,
            "src",
            "Infrastructure",
            "IIoT.Edge.Infrastructure.Persistence.EfCore",
            string.Empty);
        var efCoreSource = string.Join(
            Environment.NewLine,
            EnumerateFiles(efCoreRoot, "*.cs").Select(File.ReadAllText));

        Assert.Contains("IEdgeSqliteConnection", efCoreSource, StringComparison.Ordinal);
        Assert.Contains("AddSingleton<IEdgeSqliteConnection>", efCoreSource, StringComparison.Ordinal);
        Assert.Contains("PRAGMA journal_mode=WAL;", efCoreSource, StringComparison.Ordinal);
        Assert.Contains("BusyTimeoutMilliseconds = 5000", efCoreSource, StringComparison.Ordinal);
        Assert.Contains("PRAGMA busy_timeout={BusyTimeoutMilliseconds};", efCoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("static class EdgeSqliteConnection", efCoreSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Data Source=edge_design.db", efCoreSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SourceTree_ShouldNotReferenceOldContractProjects()
    {
        var root = FindRepositoryRoot();
        var matches = EnumerateFiles(root, "*.*")
            .Where(IsTextCandidate)
            .SelectMany(path => FindForbiddenMatches(root, path, ForbiddenContractNames))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void SourceTree_ShouldNotReferenceDeletedSdkArtifacts()
    {
        var root = FindRepositoryRoot();
        var matches = EnumerateFiles(root, "*.*")
            .Where(IsTextCandidate)
            .SelectMany(path => FindForbiddenMatches(root, path, DeletedSdkArtifactNames))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void SourceTree_ShouldNotReferenceDeletedOverWrappedApis()
    {
        var root = FindRepositoryRoot();
        var matches = EnumerateFiles(root, "*.*")
            .Where(IsTextCandidate)
            .SelectMany(path => FindForbiddenMatches(root, path, DeletedOverWrappedApiNames))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void EdgeDocs_ShouldNotDocumentLegacyLauncherUpdateJsonPascalCaseKeys()
    {
        var root = FindRepositoryRoot();
        var docsRoot = Path.Combine(root, "docs");
        var legacyPatterns = new[]
        {
            "`launcher.update.json` 中的 `Source`",
            "`launcher.update.json` 中的 `Channel`",
            "`launcher.update.json` 中的 `TargetRuntime`",
            "`Source`、`Channel`",
            "`Channel`、`TargetRuntime`"
        };

        var matches = EnumerateFiles(docsRoot, "*.md")
            .SelectMany(path => FindForbiddenMatches(root, path, legacyPatterns))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void EdgePackWorkflow_ShouldBuildOnWindowsAndPublishFromIntranetRunner()
    {
        var root = FindRepositoryRoot();
        var workflowPath = Path.Combine(root, ".github", "workflows", "edge-pack-modules.yml");
        var workflow = File.ReadAllText(workflowPath);

        Assert.Contains("workflow_dispatch:", workflow, StringComparison.Ordinal);
        Assert.Contains("release_notes:", workflow, StringComparison.Ordinal);
        Assert.Contains("Production Edge releases require explicit release notes", workflow, StringComparison.Ordinal);
        Assert.Contains("EDGE_RELEASE_VERSION", workflow, StringComparison.Ordinal);
        Assert.Contains("EDGE_RELEASE_CHANNEL", workflow, StringComparison.Ordinal);
        Assert.Contains("EDGE_RELEASE_NOTES_PATH", workflow, StringComparison.Ordinal);
        Assert.Contains("runs-on: windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("PackEdgeClientVelopack.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("-ReleaseNotes $env:EDGE_RELEASE_NOTES_PATH", workflow, StringComparison.Ordinal);
        Assert.Contains("TestEdgeVelopackPackage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("edge-installer-artifact", workflow, StringComparison.Ordinal);
        Assert.Contains("edge-velopack-releases", workflow, StringComparison.Ordinal);
        Assert.Contains("publish-edge-updates:", workflow, StringComparison.Ordinal);
        Assert.Contains("self-hosted", workflow, StringComparison.Ordinal);
        Assert.Contains("iiot-linux-prod", workflow, StringComparison.Ordinal);
        Assert.Contains(
            "actions/download-artifact@d3f86a106a0bac45b974a628896c90dbdf5c8093 # v4",
            workflow,
            StringComparison.Ordinal);
        Assert.DoesNotContain("actions/download-artifact@v4", workflow, StringComparison.Ordinal);
        Assert.Contains("EDGE_CLOUD_API_BASE_URL", workflow, StringComparison.Ordinal);
        Assert.Contains("IIOT_CLOUD_RELEASE_EMPLOYEE_NO", workflow, StringComparison.Ordinal);
        Assert.Contains("IIOT_CLOUD_RELEASE_PASSWORD", workflow, StringComparison.Ordinal);
        Assert.Contains("/human/identity/login", workflow, StringComparison.Ordinal);
        Assert.Contains("json.JSONDecodeError", workflow, StringComparison.Ordinal);
        Assert.Contains("accessToken", workflow, StringComparison.Ordinal);
        Assert.Contains("edge-release-bundles", workflow, StringComparison.Ordinal);
        Assert.Contains("Published Edge release through Cloud API", workflow, StringComparison.Ordinal);

        foreach (var forbidden in new[] { "scp", "ssh", "docker build", "ghcr.io", "Harbor" })
        {
            Assert.DoesNotContain(forbidden, workflow, StringComparison.OrdinalIgnoreCase);
        }

        var installerPublisher = File.ReadAllText(Path.Combine(root, "scripts", "PublishEdgeClientInstallerArtifact.ps1"));
        Assert.Contains("ReleaseNotes is required for Edge installer artifacts", installerPublisher, StringComparison.Ordinal);
        foreach (var forbidden in new[] { "SshTarget", "RemoteEdgeUpdatesDir", "scp", "ssh" })
        {
            Assert.DoesNotContain(forbidden, installerPublisher, StringComparison.OrdinalIgnoreCase);
        }

        var localPublisher = File.ReadAllText(Path.Combine(root, "scripts", "LocalPublishAndDeploy.ps1"));
        Assert.Contains("Production Edge release notes are required", localPublisher, StringComparison.Ordinal);
        Assert.Contains("Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgeHost", localPublisher, StringComparison.Ordinal);
        Assert.Contains("ResumeReleaseRoot", localPublisher, StringComparison.Ordinal);

        var pluginPublisher = File.ReadAllText(Path.Combine(root, "scripts", "PublishEdgePluginRelease.ps1"));
        Assert.Contains("Assert-EdgeWorkspaceDispatch -ExpectedTarget EdgePlugin", pluginPublisher, StringComparison.Ordinal);
        Assert.Contains("No package build was started", pluginPublisher, StringComparison.Ordinal);

        var deploymentCommon = File.ReadAllText(Path.Combine(root, "scripts", "EdgeDeployment.Common.ps1"));
        Assert.Contains("--fail-with-body", deploymentCommon, StringComparison.Ordinal);
        Assert.Contains("--connect-timeout", deploymentCommon, StringComparison.Ordinal);
        Assert.Contains("--max-time", deploymentCommon, StringComparison.Ordinal);
        Assert.Contains("--speed-time", deploymentCommon, StringComparison.Ordinal);
        Assert.Contains("Another Edge release is active", deploymentCommon, StringComparison.Ordinal);
    }

    [Fact]
    public void EdgeInstallUpdateDocs_ShouldDocumentStandardArtifactPublishPath()
    {
        var root = FindRepositoryRoot();
        var installDoc = File.ReadAllText(Path.Combine(root, "docs", "Edge安装更新验收.md"));
        var contractDoc = File.ReadAllText(Path.Combine(root, "docs", "Edge客户端宿主插件分发契约.md"));
        var ruleDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端规则.md"));

        Assert.Contains("唯一客户端侧验收入口", installDoc, StringComparison.Ordinal);
        Assert.Contains("GitHub hosted Windows runner", installDoc, StringComparison.Ordinal);
        Assert.Contains("内网 Linux self-hosted runner", installDoc, StringComparison.Ordinal);
        Assert.Contains("/srv/iiot/edge-updates", installDoc, StringComparison.Ordinal);
        Assert.Contains("EdgeClient 不发布 Docker 镜像，不推 Harbor", installDoc, StringComparison.Ordinal);
        Assert.Contains("更新内容必须显式填写", installDoc, StringComparison.Ordinal);
        Assert.Contains("macOS 是主力开发环境", installDoc, StringComparison.Ordinal);
        Assert.Contains("Windows 是现场部署目标", installDoc, StringComparison.Ordinal);
        Assert.Contains("两层证据必须分别记录，互不替代", installDoc, StringComparison.Ordinal);
        Assert.Contains("Windows 实机部署验收未执行", installDoc, StringComparison.Ordinal);
        Assert.Contains("GitHub hosted Windows runner 只负责构建", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("开发阶段以 macOS 主力开发环境", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("macOS 通过不能替代 Windows 部署验收", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("LocalPublishAndDeploy.ps1", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("内网 Linux self-hosted runner 只负责", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("CI artifact 发布契约", contractDoc, StringComparison.Ordinal);
        Assert.Contains("内网 Linux runner 只做发布编排，不重新构建 EdgeClient", contractDoc, StringComparison.Ordinal);
        Assert.Contains("通过 Cloud Human API 上传 release bundle", contractDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void EdgeDocs_ShouldPreserveChangeClosureAndPlcSelectionContracts()
    {
        var root = FindRepositoryRoot();
        var ruleDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端规则.md"));
        var retrospectiveDoc = File.ReadAllText(Path.Combine(root, "docs", "改动复盘与规则沉淀.md"));
        var plcSelectionDoc = File.ReadAllText(Path.Combine(root, "docs", "PLC选择与状态展示控制.md"));
        var uiSecondLayerDoc = File.ReadAllText(Path.Combine(root, "docs", "Edge客户端UI第二层规范.md"));
        var combinedDocs = string.Join(
            Environment.NewLine,
            ruleDoc,
            retrospectiveDoc,
            plcSelectionDoc,
            uiSecondLayerDoc);

        Assert.Contains("改动复盘与规则沉淀.md", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("PLC选择与状态展示控制.md", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("项目滚动复盘文档", retrospectiveDoc, StringComparison.Ordinal);
        Assert.Contains("改动范围", retrospectiveDoc, StringComparison.Ordinal);
        Assert.Contains("规则提炼", retrospectiveDoc, StringComparison.Ordinal);
        Assert.Contains("无新增长期规则", retrospectiveDoc, StringComparison.Ordinal);

        Assert.Contains("IDeviceSelectionService", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("Edge客户端UI第二层规范.md", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("统一的是页面组合规则", uiSecondLayerDoc, StringComparison.Ordinal);
        Assert.Contains("EdgeActionToolbar", uiSecondLayerDoc, StringComparison.Ordinal);
        Assert.Contains("EdgeTablePanel.HeaderMetaContent", uiSecondLayerDoc, StringComparison.Ordinal);
        Assert.Contains("未配置工序", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("IoMappingEntity", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("单点读数据", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("连续读数据", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("单点写数据", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("连续写数据", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("IO 映射与 IO 交互同源契约", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("同源、同序、同字段", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("右侧设备运行卡片信息契约", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("暂无主批计划数据", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("PLC 状态表", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("人工显示/查询筛选器", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("不得自动改全局设备号", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("未采集", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("按当前筛选结果批量", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("同步运维", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("实时监控", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("状态机", combinedDocs, StringComparison.Ordinal);
        Assert.Contains("任务绑定", combinedDocs, StringComparison.Ordinal);

        Assert.Contains("右侧“设备运行”的设备号是唯一操作入口和唯一发布者", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("设备号是人工显示/查询筛选器", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("禁止调用 `SelectDevice(...)` 发布全局设备号", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("写操作只允许落到明确单个目标", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("Dashboard `PLC 状态表` 必须以已配置 PLC 为基准行", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("不得出现 `0 / 12` 但表格空白", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("页面打开、刷新、激活、加载、无数据、无快照、空态或匹配失败时，不得自动改全局设备号", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("设备号永远不得作为写入、保存、删除、重试、重入队、启停、状态机执行、PLC 任务调度、Cloud/MES 同步范围或后台任务范围的参数", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("IO 交互与硬件 IO 映射必须强制同源、同序、同字段", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("IO 交互页必须按 IO 映射页同一五分类展示", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("IO 交互各分类区域的标题文案必须与 IO 映射分类名一致", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("主批计划无数据时必须显示明确空态", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("优先来自当前配方/运行快照的 `ProcessName`", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("不得显示“数据”这种泛化菜单名", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("启动诊断必须订阅右侧设备号产出显示集合", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("同步运维必须保留 Cloud/MES 通道总览的全局链路语义", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("可归属到 PLC 的死信、待传、失败和重试记录默认显示全部，选择具体 PLC 后只显示该 PLC 记录", plcSelectionDoc, StringComparison.Ordinal);
        Assert.Contains("UI 改动必须真实运行或截图验收", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("build 通过不等于 UI 通过", ruleDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void ClientRules_ShouldDocumentStablePlcRuntimeSnapshotContract()
    {
        var root = FindRepositoryRoot();
        var ruleDoc = File.ReadAllText(Path.Combine(root, "docs", "客户端规则.md"));

        Assert.Contains("持久化的 `PlcCode` 作为稳定身份", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("设备改名不得改变", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("配置全集的完整快照", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("发送合法空列表", ruleDoc, StringComparison.Ordinal);
        Assert.Contains("`RuntimeStatus` 必须由客户端明确分类", ruleDoc, StringComparison.Ordinal);
    }

    [Fact]
    public void SourceTree_ShouldNotReferenceRemovedMapperOrUnusedCentralPackages()
    {
        var root = FindRepositoryRoot();
        var forbiddenNames = new[]
        {
            "Auto" + "Mapper",
            "Microsoft.Extensions." + "DependencyModel"
        };
        var files = EnumerateFiles(Path.Combine(root, "src"), "*.*")
            .Where(IsTextCandidate)
            .Append(Path.Combine(root, "Directory.Packages.props"))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var nameMatches = files
            .SelectMany(path => FindForbiddenMatches(root, path, forbiddenNames))
            .ToArray();
        var usageMatches = files
            .SelectMany(path => RemovedMapperUsagePattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} contains removed mapper usage at offset {match.Index}"))
            .ToArray();

        Assert.Empty(nameMatches.Concat(usageMatches));
    }

    [Fact]
    public void Application_ShouldNotReintroducePresentationModelsOrObservableBase()
    {
        var root = FindRepositoryRoot();
        var applicationRoot = Path.Combine(root, "src", "Application", "IIoT.Edge.Application");
        var files = EnumerateFiles(applicationRoot, "*.cs").ToArray();

        var typeMatches = files
            .SelectMany(path => ApplicationUiFacingTypePattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} declares UI-facing type {match.Groups[1].Value}"))
            .ToArray();
        var fileMatches = files
            .Where(path =>
            {
                var name = Path.GetFileNameWithoutExtension(path);
                return name.EndsWith("Vm", StringComparison.Ordinal)
                    || name.EndsWith("ViewModel", StringComparison.Ordinal)
                    || name.EndsWith("ViewModels", StringComparison.Ordinal);
            })
            .Select(path => $"{ToRepositoryPath(root, path)} keeps a UI-facing file name")
            .ToArray();
        var observableMatches = files
            .Where(path => File.ReadAllText(path).Contains("Observable" + "ModelBase", StringComparison.Ordinal))
            .Select(path => $"{ToRepositoryPath(root, path)} references ObservableModelBase")
            .ToArray();

        Assert.Empty(typeMatches.Concat(fileMatches).Concat(observableMatches));
    }

    [Fact]
    public void PresentationRecipeView_ShouldNotKeepDuplicateMediatRUseCases()
    {
        var root = FindRepositoryRoot();
        var recipeViewRoot = Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Features",
            "Formula",
            "RecipeView");
        var forbiddenNames = new[]
        {
            "IRequest",
            "IRequestHandler",
            "GetRecipeViewSnapshotQuery",
            "GetIsLocalAdminQuery",
            "SyncRecipeFromCloudCommand",
            "SwitchRecipeSourceCommand",
            "SaveLocalRecipeParamCommand",
            "DeleteLocalRecipeParamCommand"
        };

        var matches = EnumerateFiles(recipeViewRoot, "*.cs")
            .SelectMany(path => FindForbiddenMatches(root, path, forbiddenNames))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void PresentationViewModels_ShouldNotDependOnMediatRSender()
    {
        var root = FindRepositoryRoot();
        var presentationRoot = Path.Combine(root, "src", "Presentation");

        var matches = EnumerateFiles(presentationRoot, "*.cs")
            .Where(path => ToRepositoryPath(root, path).Contains("/ViewModels/", StringComparison.Ordinal))
            .SelectMany(path => FindForbiddenMatches(root, path, ["ISender", "using MediatR"]))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void Presentation_ShouldNotDefineMediatRRequestUseCases()
    {
        var root = FindRepositoryRoot();
        var presentationRoot = Path.Combine(root, "src", "Presentation");

        var matches = EnumerateFiles(presentationRoot, "*.cs")
            .SelectMany(path => FindForbiddenMatches(root, path, ["IRequest", "IRequestHandler"]))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void DeviceSelection_ShouldOnlyBePublishedByEquipmentPanel()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var allowedPublishers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src/Presentation/IIoT.Edge.Presentation.Panels/Features/DeviceSelection/DeviceSelectionService.cs",
            "src/Presentation/IIoT.Edge.Presentation.Panels/Features/Equipment/ViewModels/EquipmentViewModel.cs"
        };
        var selectDeviceCallPattern = new Regex(@"\bSelectDevice\s*\(", RegexOptions.CultureInvariant);

        var matches = EnumerateFiles(sourceRoot, "*.cs")
            .Where(path => !ToRepositoryPath(root, path).StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !allowedPublishers.Contains(ToRepositoryPath(root, path)))
            .SelectMany(path => selectDeviceCallPattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} publishes device selection at offset {match.Index}"))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void EquipmentPanel_CurrentProcess_ShouldPreferBusinessProcessName()
    {
        var root = FindRepositoryRoot();
        var source = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Panels/Features/Equipment/ViewModels/EquipmentViewModel.cs"));
        var processNameIndex = source.IndexOf("NormalizeDisplayText(ProcessName)", StringComparison.Ordinal);
        var menuTitleIndex = source.IndexOf("_viewRegistry.GetAllMenus()", StringComparison.Ordinal);

        Assert.True(processNameIndex >= 0, "Equipment panel must use ProcessName as the current process business name.");
        Assert.True(menuTitleIndex >= 0, "Equipment panel fallback menu lookup should remain explicit.");
        Assert.True(
            processNameIndex < menuTitleIndex,
            "Equipment panel must prefer ProcessName before falling back to menu titles such as “数据”.");
    }

    [Fact]
    public void EquipmentPanel_CurrentProcessSlot_ShouldStayVisible()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Panels/Features/Equipment/Views/EquipmentView.axaml"));
        var zhResources = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Panels/Resources/Languages/zh-CN.axaml"));
        var enResources = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Panels/Resources/Languages/en-US.axaml"));

        Assert.Contains("Panels_Label_CurrentProcess", xaml, StringComparison.Ordinal);
        Assert.Contains("CurrentProcessDisplayName", xaml, StringComparison.Ordinal);
        Assert.Contains("未配置工序", zhResources, StringComparison.Ordinal);
        Assert.Contains("Process not configured", enResources, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareCrudPages_ShouldUseSharedTableToolbarContract()
    {
        var root = FindRepositoryRoot();
        var pagePaths = new[]
        {
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/NetworkDevicePage.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/SerialDevicePage.axaml"
        };

        foreach (var pagePath in pagePaths)
        {
            var xaml = File.ReadAllText(ToFullPath(root, pagePath));
            Assert.Contains("<edge:EdgeTablePanel Classes=\"fill\"", xaml, StringComparison.Ordinal);
            Assert.Contains("<edge:EdgeTablePanel.HeaderMetaContent>", xaml, StringComparison.Ordinal);
            Assert.Contains("<edge:EdgeActionToolbar>", xaml, StringComparison.Ordinal);
            Assert.Contains("ViewportMaxHeight=\"0\"", xaml, StringComparison.Ordinal);
            AssertActionOrder(
                xaml,
                "Navigation_Button_Add",
                "Kind=\"Primary\"",
                "Navigation_Button_Edit",
                "Kind=\"Secondary\"",
                "Navigation_Button_Delete",
                "Kind=\"Danger\"");
        }
    }

    [Fact]
    public void NetworkDevicePage_ShouldUseResponsiveColumnsAndGroupedSharedDialog()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/NetworkDevicePage.axaml"));

        Assert.Contains("x:Name=\"NetworkDeviceDialog\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Navigation_NetworkDevice_IdentitySection", xaml, StringComparison.Ordinal);
        Assert.Contains("Navigation_NetworkDevice_ConnectionSection", xaml, StringComparison.Ordinal);
        Assert.Contains("Navigation_NetworkDevice_AdvancedSection", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeCard", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding IpAddress}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("MinWidth=\"134\" Width=\"1.35*\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"180\" Width=\"2*\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ShellWindowRegion_ShouldMatchStageCornerRadiusToken()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(root, "src/Edge/IIoT.Edge.Shell/MainWindow.axaml"));
        var code = File.ReadAllText(ToFullPath(root, "src/Edge/IIoT.Edge.Shell/MainWindow.axaml.cs"));

        Assert.Contains("CornerRadius=\"{DynamicResource Edge.CornerRadius.Stage}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("private const int WindowCornerRadius = 16;", code, StringComparison.Ordinal);
        Assert.Contains("Height=\"{DynamicResource Edge.Size.HeaderHeight}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Height=\"{DynamicResource Edge.Size.FooterHeight}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Width=\"{DynamicResource Edge.Size.RightRailWidth}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDefinitions=\"50,*,30\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ColumnDefinitions=\"56,*,420\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void IoMappingPage_ShouldUseTemplateEditToolbarContract()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/IoMappingPage.axaml"));
        var coordinator = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/ViewModels/HardwareConfigLoadSaveCoordinator.cs"));
        var zhResources = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Resources/Languages/zh-CN.axaml"));

        Assert.DoesNotContain("<edge:EdgeSectionHeader.ActionContent>", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeTablePanel Classes=\"fill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeTablePanel.ActionContent>", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeActionToolbar>", xaml, StringComparison.Ordinal);
        AssertActionOrder(
            xaml,
            "ApplyModuleTemplateCommand",
            "Navigation_Button_ApplyTemplate",
            "OpenEditIoMappingDialogCommand",
            "Navigation_Button_Edit");
        Assert.Contains("重置标准点位", zhResources, StringComparison.Ordinal);
        Assert.DoesNotContain(">套用模板<", zhResources, StringComparison.Ordinal);
        Assert.DoesNotContain(">播种<", zhResources, StringComparison.Ordinal);
        Assert.Contains("IsEmpty=\"{Binding HasNoInteractionIoMappingGroups}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Binding=\"{Binding Remark}\" Header=\"{DynamicResource Navigation_Column_Remark}\" MinWidth=\"240\" Width=\"*\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth=\"220\" Width=\"220\"", xaml, StringComparison.Ordinal);
        Assert.Equal(1, xaml.Split("Navigation_Empty_IoInteractionRows", StringSplitOptions.None).Length - 1);
        Assert.Contains("ConfirmResetModuleTemplateAsync", coordinator, StringComparison.Ordinal);
        Assert.Contains("清空当前 PLC 已有 IO 映射", coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigAndIoTables_ShouldAvoidUnboundedScrollHostStackPanelLayout()
    {
        var root = FindRepositoryRoot();
        var pagePaths = new[]
        {
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/IoMappingPage.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/IOView/Views/IOViewPage.axaml"
        };

        foreach (var pagePath in pagePaths)
        {
            var xaml = File.ReadAllText(ToFullPath(root, pagePath));
            Assert.DoesNotContain("<edge:EdgeScrollHost Variant=\"Panel\">\r\n                    <StackPanel", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("<edge:EdgeScrollHost Variant=\"Panel\">\n                    <StackPanel", xaml, StringComparison.Ordinal);
            Assert.Contains("ViewportMaxHeight=\"0\"", xaml, StringComparison.Ordinal);
            Assert.Contains("<edge:EdgeTablePanel.HeaderMetaContent>", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void PlcTaskBindingPage_ShouldUseContentDrivenBoundedTable()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/PlcTaskBindingView/Views/PlcTaskBindingPage.axaml"));

        Assert.Contains("RowDefinitions=\"Auto,Auto,Auto,*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEmpty=\"{Binding !SelectedDeviceTasks.Count}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewportMaxHeight=\"320\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<edge:EdgeTablePanel\n            Grid.Row=\"2\"\n            Classes=\"fill\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewportMaxHeight=\"0\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void CapacityView_ShouldUseFillTableLayoutAndSharedToolbar()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Production/CapacityView/Views/CapacityViewPage.axaml"));

        Assert.DoesNotContain("<edge:EdgeScrollHost>", xaml, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,Auto,Auto,*,Auto\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeTablePanel", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"fill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEmpty=\"{Binding IsDailyRecordsEmpty}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("EmptyTitle=\"{DynamicResource Navigation_Capacity_EmptyTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeTablePanel.HeaderMetaContent>", xaml, StringComparison.Ordinal);
        Assert.Contains("<edge:EdgeActionToolbar>", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewportMaxHeight=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<sharedViews:EmptyStateView\r\n                        IsVisible=\"{Binding IsDailyRecordsEmpty}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("<sharedViews:EmptyStateView\n                        IsVisible=\"{Binding IsDailyRecordsEmpty}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"180\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsVisible=\"{Binding HasChartRecords}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void MonitorView_ShouldUseFillTablesInsteadOfFixedSmallHeights()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Production/Monitor/Views/MonitorView.axaml"));

        Assert.DoesNotContain("<edge:EdgeScrollHost", xaml, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,*,*\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"{DynamicResource Navigation_Monitor_CurrentCellsTableTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Title=\"{DynamicResource Navigation_Monitor_CellFieldsTableTitle}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Classes=\"fill\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ViewportMaxHeight=\"0\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewportMaxHeight=\"220\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid MinHeight=\"260\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Grid MinHeight=\"150\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEmpty=\"{Binding IsCellDebugEmpty}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("IsEmpty=\"{Binding IsSelectedCellEmpty}\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void RecipePage_ShouldKeepRecipeSpecificLayout()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Formula/RecipeView/Views/RecipeViewPage.axaml"));

        Assert.Contains("EdgeInfoSummaryCard", xaml, StringComparison.Ordinal);
        Assert.Contains("Navigation_Label_EmergencyEdit", xaml, StringComparison.Ordinal);
        Assert.Contains(
            "x:Name=\"EmergencyEditCard\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsVisible=\"{Binding IsLocalAdmin}\"",
            xaml,
            StringComparison.Ordinal);
        Assert.Contains("DeleteLocalParamCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Danger\"", xaml, StringComparison.Ordinal);
        var deleteCommandIndex = xaml.IndexOf("DeleteLocalParamCommand", StringComparison.Ordinal);
        var deleteButtonEndIndex = xaml.IndexOf("/>", deleteCommandIndex, StringComparison.Ordinal);
        Assert.True(deleteCommandIndex >= 0 && deleteButtonEndIndex > deleteCommandIndex);
        Assert.DoesNotContain(
            "IsVisible",
            xaml[deleteCommandIndex..deleteButtonEndIndex],
            StringComparison.Ordinal);
        Assert.Contains("Navigation_Button_SwitchDataSource", xaml, StringComparison.Ordinal);
        Assert.Contains("Kind=\"Secondary\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("AddRecipe", xaml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DeleteRecipeCommand", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void DeviceSelection_ShouldNotExposeSecondActionableDeviceSelector()
    {
        var root = FindRepositoryRoot();
        var allowedSelectorOwners = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "src/Presentation/IIoT.Edge.Presentation.Panels/Features/Equipment/Views/EquipmentView.axaml"
        };
        var forbiddenBindings = new[]
        {
            "ItemsSource=\"{Binding DeviceFilters",
            "SelectedItem=\"{Binding SelectedDeviceFilter",
            "ItemsSource=\"{Binding IoMappingNetworkDevices",
            "SelectedItem=\"{Binding SelectedNetworkDevice"
        };

        var matches = EnumerateFiles(Path.Combine(root, "src"), "*.axaml")
            .Where(path => !allowedSelectorOwners.Contains(ToRepositoryPath(root, path)))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return forbiddenBindings
                    .Where(binding => source.Contains(binding, StringComparison.Ordinal))
                    .Select(binding => $"{ToRepositoryPath(root, path)} exposes local device selector binding '{binding}'");
            })
            .ToArray();

        Assert.Empty(matches);
    }

    private static void AssertActionOrder(string source, params string[] fragments)
    {
        var previous = -1;
        foreach (var fragment in fragments)
        {
            var index = source.IndexOf(fragment, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected fragment '{fragment}' to exist.");
            Assert.True(index > previous, $"Expected fragment '{fragment}' to appear after the previous toolbar fragment.");
            previous = index;
        }
    }

    [Fact]
    public void DeviceSelection_ShouldStayOutOfRuntimeAndIntegrationLayers()
    {
        var root = FindRepositoryRoot();
        var sourceRoot = Path.Combine(root, "src");
        var forbiddenSymbols = new[]
        {
            "IDeviceSelectionService",
            "SelectedDeviceKey"
        };

        static bool IsAllowedUiContext(string repositoryPath)
            => repositoryPath.StartsWith("src/Presentation/", StringComparison.OrdinalIgnoreCase)
               || (repositoryPath.StartsWith("src/Modules/", StringComparison.OrdinalIgnoreCase)
                   && repositoryPath.Contains("/Presentation/", StringComparison.OrdinalIgnoreCase));

        var matches = EnumerateFiles(sourceRoot, "*.cs")
            .Where(path => !ToRepositoryPath(root, path).StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
            .Where(path => !IsAllowedUiContext(ToRepositoryPath(root, path)))
            .SelectMany(path =>
            {
                var source = File.ReadAllText(path);
                return forbiddenSymbols
                    .Where(symbol => source.Contains(symbol, StringComparison.Ordinal))
                    .Select(symbol => $"{ToRepositoryPath(root, path)} references UI device selection symbol '{symbol}'");
            })
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void HardwareIoMappingPage_ShouldNotExposeSecondActionableDeviceSelector()
    {
        var root = FindRepositoryRoot();
        var ioMappingPage = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/IoMappingPage.axaml"));

        Assert.Contains("SelectedNetworkDeviceDisplayName", ioMappingPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding IoMappingNetworkDevices", ioMappingPage, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectedItem=\"{Binding SelectedNetworkDevice", ioMappingPage, StringComparison.Ordinal);
    }

    [Fact]
    public void HardwareConfigPage_ShouldNotExposeGlobalSaveButton()
    {
        var root = FindRepositoryRoot();
        var hardwareConfigPage = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/HardwareConfigPage.axaml"));

        Assert.DoesNotContain("Command=\"{Binding SaveCommand}\"", hardwareConfigPage, StringComparison.Ordinal);
    }

    [Fact]
    public void IoInteractionPage_ShouldFollowIoMappingFiveCategories()
    {
        var root = FindRepositoryRoot();
        var ioViewPage = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/IOView/Views/IOViewPage.axaml"));
        var ioMappingPage = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/IoMappingPage.axaml"));
        var zhResources = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Resources/Languages/zh-CN.axaml"));
        var legacyDataSectionsKey = "Navigation_Empty_Io" + "DataSections";
        var legacyArraySectionsKey = "Navigation_Empty_Io" + "ArraySections";

        Assert.Contains("Navigation_IoMapping_TabInteraction", ioViewPage, StringComparison.Ordinal);
        Assert.Contains("Navigation_IoMapping_TabSingleRead", ioViewPage, StringComparison.Ordinal);
        Assert.Contains("Navigation_IoMapping_TabContinuousRead", ioViewPage, StringComparison.Ordinal);
        Assert.Contains("Navigation_IoMapping_TabSingleWrite", ioViewPage, StringComparison.Ordinal);
        Assert.Contains("Navigation_IoMapping_TabContinuousWrite", ioViewPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigation_IoView_TabInteraction", ioViewPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigation_IoView_TabData", ioViewPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigation_IoView_TabMatrix", ioViewPage, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigation_IoView_TabInteraction", zhResources, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigation_IoView_TabData", zhResources, StringComparison.Ordinal);
        Assert.DoesNotContain("Navigation_IoView_TabMatrix", zhResources, StringComparison.Ordinal);
        Assert.DoesNotContain(">单点读写<", zhResources, StringComparison.Ordinal);
        Assert.DoesNotContain(">连续读写<", zhResources, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewportMaxHeight=\"320\"", ioViewPage, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewportMaxHeight=\"360\"", ioViewPage, StringComparison.Ordinal);
        Assert.DoesNotContain(legacyDataSectionsKey, ioViewPage, StringComparison.Ordinal);
        Assert.DoesNotContain(legacyArraySectionsKey, ioViewPage, StringComparison.Ordinal);
        Assert.DoesNotContain(legacyDataSectionsKey, ioMappingPage, StringComparison.Ordinal);
        Assert.DoesNotContain(legacyArraySectionsKey, ioMappingPage, StringComparison.Ordinal);
        Assert.DoesNotContain(legacyDataSectionsKey, zhResources, StringComparison.Ordinal);
        Assert.DoesNotContain(legacyArraySectionsKey, zhResources, StringComparison.Ordinal);
    }

    [Fact]
    public void Shell_ShouldUseRealIconAndAutoSizedEquipmentRail()
    {
        var root = FindRepositoryRoot();
        var mainWindow = File.ReadAllText(ToFullPath(
            root,
            "src/Edge/IIoT.Edge.Shell/MainWindow.axaml"));
        var installerService = File.ReadAllText(ToFullPath(
            root,
            "src/Edge/IIoT.Edge.Installer/InstallerService.cs"));

        Assert.Contains("Icon=\"avares://IIoT.Edge.UI.Shared/Assets/images/icon.ico\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("RowDefinitions=\"Auto,12,*\"", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("MinHeight=\"260\"", mainWindow, StringComparison.Ordinal);
        Assert.Contains("shortcut.IconLocation", installerService, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleProjects_ShouldDeclareExplicitPluginRole()
    {
        var root = FindRepositoryRoot();
        var modulesRoot = ToFullPath(root, "src/Modules");
        var findings = EnumerateFiles(modulesRoot, "*.csproj")
            .Where(path => !Path.GetFileNameWithoutExtension(path).Equals(
                "IIoT.Edge.Module.Sdk",
                StringComparison.OrdinalIgnoreCase))
            .SelectMany(path =>
            {
                var project = XDocument.Load(path);
                var projectName = Path.GetFileNameWithoutExtension(path);
                var projectDirectory = Path.GetDirectoryName(path)
                    ?? throw new InvalidOperationException($"无法定位项目目录：{path}");
                var moduleId = GetProjectProperty(project, "PluginModuleId");
                var isEdgePluginModule = GetProjectProperty(project, "IsEdgePluginModule");
                var isPackable = GetProjectProperty(project, "IsPackable");
                var hasPluginManifest = File.Exists(Path.Combine(projectDirectory, "plugin.json"));
                var repoPath = ToRepositoryPath(root, path);

                return new[]
                {
                    !string.IsNullOrWhiteSpace(moduleId)
                        ? null
                        : $"{repoPath} has no PluginModuleId",
                    hasPluginManifest ? null : $"{repoPath} is missing plugin.json",
                    string.Equals(isEdgePluginModule, "true", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : $"{repoPath} must declare IsEdgePluginModule=true",
                    string.Equals(isPackable, "true", StringComparison.OrdinalIgnoreCase)
                        ? null
                        : $"{repoPath} must declare IsPackable=true",
                    projectName.EndsWith(".Shared", StringComparison.OrdinalIgnoreCase)
                        ? $"{repoPath} must be an independently loadable plugin, not a shared-family project"
                        : null
                };
            })
            .Where(finding => !string.IsNullOrWhiteSpace(finding))
            .Select(finding => finding!)
            .ToArray();

        Assert.Empty(findings);
    }

    [Fact]
    public void TestPluginFixture_ShouldRemainOutsideProductionPackagingInputs()
    {
        var root = FindRepositoryRoot();
        var fixtureProject = XDocument.Load(ToFullPath(
            root,
            "src/Testing/IIoT.Edge.TestPlugin/IIoT.Edge.TestPlugin.csproj"));

        Assert.Equal("true", GetProjectProperty(fixtureProject, "IsEdgePluginTestFixture"));
        Assert.Equal("false", GetProjectProperty(fixtureProject, "IsPackable"));
        Assert.DoesNotContain(
            "TestPlugin",
            File.ReadAllText(ToFullPath(root, "scripts/edge-runtime.publish.json")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TestPlugin",
            File.ReadAllText(ToFullPath(root, "scripts/PluginBundles/all-official.json")),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "TestPlugin",
            File.ReadAllText(ToFullPath(root, "src/Edge/IIoT.Edge.Launcher/launcher.profiles.json")),
            StringComparison.Ordinal);
        Assert.Contains(
            "Get-EdgeModuleProjectMap -RepoRoot $repoRoot",
            File.ReadAllText(ToFullPath(root, "scripts/PackEdgePlugin.ps1")),
            StringComparison.Ordinal);
        Assert.Contains(
            "Join-Path $RepoRoot 'src\\Modules'",
            File.ReadAllText(ToFullPath(root, "scripts/EdgeRuntime.Common.ps1")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeLogs_ShouldNotUseLegacyEnglishVisiblePrefixes()
    {
        var root = FindRepositoryRoot();
        var sourceRoots = new[]
        {
            "src/Application",
            "src/Infrastructure",
            "src/Edge",
            "src/Modules"
        };
        var forbiddenVisibleLogTexts = new[]
        {
            "[DataPipeline]",
            "[ContextStore]",
            "[Retry-Cloud]",
            "[Retry-MES]",
            "[Cloud]",
            "[DeviceLogSync]",
            "[RecipeSync]",
            "[Recipe]",
            "[CapacitySync]",
            "[Background]",
            "Initialized and started",
            "task(s)",
            "Task failed",
            "timeout_exceeded",
            "consumer_returned_false"
        };

        var findings = sourceRoots
            .Select(path => ToFullPath(root, path))
            .SelectMany(path => EnumerateFiles(path, "*.cs"))
            .Where(path => !ToRepositoryPath(root, path).StartsWith("src/Tests/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => FindForbiddenMatches(root, path, forbiddenVisibleLogTexts))
            .ToArray();

        Assert.Empty(findings);
    }

    [Fact]
    public void SourceTree_ShouldNotContainMojibakeMarkers()
    {
        var root = FindRepositoryRoot();
        var matches = EnumerateFiles(root, "*.*")
            .Where(IsTextCandidate)
            .SelectMany(path => FindForbiddenMatches(root, path, MojibakeMarkers))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void ApplicationAbstractions_ShouldNotContainImplementationHelpers()
    {
        var root = FindRepositoryRoot();
        var abstractionsRoot = Path.Combine(root, "src", "Application", "IIoT.Edge.Application", "Abstractions");
        var forbiddenPatterns = new[]
        {
            new Regex(@"\b(static|internal\s+static|public\s+static)\s+class\b", RegexOptions.CultureInvariant),
            new Regex(@"\bclass\s+\w*Helper\b", RegexOptions.CultureInvariant),
            new Regex(@"\b(File|Directory)\.", RegexOptions.CultureInvariant),
            new Regex(@"\bSHA256\b", RegexOptions.CultureInvariant),
            new Regex(@"\bTask\.Delay\b", RegexOptions.CultureInvariant)
        };

        var matches = EnumerateFiles(abstractionsRoot, "*.cs")
            .SelectMany(path =>
            {
                var text = File.ReadAllText(path);
                return forbiddenPatterns
                    .Where(pattern => pattern.IsMatch(text))
                    .Select(pattern => $"{ToRepositoryPath(root, path)} contains implementation detail pattern {pattern}");
            })
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void NavigationLanguageDictionaries_ShouldHaveSameResourceKeys()
    {
        var root = FindRepositoryRoot();
        var languageRoot = Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Resources",
            "Languages");

        var zhKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "zh-CN.axaml"));
        var enKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "en-US.axaml"));

        Assert.Empty(zhKeys.Except(enKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Empty(enKeys.Except(zhKeys, StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void NavigationLanguageDictionaries_ShouldNotKeepHostProcessDisplayKeys()
    {
        var root = FindRepositoryRoot();
        var languageRoot = Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Resources",
            "Languages");

        var processKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "zh-CN.axaml"))
            .Union(GetXamlResourceKeys(Path.Combine(languageRoot, "en-US.axaml")), StringComparer.Ordinal)
            .Where(key => key.StartsWith("Navigation_Process_", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(processKeys);
    }

    [Fact]
    public void NavigationFeatureResourceLookups_ShouldExistInLanguageDictionaries()
    {
        var root = FindRepositoryRoot();
        var navigationRoot = Path.Combine(root, "src", "Presentation", "IIoT.Edge.Presentation.Navigation");
        var languageRoot = Path.Combine(navigationRoot, "Resources", "Languages");
        var dictionaryKeys = GetXamlResourceKeys(Path.Combine(languageRoot, "zh-CN.axaml"))
            .Union(GetXamlResourceKeys(Path.Combine(languageRoot, "en-US.axaml")), StringComparer.Ordinal)
            .ToHashSet(StringComparer.Ordinal);

        var missingKeys = EnumerateFiles(Path.Combine(navigationRoot, "Features"), "*.cs")
            .SelectMany(path => ResourceLookupPattern
                .Matches(File.ReadAllText(path))
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .Where(key => !dictionaryKeys.Contains(key))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(missingKeys);
    }

    [Fact]
    public void NavigationFeatures_ShouldNotCreateVisibleChineseValidationIssuesDirectly()
    {
        var root = FindRepositoryRoot();
        var featureRoot = Path.Combine(
            root,
            "src",
            "Presentation",
            "IIoT.Edge.Presentation.Navigation",
            "Features");

        var matches = EnumerateFiles(featureRoot, "*.cs")
            .SelectMany(path => DirectVisibleValidationIssuePattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} contains direct visible validation text at offset {match.Index}"))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void Tests_ShouldNotUseLongFixedTaskDelaysForSynchronization()
    {
        var root = FindRepositoryRoot();
        var testRoot = Path.Combine(root, "src", "Tests");

        var matches = EnumerateFiles(testRoot, "*.cs")
            .SelectMany(path => LongTaskDelayPattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} contains long fixed Task.Delay at offset {match.Index}"))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void ShellVisibleXaml_ShouldUseDynamicResourcesForChineseText()
    {
        var root = FindRepositoryRoot();
        var xamlRoots = new[]
        {
            Path.Combine(root, "src", "Edge", "IIoT.Edge.Shell"),
            Path.Combine(root, "src", "Presentation"),
            Path.Combine(root, "src", "Modules")
        };

        var matches = xamlRoots
            .Where(Directory.Exists)
            .SelectMany(path => EnumerateFiles(path, "*.axaml"))
            .Where(path => !ToRepositoryPath(root, path).Contains("/Resources/Languages/", StringComparison.OrdinalIgnoreCase))
            .Where(ContainsChineseText)
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void BusinessXaml_ShouldUseSharedVisibleControlsInsteadOfNativeControls()
    {
        var root = FindRepositoryRoot();
        var xamlRoots = GetBusinessXamlRoots(root);

        var matches = xamlRoots
            .Where(Directory.Exists)
            .SelectMany(path => EnumerateFiles(path, "*.axaml"))
            .Where(path => !ToRepositoryPath(root, path).Contains("/Resources/Languages/", StringComparison.OrdinalIgnoreCase))
            .SelectMany(path => BusinessXamlNativeVisibleControlPattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} uses native visible control {match.Value} at offset {match.Index}"))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void ReworkedBusinessXaml_ShouldNotUsePageLevelVisualProperties()
    {
        var root = FindRepositoryRoot();
        var reworkedFiles = new[]
        {
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Dashboard/Views/DashboardView.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/Views/DashboardPreviewView.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Configuration/Views/ConfigurationWorkspaceView.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Shell/Views/PlaceholderPageView.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/HardwareConfigView/Views/HardwareConfigPage.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Hardware/IOView/Views/IOViewPage.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Formula/RecipeView/Views/RecipeViewPage.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/System/DiagnosticsView/Views/DiagnosticsPage.axaml",
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/Production/Monitor/Views/MonitorView.axaml"
        };

        var matches = reworkedFiles
            .Select(repositoryPath => ToFullPath(root, repositoryPath))
            .SelectMany(path => PageLevelVisualPropertyPattern
                .Matches(File.ReadAllText(path))
                .Select(match => $"{ToRepositoryPath(root, path)} uses page-level visual property {match.Value} at offset {match.Index}"))
            .ToArray();

        Assert.Empty(matches);
    }

    [Fact]
    public void SharedUi_ShouldProvidePropertyDrivenSummaryAndTimelineControls()
    {
        var root = FindRepositoryRoot();
        var requiredFiles = new[]
        {
            "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Controls/Metrics/EdgeInfoSummaryCard.cs",
            "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Controls/Metrics/EdgeSummaryItemsControl.cs",
            "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Controls/Status/EdgeStatusTimeline.cs",
            "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Controls/Status/EdgeStatusSegmentBar.cs"
        };

        Assert.All(requiredFiles, repositoryPath =>
            Assert.True(File.Exists(ToFullPath(root, repositoryPath)), $"{repositoryPath} should exist."));

        var metricsStyles = File.ReadAllText(ToFullPath(
            root,
            "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Styles/Controls/Metrics.axaml"));
        var statusStyles = File.ReadAllText(ToFullPath(
            root,
            "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Styles/Controls/Status.axaml"));

        Assert.Contains("controls|EdgeInfoSummaryCard", metricsStyles, StringComparison.Ordinal);
        Assert.Contains("controls|EdgeSummaryItemsControl", metricsStyles, StringComparison.Ordinal);
        Assert.Contains("controls|EdgeStatusTimeline", statusStyles, StringComparison.Ordinal);
        Assert.Contains("SegmentWidth", statusStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedUi_Scrollbars_ShouldUseUnifiedHitAreaAndThumbVisibility()
    {
        var root = FindRepositoryRoot();
        var gridSource = File.ReadAllText(ToFullPath(
            root,
            "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Controls/Data/EdgeDataGrid.cs"));
        var dataStyles = File.ReadAllText(ToFullPath(
                root,
                "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Styles/Controls/Data.axaml"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("nameof(HorizontalScrollBarReserveHeight),\n            10d", gridSource.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("""
                            Name="PART_VerticalScrollbar"
                            Classes="edge-data-grid-scrollbar"
                            Grid.Row="1"
                            Grid.Column="2"
                            Margin="2,2,0,2"
                            Width="10"
""", dataStyles, StringComparison.Ordinal);
        Assert.Contains("""
                                Name="PART_HorizontalScrollbar"
                                Classes="edge-data-grid-scrollbar"
                                Grid.Column="1"
                                Height="10"
                                Margin="0"
""", dataStyles, StringComparison.Ordinal);
        Assert.Contains("DataGrid.edge-data-grid ScrollBar.edge-data-grid-scrollbar", dataStyles, StringComparison.Ordinal);
        Assert.Contains("Padding=\"2\"", dataStyles, StringComparison.Ordinal);
        Assert.Contains("Opacity=\"0.28\"", dataStyles, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Opacity\" Value=\"0.58\" />", dataStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedUi_StatusSegment_ShouldUseExistingCornerRadiusToken()
    {
        var root = FindRepositoryRoot();
        var statusStyles = File.ReadAllText(ToFullPath(
            root,
            "src/Shared/IIoT.Edge.UI.Shared/Avalonia/Styles/Controls/Status.axaml"));

        Assert.DoesNotContain("Edge.CornerRadius.Badge", statusStyles, StringComparison.Ordinal);
        Assert.Contains("Edge.CornerRadius.Pill", statusStyles, StringComparison.Ordinal);
    }

    [Fact]
    public void ModuleDataPages_ShouldUseFillTableLayoutInsteadOfMinHeight()
    {
        var root = FindRepositoryRoot();
        var pagePaths = new[]
        {
            "src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.axaml"
        };

        foreach (var pagePath in pagePaths)
        {
            var xaml = File.ReadAllText(ToFullPath(root, pagePath));
            Assert.Contains("<edge:EdgeTablePanel", xaml, StringComparison.Ordinal);
            Assert.Contains("Classes=\"fill\"", xaml, StringComparison.Ordinal);
            Assert.Contains("ViewportMaxHeight=\"0\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("MinHeight=\"620\"", xaml, StringComparison.Ordinal);
            Assert.DoesNotContain("MinHeight=\"520\"", xaml, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DiagnosticsDeadLetterRequeue_ShouldUseRetrySemanticIcon()
    {
        var root = FindRepositoryRoot();
        var xaml = File.ReadAllText(ToFullPath(
            root,
            "src/Presentation/IIoT.Edge.Presentation.Navigation/Features/System/DiagnosticsView/Views/DiagnosticsPage.axaml"));

        Assert.Contains("RequeueDeadLetterCommand", xaml, StringComparison.Ordinal);
        Assert.Contains("Icon=\"{StaticResource Edge.Icon.Refresh}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Icon=\"{StaticResource Edge.Icon.Sync}\"", xaml, StringComparison.Ordinal);
    }

    private static IReadOnlyList<string> GetProjectReferences(string projectPath)
        => XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToArray();

    private static IReadOnlyList<string> GetProjectReferenceRepositoryPaths(string root, string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException($"无法定位项目目录：{projectPath}");

        return XDocument.Load(projectPath)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Replace('\\', Path.DirectorySeparatorChar))
            .Select(value => Path.GetFullPath(Path.Combine(projectDirectory, value)))
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();
    }

    private static IReadOnlySet<string> GetXamlResourceKeys(string path)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path)
            .Descendants()
            .Select(element => element.Attribute(x + "Key")?.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string? GetProjectProperty(XDocument project, string propertyName)
    {
        foreach (var propertyGroup in project.Root?.Elements("PropertyGroup") ?? [])
        {
            var value = propertyGroup.Element(propertyName)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }

    private static IEnumerable<string> FindForbiddenMatches(string root, string path, IReadOnlyList<string> forbiddenNames)
    {
        var text = File.ReadAllText(path);
        foreach (var forbiddenName in forbiddenNames)
        {
            if (text.Contains(forbiddenName, StringComparison.Ordinal))
            {
                yield return $"{ToRepositoryPath(root, path)} contains {forbiddenName}";
            }
        }
    }

    private static IEnumerable<string> FindForbiddenRegexMatches(string root, string path, Regex pattern, string description)
    {
        var text = File.ReadAllText(path);
        foreach (Match match in pattern.Matches(text))
        {
            yield return $"{ToRepositoryPath(root, path)} contains {description} {match.Value}";
        }
    }

    private static IEnumerable<string> EnumerateFiles(string root, string pattern)
        => Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            .Where(path => !ShouldSkip(path));

    private static bool IsFontFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".woff2", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTextCandidate(string path)
    {
        var fileName = Path.GetFileName(path);
        if (string.Equals(fileName, ".gitignore", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var extension = Path.GetExtension(path);
        return extension.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".props", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".xaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".axaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".md", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".config", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".editorconfig", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsChineseText(string path)
        => File.ReadAllText(path).Any(ch => ch >= '\u4e00' && ch <= '\u9fff');

    private static string[] GetBusinessXamlRoots(string root)
        =>
        [
            Path.Combine(root, "src", "Edge"),
            Path.Combine(root, "src", "Presentation"),
            Path.Combine(root, "src", "Modules")
        ];

    private static bool ShouldSkip(string path)
    {
        var segments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("obj", StringComparison.OrdinalIgnoreCase)
            || segment.Equals("publish", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".git", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".vs", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".dotnet", StringComparison.OrdinalIgnoreCase)
            || segment.Equals(".artifacts", StringComparison.OrdinalIgnoreCase));
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IIoT.EdgeClient.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("无法定位 IIoT.EdgeClient 仓库根目录。");
    }

    private static string ToFullPath(string root, string repositoryPath)
        => Path.Combine(root, repositoryPath.Replace('/', Path.DirectorySeparatorChar));

    private static string ToRepositoryPath(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');
}
