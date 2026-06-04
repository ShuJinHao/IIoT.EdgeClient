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
                "IIoT.Edge.Infrastructure",
                "IIoT.Edge.Runtime",
                "IIoT.Edge.Presentation",
                "IIoT.Edge.UI.Shared"
            ],
            ["src/Application/IIoT.Edge.Application"] =
            [
                "IIoT.Edge.Infrastructure",
                "IIoT.Edge.Runtime",
                "IIoT.Edge.Presentation",
                "IIoT.Edge.UI.Shared"
            ],
            ["src/Infrastructure/IIoT.Edge.Infrastructure.DeviceComm"] =
            [
                "IIoT.Edge.Runtime"
            ],
            ["src/Shared/IIoT.Edge.SharedKernel"] =
            [
                "IIoT.Edge.Application",
                "IIoT.Edge.Infrastructure",
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

        Assert.Contains("../homogenization/IIoT.Edge.Shell", profileCatalog, StringComparison.Ordinal);
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

        Assert.Contains("\"IIoT.Edge.Application.dll\"", fileSystem, StringComparison.Ordinal);
        Assert.Contains("\"IIoT.Edge.Runtime.dll\"", fileSystem, StringComparison.Ordinal);
        Assert.Contains("\"Modules\"", fileSystem, StringComparison.Ordinal);
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
        if (string.Equals(fileName, "CODEOWNERS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(fileName, ".gitignore", StringComparison.OrdinalIgnoreCase))
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
