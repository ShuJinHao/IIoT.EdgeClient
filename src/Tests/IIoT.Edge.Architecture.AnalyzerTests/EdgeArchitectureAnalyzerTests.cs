using System.Collections.Immutable;
using IIoT.Edge.Architecture.Analyzers;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Xunit;

namespace IIoT.Edge.Architecture.AnalyzerTests;

public sealed class EdgeArchitectureAnalyzerTests
{
    [Theory]
    [InlineData("IIoT.Edge.Domain.Hardware.Aggregates", "NetworkDeviceEntity")]
    [InlineData("IIoT.Edge.Domain.Hardware.Aggregates", "IoMappingEntity")]
    [InlineData("IIoT.Edge.Domain.Hardware.Aggregates", "PlcTaskBindingEntity")]
    [InlineData("IIoT.Edge.Domain.Hardware.Aggregates", "SerialDeviceEntity")]
    [InlineData("IIoT.Edge.Domain.Config.Aggregates", "SystemConfigEntity")]
    public async Task ApprovedFiveRepositoryRoots_AreAllowed(string entityNamespace, string entityName)
    {
        var source = RepositoryPrelude(entityNamespace, entityName) + $$"""
            public sealed class Consumer
            {
                private readonly IIoT.Edge.SharedKernel.Repository.IRepository<{{entityNamespace}}.{{entityName}}> repository;
                public Consumer(IIoT.Edge.SharedKernel.Repository.IRepository<{{entityNamespace}}.{{entityName}}> repository)
                    => this.repository = repository;
                public System.Threading.Tasks.Task<int> Save(System.Threading.CancellationToken token)
                    => repository.SaveChangesAsync(token);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "DDD004");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "DATA002" or "DDD007");
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("global-using")]
    [InlineData("fully-qualified")]
    [InlineData("nested-generic")]
    [InlineData("operation-result")]
    public async Task DeviceParamRepository_AllSyntaxForms_ReportDdd004(string form)
    {
        const string entityNamespace = "IIoT.Edge.Domain.Config.Aggregates";
        const string entityName = "DeviceParamEntity";
        var usingPrefix = form switch
        {
            "alias" => "using Repo = IIoT.Edge.SharedKernel.Repository.IRepository<IIoT.Edge.Domain.Config.Aggregates.DeviceParamEntity>;",
            "global-using" => "global using IIoT.Edge.SharedKernel.Repository;",
            _ => string.Empty
        };
        var declaration = form switch
        {
            "alias" => "public sealed class Consumer { private Repo value = null!; }",
            "global-using" => "public sealed class Consumer { private IRepository<IIoT.Edge.Domain.Config.Aggregates.DeviceParamEntity> value = null!; }",
            "fully-qualified" => "public sealed class Consumer { private global::IIoT.Edge.SharedKernel.Repository.IRepository<global::IIoT.Edge.Domain.Config.Aggregates.DeviceParamEntity> value = null!; }",
            "nested-generic" => "public sealed class Box<T> { } public sealed class Consumer { private Box<IIoT.Edge.SharedKernel.Repository.IRepository<IIoT.Edge.Domain.Config.Aggregates.DeviceParamEntity>> value = null!; }",
            "operation-result" => "public static class Services { public static T GetRequiredService<T>() => default!; } public sealed class Consumer { public object Run() => Services.GetRequiredService<IIoT.Edge.SharedKernel.Repository.IRepository<IIoT.Edge.Domain.Config.Aggregates.DeviceParamEntity>>(); }",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
        var source = usingPrefix + RepositoryPrelude(entityNamespace, entityName) + declaration;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        AssertSingle(diagnostics, "DDD004");
    }

    [Fact]
    public async Task AggregateMarkerOutsideRegistry_DoesNotCreateRepositoryPermission()
    {
        var source = """
            namespace IIoT.Edge.SharedKernel.Domain { public interface IAggregateRoot { } }
            namespace IIoT.Edge.SharedKernel.Repository
            {
                public interface IRepository<T> where T : IIoT.Edge.SharedKernel.Domain.IAggregateRoot { }
            }
            public sealed class InventedRoot : IIoT.Edge.SharedKernel.Domain.IAggregateRoot { }
            public sealed class Consumer
            {
                private IIoT.Edge.SharedKernel.Repository.IRepository<InventedRoot> repository = null!;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        AssertSingle(diagnostics, "DDD004");
    }

    [Fact]
    public async Task GenericHelperConstructedRepositoryTypeArgument_ReportsDdd004()
    {
        var source = RepositoryPrelude(
            "IIoT.Edge.Domain.Config.Aggregates",
            "DeviceParamEntity") + """
            public static class Resolver
            {
                public static object Resolve<T>() => new object();
            }
            public sealed class Consumer
            {
                public object Run() => Resolver.Resolve<
                    IIoT.Edge.SharedKernel.Repository.IRepository<
                        IIoT.Edge.Domain.Config.Aggregates.DeviceParamEntity>>();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        AssertSingle(diagnostics, "DDD004");
    }

    [Fact]
    public async Task EfNavigationAndMediatRNotification_DoNotInferAggregateOwnership()
    {
        var source = """
            namespace MediatR { public interface INotification { } }
            namespace Microsoft.EntityFrameworkCore { public sealed class NavigationAttribute : System.Attribute { } }
            public sealed class Child : MediatR.INotification { }
            public sealed class Parent
            {
                public System.Collections.Generic.List<Child> Children { get; } = new();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Domain", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id.StartsWith("DDD", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplicationProviderField_ReportsDdd007_ButBusinessStoreNameDoesNot()
    {
        var source = """
            namespace Microsoft.Data.Sqlite { public sealed class SqliteConnection { } }
            public sealed class ProductionStore { public string Name => "business"; }
            public sealed class Consumer
            {
                private Microsoft.Data.Sqlite.SqliteConnection connection = null!;
                private ProductionStore store = new();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        AssertSingle(diagnostics, "DDD007");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.GetMessage().Contains("ProductionStore", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplicationRawProviderInvocation_ReportsData002()
    {
        var source = """
            namespace Microsoft.Data.Sqlite
            {
                public sealed class SqliteCommand { public int ExecuteNonQuery() => 0; }
            }
            public sealed class Consumer
            {
                public int Run(Microsoft.Data.Sqlite.SqliteCommand command) => command.ExecuteNonQuery();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "DATA002");
    }

    [Fact]
    public async Task PresentationProviderUse_ReportsData001()
    {
        var source = """
            namespace Microsoft.Data.Sqlite { public sealed class SqliteConnection { } }
            public sealed class ViewModel
            {
                private Microsoft.Data.Sqlite.SqliteConnection connection = null!;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Presentation.Navigation", [source]);

        AssertSingle(diagnostics, "DATA001");
    }

    [Fact]
    public async Task DbContextSaveChangesOutsideOwner_ReportsData005()
    {
        var source = """
            namespace Microsoft.EntityFrameworkCore
            {
                public class DbContext { public int SaveChanges() => 0; }
            }
            public sealed class Store
            {
                public int Save(Microsoft.EntityFrameworkCore.DbContext db) => db.SaveChanges();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Infrastructure.Integration", [source]);

        AssertSingle(diagnostics, "DATA005");
    }

    [Fact]
    public async Task RepositoryPortSaveChanges_IsNotProviderCommit()
    {
        var source = RepositoryPrelude(
            "IIoT.Edge.Domain.Config.Aggregates",
            "SystemConfigEntity") + """
            public sealed class Handler
            {
                private readonly IIoT.Edge.SharedKernel.Repository.IRepository<IIoT.Edge.Domain.Config.Aggregates.SystemConfigEntity> repository;
                public Handler(IIoT.Edge.SharedKernel.Repository.IRepository<IIoT.Edge.Domain.Config.Aggregates.SystemConfigEntity> repository)
                    => this.repository = repository;
                public System.Threading.Tasks.Task<int> Save(System.Threading.CancellationToken token)
                    => repository.SaveChangesAsync(token);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "DATA005" or "DATA002" or "DDD007");
    }

    [Theory]
    [InlineData("Dapper.SqlMapper.ExecuteAsync(db, \"update x\")")]
    [InlineData("global::Dapper.SqlMapper.ExecuteAsync(db, \"update x\")")]
    public async Task DapperWriteSyntaxForms_OutsideOwner_ReportData006(string expression)
    {
        var source = $$"""
            namespace Dapper
            {
                public static class SqlMapper
                {
                    public static System.Threading.Tasks.Task<int> ExecuteAsync(object db, string sql)
                        => System.Threading.Tasks.Task.FromResult(0);
                }
            }
            public sealed class Store
            {
                public System.Threading.Tasks.Task<int> Run(object db) => {{expression}};
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Infrastructure.Integration", [source]);

        AssertSingle(diagnostics, "DATA006");
    }

    [Fact]
    public async Task NonDapperExecuteAsyncAndLocalCollectionAdd_AreAllowed()
    {
        var source = """
            public sealed class BusinessExecutor
            {
                public System.Threading.Tasks.Task ExecuteAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            public sealed class Handler
            {
                public async System.Threading.Tasks.Task Run(BusinessExecutor executor)
                {
                    var values = new System.Collections.Generic.List<int>();
                    values.Add(1);
                    await executor.ExecuteAsync();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "DATA003" or "DATA006" or "DATA002" or "DDD007");
    }

    [Fact]
    public async Task EfAndDapperRegisteredOwners_AreAllowed()
    {
        var efSource = """
            namespace Microsoft.EntityFrameworkCore { public class DbContext { public int SaveChanges() => 0; } }
            public sealed class EfStore { public int Save(Microsoft.EntityFrameworkCore.DbContext db) => db.SaveChanges(); }
            """;
        var dapperSource = """
            namespace Dapper { public static class SqlMapper { public static int Execute(object db, string sql) => 0; } }
            public sealed class DapperStore { public int Save(object db) => Dapper.SqlMapper.Execute(db, "sql"); }
            """;

        var efDiagnostics = await AnalyzeAsync("IIoT.Edge.Infrastructure.Persistence.EfCore", [efSource]);
        var dapperDiagnostics = await AnalyzeAsync("IIoT.Edge.Infrastructure.Persistence.Dapper", [dapperSource]);

        Assert.DoesNotContain(efDiagnostics, diagnostic => diagnostic.Id.StartsWith("DATA", StringComparison.Ordinal));
        Assert.DoesNotContain(dapperDiagnostics, diagnostic => diagnostic.Id.StartsWith("DATA", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("global-using")]
    [InlineData("fully-qualified")]
    [InlineData("property")]
    public async Task ApplicationUseOfInfrastructureSymbol_ReportsWsarch004(string form)
    {
        var infrastructure = CreateReference(
            "IIoT.Edge.Infrastructure.Integration",
            "namespace IIoT.Edge.Infrastructure.Integration { public sealed class CloudClient { public void Send() { } } }");
        var source = form switch
        {
            "alias" => "using Client = IIoT.Edge.Infrastructure.Integration.CloudClient; public sealed class Handler { private Client client = new(); }",
            "global-using" => "global using IIoT.Edge.Infrastructure.Integration; public sealed class Handler { private CloudClient client = new(); }",
            "fully-qualified" => "public sealed class Handler { private global::IIoT.Edge.Infrastructure.Integration.CloudClient client = new(); }",
            "property" => "public sealed class Handler { public IIoT.Edge.Infrastructure.Integration.CloudClient Client { get; } = new(); }",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source], infrastructure);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "WSARCH004");
    }

    [Theory]
    [InlineData("nested-generic")]
    [InlineData("tuple")]
    public async Task NestedRoleTypeReferences_ReportWsarch004(string form)
    {
        var infrastructure = CreateReference(
            "IIoT.Edge.Infrastructure.Integration",
            "namespace IIoT.Edge.Infrastructure.Integration { public sealed class CloudClient { } }");
        var source = form == "nested-generic"
            ? "public sealed class Handler { private System.Collections.Generic.List<IIoT.Edge.Infrastructure.Integration.CloudClient> clients = new(); }"
            : "public sealed class Handler { private (int Count, IIoT.Edge.Infrastructure.Integration.CloudClient Client) state; }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source], infrastructure);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "WSARCH004");
    }

    [Fact]
    public async Task DomainUseOfApplicationSymbol_ReportsDdd001()
    {
        var application = CreateReference(
            "IIoT.Edge.Application",
            "namespace IIoT.Edge.Application { public interface IUpperPort { } }");

        var diagnostics = await AnalyzeAsync(
            "IIoT.Edge.Domain",
            ["public sealed class Aggregate { private IIoT.Edge.Application.IUpperPort port = null!; }"],
            application);

        AssertSingle(diagnostics, "DDD001");
    }

    [Fact]
    public async Task ProductionReferenceToTestingAssembly_ReportsWsarch003()
    {
        var testing = CreateReference("IIoT.Edge.Testing.Core", "public sealed class FakeFactory { }");

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", ["public sealed class Handler { }"], testing);

        AssertSingle(diagnostics, "WSARCH003");
    }

    [Fact]
    public async Task TestAssemblyMayReferenceTestKit()
    {
        var testKit = CreateReference("IIoT.Edge.TestKit", "public sealed class FakeFactory { }");

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application.Tests", ["public sealed class HandlerTests { }"], testKit);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "WSARCH003");
    }

    [Fact]
    public async Task PluginForbiddenHostSymbol_ReportsPlug001()
    {
        var host = CreateReference(
            "IIoT.Edge.Host.DataPipeline",
            "namespace IIoT.Edge.Host.DataPipeline { public sealed class RuntimeQueue { } }");
        var source = PluginMetadata +
            "public sealed class Plugin { private IIoT.Edge.Host.DataPipeline.RuntimeQueue queue = new(); }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source], host);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "PLUG001");
    }

    [Fact]
    public async Task PluginToAnotherPlugin_ReportsPlug002()
    {
        var otherPlugin = CreateReference(
            "IIoT.Edge.Module.OtherProcess",
            "namespace IIoT.Edge.Module.OtherProcess { public sealed class OtherRuntime { } }");
        var source = PluginMetadata +
            "public sealed class Plugin { private IIoT.Edge.Module.OtherProcess.OtherRuntime runtime = new(); }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source], otherPlugin);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "PLUG002");
    }

    [Fact]
    public async Task HostUseOfConcretePluginSymbol_ReportsPlug003()
    {
        var plugin = CreateReference(
            "IIoT.Edge.Module.Sample",
            "namespace IIoT.Edge.Module.Sample { public sealed class SampleRuntime { } }");

        var diagnostics = await AnalyzeAsync(
            "IIoT.Edge.Host.Bootstrap",
            ["public sealed class Host { private IIoT.Edge.Module.Sample.SampleRuntime runtime = new(); }"],
            plugin);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "PLUG003");
    }

    [Fact]
    public async Task PluginApprovedContractsAndNavigationSeam_AreAllowed()
    {
        var application = CreateReference("IIoT.Edge.Application", "namespace IIoT.Edge.Application { public interface IPort { } }");
        var sdk = CreateReference("IIoT.Edge.Module.Sdk", "namespace IIoT.Edge.Module.Sdk { public interface IModulePort { } }");
        var shared = CreateReference("IIoT.Edge.SharedKernel", "namespace IIoT.Edge.SharedKernel { public interface IValue { } }");
        var ui = CreateReference("IIoT.Edge.UI.Shared", "namespace IIoT.Edge.UI.Shared { public interface IView { } }");
        var navigation = CreateReference("IIoT.Edge.Presentation.Navigation", "namespace IIoT.Edge.Presentation.Navigation { public interface IRegistration { } }");
        var source = PluginMetadata + """
            public sealed class Plugin
            {
                private IIoT.Edge.Application.IPort app = null!;
                private IIoT.Edge.Module.Sdk.IModulePort sdk = null!;
                private IIoT.Edge.SharedKernel.IValue shared = null!;
                private IIoT.Edge.UI.Shared.IView ui = null!;
                private IIoT.Edge.Presentation.Navigation.IRegistration navigation = null!;
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.Edge.Module.Fixture",
            [source],
            application,
            sdk,
            shared,
            ui,
            navigation);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "PLUG001" or "PLUG002" or "PLUG004");
    }

    [Fact]
    public async Task PluginWithoutRoleMetadata_ReportsPlug004()
    {
        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", ["public sealed class Plugin { }"]);

        AssertSingle(diagnostics, "PLUG004");
    }

    [Fact]
    public async Task ModuleSdkWithExactRoleMetadata_PassesPlug004()
    {
        var source = """
            [assembly: System.Reflection.AssemblyMetadata("EdgeModuleRole", "Sdk")]
            [assembly: System.Reflection.AssemblyMetadata("IsEdgePluginModule", "false")]
            [assembly: System.Reflection.AssemblyMetadata("IsPackable", "false")]
            public interface IModuleContract { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Sdk", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "PLUG004");
    }

    [Fact]
    public async Task ProductionTaskDirectUploader_ReportsEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.Mes
            {
                public interface IProcessMesUploader
                {
                    System.Threading.Tasks.Task UploadAsync();
                }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.Mes.IProcessMesUploader uploader;
                public Task(IIoT.Edge.Application.Abstractions.Mes.IProcessMesUploader uploader) => this.uploader = uploader;
                public System.Threading.Tasks.Task RunAsync() => uploader.UploadAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskPropertyGetterUploader_ReportsEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader;
                public Task(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader) => this.uploader = uploader;
                private System.Threading.Tasks.Task Trigger => uploader.UploadAsync();
                public System.Threading.Tasks.Task RunAsync() => Trigger;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskPropertyGetterUnprotectedEnqueue_ReportsEdgeOut002()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService { System.Threading.Tasks.Task EnqueueAsync(object record); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                private System.Threading.Tasks.Task Trigger => pipeline.EnqueueAsync(new object());
                public System.Threading.Tasks.Task RunAsync() => Trigger;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskSafePropertyGetter_DoesNotReportEdgeOutDiagnostics()
    {
        var source = PluginMetadata + TaskPrelude + """
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private System.Threading.Tasks.Task Trigger => System.Threading.Tasks.Task.CompletedTask;
                public System.Threading.Tasks.Task RunAsync() => Trigger;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "EDGEOUT001" or "EDGEOUT002");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionTaskDelegateUploader_ReportsEdgeOut001(bool useLambda)
    {
        var delegateValue = useLambda ? "() => uploader.UploadAsync()" : "uploader.UploadAsync";
        var source = PluginMetadata + TaskPrelude + $$"""
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader;
                public Task(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader) => this.uploader = uploader;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    System.Func<System.Threading.Tasks.Task> send = {{delegateValue}};
                    await send();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionTaskDelegateEnqueue_ReportsEdgeOut002(bool useLambda)
    {
        var delegateValue = useLambda ? "record => pipeline.EnqueueAsync(record)" : "pipeline.EnqueueAsync";
        var source = PluginMetadata + TaskPrelude + $$"""
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService { System.Threading.Tasks.Task EnqueueAsync(object record); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    System.Func<object, System.Threading.Tasks.Task> send = {{delegateValue}};
                    await send(new object());
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskOrdinaryDelegate_DoesNotReportEdgeOutDiagnostics()
    {
        var source = PluginMetadata + TaskPrelude + """
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    System.Func<System.Threading.Tasks.Task> work = () => System.Threading.Tasks.Task.CompletedTask;
                    await work();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "EDGEOUT001" or "EDGEOUT002");
    }

    [Theory]
    [InlineData("System.Func<System.Threading.Tasks.Task>", "_ = work();")]
    [InlineData("System.Action", "work();")]
    [InlineData("System.Func<int>", "_ = work();")]
    public async Task ProductionTaskUnresolvedDelegate_FailsClosedWithEdgeOut001(
        string delegateType,
        string invocation)
    {
        var source = PluginMetadata + TaskPrelude + $$"""
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly {{delegateType}} work;
                public Task({{delegateType}} work) => this.work = work;
                public System.Threading.Tasks.Task RunAsync()
                {
                    {{invocation}}
                    return System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskSafeDelegateReassignedFromUnknown_FailsClosedWithEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly System.Func<System.Threading.Tasks.Task> injected;
                public Task(System.Func<System.Threading.Tasks.Task> injected) => this.injected = injected;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    System.Func<System.Threading.Tasks.Task> work = () => System.Threading.Tasks.Task.CompletedTask;
                    work = injected;
                    await work();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskSafeDelegateCombinedWithUploader_ReportsEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader;
                public Task(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader) => this.uploader = uploader;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    System.Func<System.Threading.Tasks.Task> work = () => System.Threading.Tasks.Task.CompletedTask;
                    work += uploader.UploadAsync;
                    await work();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskSafeDelegateCombinedWithEnqueue_ReportsEdgeOut002()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService { System.Threading.Tasks.Task EnqueueAsync(object record); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    System.Func<object, System.Threading.Tasks.Task> work = _ => System.Threading.Tasks.Task.CompletedTask;
                    work += pipeline.EnqueueAsync;
                    await work(new object());
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskSafeDelegateReassignment_DoesNotReportEdgeOutDiagnostics()
    {
        var source = PluginMetadata + TaskPrelude + """
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public async System.Threading.Tasks.Task RunAsync()
                {
                    System.Func<System.Threading.Tasks.Task> work = () => System.Threading.Tasks.Task.CompletedTask;
                    work = static () => System.Threading.Tasks.Task.CompletedTask;
                    await work();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "EDGEOUT001" or "EDGEOUT002");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionTaskConditionalDelegateWithUnknownBranch_FailsClosed(bool enqueue)
    {
        var contract = enqueue
            ? "namespace IIoT.Edge.Application.Abstractions.DataPipeline { public interface IDataPipelineService { System.Threading.Tasks.Task EnqueueAsync(object value); } }"
            : string.Empty;
        var known = enqueue
            ? "() => pipeline.EnqueueAsync(new object())"
            : "static () => System.Threading.Tasks.Task.CompletedTask";
        var field = enqueue
            ? "private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;"
            : string.Empty;
        var constructor = enqueue
            ? "public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline, System.Func<System.Threading.Tasks.Task> injected) { this.pipeline = pipeline; this.injected = injected; }"
            : "public Task(System.Func<System.Threading.Tasks.Task> injected) => this.injected = injected;";
        var source = PluginMetadata + TaskPrelude + $$"""
            {{contract}}
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                {{field}}
                private readonly System.Func<System.Threading.Tasks.Task> injected;
                {{constructor}}
                public async System.Threading.Tasks.Task RunAsync(bool flag)
                {
                    System.Func<System.Threading.Tasks.Task> work = flag ? {{known}} : injected;
                    await work();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskConditionalDelegateWithKnownSafeBranches_Passes()
    {
        var source = PluginMetadata + TaskPrelude + """
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public async System.Threading.Tasks.Task RunAsync(bool flag)
                {
                    System.Func<System.Threading.Tasks.Task> work = flag
                        ? static () => System.Threading.Tasks.Task.CompletedTask
                        : static async () => { await System.Threading.Tasks.Task.Yield(); };
                    await work();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "EDGEOUT001" or "EDGEOUT002");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionTaskIndirectConstructorOrSetterUploader_ReportsEdgeOut001(bool useSetter)
    {
        var action = useSetter ? "helper.Trigger = true;" : "_ = new Helper(uploader);";
        var helperBody = useSetter
            ? "public bool Trigger { set { _ = uploader.UploadAsync(); } }"
            : "public Helper(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader) { _ = uploader.UploadAsync(); }";
        var helperConstructor = useSetter
            ? "public Helper(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader) => this.uploader = uploader;"
            : string.Empty;
        var source = PluginMetadata + TaskPrelude + $$"""
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Helper
            {
                private readonly IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader;
                {{helperConstructor}}
                {{helperBody}}
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader;
                private readonly Helper helper;
                public Task(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader)
                {
                    this.uploader = uploader;
                    helper = new Helper(uploader);
                }
                public System.Threading.Tasks.Task RunAsync()
                {
                    {{action}}
                    return System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskFieldInitializerOutbound_ReportsEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly System.Threading.Tasks.Task pending =
                    new System.Net.Http.HttpClient().GetAsync("https://127.0.0.1/");
                public System.Threading.Tasks.Task RunAsync() => pending;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskConstructorUploader_ReportsEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public Task(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader) { _ = uploader.UploadAsync(); }
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskConstructorUnprotectedEnqueue_ReportsEdgeOut002()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService { System.Threading.Tasks.Task EnqueueAsync(object record); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) { _ = pipeline.EnqueueAsync(new object()); }
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskUnreferencedPropertyAccessorUploader_ReportsEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader;
                public Task(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader) => this.uploader = uploader;
                private System.Threading.Tasks.Task Hidden => uploader.UploadAsync();
                public System.Threading.Tasks.Task RunAsync() => System.Threading.Tasks.Task.CompletedTask;
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskInterfaceHelperAcrossFiles_ReportsEdgeOut001()
    {
        var task = PluginMetadata + TaskPrelude + """
            public interface IHelper { System.Threading.Tasks.Task SendAsync(); }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IHelper helper;
                public Task(IHelper helper) => this.helper = helper;
                public System.Threading.Tasks.Task RunAsync() => helper.SendAsync();
            }
            """;
        var helper = """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Helper : IHelper
            {
                private readonly IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader;
                public Helper(IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader uploader) => this.uploader = uploader;
                public System.Threading.Tasks.Task SendAsync() => uploader.UploadAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [task, helper]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskGenericHelperAndPropertyIndirection_ReportEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Holder<T> { public Holder(T value) => Value = value; public T Value { get; } }
            public static class GenericHelper
            {
                public static System.Threading.Tasks.Task Send<T>(Holder<T> holder)
                    where T : IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader
                    => holder.Value.UploadAsync();
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly Holder<IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader> holder;
                public Task(Holder<IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader> holder) => this.holder = holder;
                public System.Threading.Tasks.Task RunAsync() => GenericHelper.Send(holder);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskOverrideDispatch_ReportsEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.Mes
            {
                public interface IProcessMesUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public abstract class BaseTask : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public System.Threading.Tasks.Task RunAsync() => SendCoreAsync();
                protected abstract System.Threading.Tasks.Task SendCoreAsync();
            }
            public sealed class Task : BaseTask
            {
                private readonly IIoT.Edge.Application.Abstractions.Mes.IProcessMesUploader uploader;
                public Task(IIoT.Edge.Application.Abstractions.Mes.IProcessMesUploader uploader) => this.uploader = uploader;
                protected override System.Threading.Tasks.Task SendCoreAsync() => uploader.UploadAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ForeignSimpleNameUploader_DoesNotReportEdgeOut001()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace Fixture.Transport
            {
                public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly Fixture.Transport.IProcessCloudUploader uploader;
                public Task(Fixture.Transport.IProcessCloudUploader uploader) => this.uploader = uploader;
                public System.Threading.Tasks.Task RunAsync() => uploader.UploadAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HttpMessageInvoker_IsOutboundOnlyWhenReachableFromProductionTask(bool productionTask)
    {
        var declaration = productionTask
            ? "public sealed class Worker : IIoT.Edge.Application.Abstractions.Plc.IPlcTask"
            : "public sealed class Worker";
        var source = PluginMetadata + TaskPrelude + $$"""
            {{declaration}}
            {
                private readonly System.Net.Http.HttpMessageInvoker invoker;
                public Worker(System.Net.Http.HttpMessageHandler handler)
                    => invoker = new System.Net.Http.HttpMessageInvoker(handler);
                public System.Threading.Tasks.Task RunAsync()
                    => invoker.SendAsync(
                        new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, "https://127.0.0.1/"),
                        System.Threading.CancellationToken.None);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        if (productionTask)
            Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
        else
            Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Fact]
    public async Task ProductionTaskDataPipelineEnqueue_IsAllowed()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    System.Threading.Tasks.Task EnqueueAsync(object record);
                }
            }

            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    try
                    {
                        await pipeline.EnqueueAsync(new object());
                    }
                    catch (System.Exception)
                    {
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT002");
    }

    [Theory]
    [InlineData("Socket")]
    [InlineData("UdpClient")]
    [InlineData("TcpListener")]
    public async Task ProductionTaskRawSocketTransport_ReportsEdgeOut001(string transportType)
    {
        var source = PluginMetadata + TaskPrelude + $$"""
            namespace System.Net.Sockets
            {
                public sealed class {{transportType}}
                {
                    public void Send() { }
                }
            }
            public sealed class ProductionTask : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public void Execute(System.Net.Sockets.{{transportType}} transport) => transport.Send();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("delegate")]
    public async Task ProductionTaskExternalHelperBoundary_FailsClosedForOutboundAndGuard(
        string form)
    {
        var application = CreateReference(
            "IIoT.Edge.Application",
            """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface ICloudHttpClient
                {
                    System.Threading.Tasks.Task SendAsync();
                }
            }
            namespace IIoT.Edge.Application.External
            {
                public interface IPublishHelper
                {
                    System.Threading.Tasks.Task PublishAsync();
                }
                public sealed class PublishHelper(
                    IIoT.Edge.Application.Abstractions.Cloud.ICloudHttpClient cloud) : IPublishHelper
                {
                    public System.Threading.Tasks.Task PublishAsync() => cloud.SendAsync();
                }
            }
            """);
        var call = form == "direct"
            ? "await helper.PublishAsync();"
            : "System.Func<System.Threading.Tasks.Task> publish = helper.PublishAsync; await publish();";
        var source = PluginMetadata + TaskPrelude + $$"""
            public sealed class ProductionTask : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.External.IPublishHelper helper;
                public ProductionTask(IIoT.Edge.Application.External.IPublishHelper helper)
                    => this.helper = helper;
                public async System.Threading.Tasks.Task ExecuteAsync()
                {
                    {{call}}
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source], application);

        AssertSingle(diagnostics, "EDGEOUT001");
        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Theory]
    [InlineData("IIoT.Edge.Application", "void", "helper.Invoke();")]
    [InlineData("IIoT.Edge.Application", "int", "_ = helper.Invoke();")]
    [InlineData("IIoT.Edge.Application", "ExternalBoundary.CustomAwaitable", "_ = helper.Invoke();")]
    [InlineData("IIoT.Edge.Module.Sdk", "void", "helper.Invoke();")]
    [InlineData("IIoT.Edge.Module.Sdk", "int", "_ = helper.Invoke();")]
    [InlineData("IIoT.Edge.Module.Sdk", "ExternalBoundary.CustomAwaitable", "_ = helper.Invoke();")]
    [InlineData("IIoT.Edge.SharedKernel", "void", "helper.Invoke();")]
    [InlineData("IIoT.Edge.SharedKernel", "int", "_ = helper.Invoke();")]
    [InlineData("IIoT.Edge.SharedKernel", "ExternalBoundary.CustomAwaitable", "_ = helper.Invoke();")]
    public async Task ProductionTaskExternalHelperMetadataReference_AllReturnShapesFailClosed(
        string helperAssemblyName,
        string returnType,
        string invocation)
    {
        var helperReference = CreateReference(
            helperAssemblyName,
            $$"""
            namespace ExternalBoundary
            {
                public readonly struct CustomAwaitable { }
                public interface IExternalHelper
                {
                    {{returnType}} Invoke();
                }
            }
            """);
        var source = PluginMetadata + TaskPrelude + $$"""
            public sealed class ProductionTask : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly ExternalBoundary.IExternalHelper helper;
                public ProductionTask(ExternalBoundary.IExternalHelper helper) => this.helper = helper;

                public System.Threading.Tasks.Task ExecuteAsync()
                {
                    {{invocation}}
                    return System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync(
            "IIoT.Edge.Module.Fixture",
            [source],
            helperReference);

        AssertSingle(diagnostics, "EDGEOUT001");
        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskApprovedExternalPipelineConfigDiagnosticsCalls_AreAllowed()
    {
        var application = CreateReference(
            "IIoT.Edge.Application",
            """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    System.Threading.Tasks.Task EnqueueAsync(object record);
                }
            }
            namespace IIoT.Edge.Application.Abstractions.Logging
            {
                public interface ILogService { void Info(string message); }
            }
            namespace IIoT.Edge.Application.Abstractions.Config
            {
                public interface IRuntimeConfig { string Read(); }
            }
            namespace IIoT.Edge.Application.Abstractions.Diagnostics
            {
                public interface IDiagnosticsRecorder { void Record(string value); }
            }
            """);
        var source = PluginMetadata + TaskPrelude + """
            public sealed class ProductionTask : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public async System.Threading.Tasks.Task ExecuteAsync(
                    IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline,
                    IIoT.Edge.Application.Abstractions.Logging.ILogService logger,
                    IIoT.Edge.Application.Abstractions.Config.IRuntimeConfig config,
                    IIoT.Edge.Application.Abstractions.Diagnostics.IDiagnosticsRecorder diagnostics)
                {
                    var value = config.Read();
                    logger.Info(value);
                    diagnostics.Record(value);
                    try
                    {
                        await pipeline.EnqueueAsync(new object());
                    }
                    catch (System.Exception)
                    {
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source], application);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id is "EDGEOUT001" or "EDGEOUT002");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProductionTaskUnawaitedEnqueueInsideTry_ReportsEdgeOut002(bool useValueTask)
    {
        var returnType = useValueTask
            ? "System.Threading.Tasks.ValueTask"
            : "System.Threading.Tasks.Task";
        var source = PluginMetadata + TaskPrelude + $$"""
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    {{returnType}} EnqueueAsync(object record);
                }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public System.Threading.Tasks.Task RunAsync()
                {
                    try { _ = pipeline.EnqueueAsync(new object()); }
                    catch (System.Exception) { }
                    return System.Threading.Tasks.Task.CompletedTask;
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskUnprotectedEnqueue_ReportsEdgeOut002()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    System.Threading.Tasks.Task EnqueueAsync(object record);
                }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public System.Threading.Tasks.Task RunAsync() => pipeline.EnqueueAsync(new object());
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskEnqueueCaughtOnlyAsCancellation_ReportsEdgeOut002()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    System.Threading.Tasks.Task EnqueueAsync(object record);
                }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    try { await pipeline.EnqueueAsync(new object()); }
                    catch (System.OperationCanceledException) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskHelperWrappedUnprotectedEnqueue_ReportsEdgeOut002()
    {
        var task = PluginMetadata + TaskPrelude + """
            public interface IEnqueueHelper { System.Threading.Tasks.Task SendAsync(); }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IEnqueueHelper helper;
                public Task(IEnqueueHelper helper) => this.helper = helper;
                public System.Threading.Tasks.Task RunAsync() => helper.SendAsync();
            }
            """;
        var helper = """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    System.Threading.Tasks.Task EnqueueAsync(object record);
                }
            }
            public sealed class EnqueueHelper : IEnqueueHelper
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public EnqueueHelper(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline)
                    => this.pipeline = pipeline;
                public System.Threading.Tasks.Task SendAsync() => pipeline.EnqueueAsync(new object());
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [task, helper]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskEnqueueCatchAndRethrow_ReportsEdgeOut002()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    System.Threading.Tasks.Task EnqueueAsync(object record);
                }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    try { await pipeline.EnqueueAsync(new object()); }
                    catch (System.Exception) { throw; }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskEnqueueCatchAndExceptionDispatchRethrow_ReportsEdgeOut002()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService { System.Threading.Tasks.Task EnqueueAsync(object record); }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    try { await pipeline.EnqueueAsync(new object()); }
                    catch (System.Exception ex)
                    {
                        System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex).Throw();
                    }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskEnqueueCatchWithFalseFilter_ReportsEdgeOut002()
    {
        var source = PluginMetadata + TaskPrelude + """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    System.Threading.Tasks.Task EnqueueAsync(object record);
                }
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public Task(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline) => this.pipeline = pipeline;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    try { await pipeline.EnqueueAsync(new object()); }
                    catch (System.Exception) when (false) { }
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "EDGEOUT002");
    }

    [Fact]
    public async Task ProductionTaskCaughtHelperCall_PassesEdgeOut002()
    {
        var task = PluginMetadata + TaskPrelude + """
            public interface IEnqueueHelper { System.Threading.Tasks.Task SendAsync(); }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IEnqueueHelper helper;
                public Task(IEnqueueHelper helper) => this.helper = helper;
                public async System.Threading.Tasks.Task RunAsync()
                {
                    try { await helper.SendAsync(); }
                    catch (System.Exception) { }
                }
            }
            """;
        var helper = """
            namespace IIoT.Edge.Application.Abstractions.DataPipeline
            {
                public interface IDataPipelineService
                {
                    System.Threading.Tasks.Task EnqueueAsync(object record);
                }
            }
            public sealed class EnqueueHelper : IEnqueueHelper
            {
                private readonly IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline;
                public EnqueueHelper(IIoT.Edge.Application.Abstractions.DataPipeline.IDataPipelineService pipeline)
                    => this.pipeline = pipeline;
                public System.Threading.Tasks.Task SendAsync() => pipeline.EnqueueAsync(new object());
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [task, helper]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT002");
    }

    [Fact]
    public async Task ExactRemovedCompatibilityTypeDeclarations_ReportEdgeComp001()
    {
        var source = """
            namespace IIoT.Edge.Application.Modules
            {
                public class ProcessCloudUploaderBase<TRecord, TPayload> { }
                public class ProcessMesUploaderBase<TRecord> { }
            }
            namespace IIoT.Edge.Application.Modules.Mes
            {
                public class MesUploadChannelBase<TRecord> { }
            }
            namespace IIoT.Edge.Application.Abstractions.Plc
            {
                public interface ISignalInteraction { }
            }
            namespace IIoT.Edge.Infrastructure.DeviceComm.Signals
            {
                public class SignalInteraction { }
            }
            namespace IIoT.Edge.Application.Abstractions.Plc.Signals
            {
                public interface ILogicalSignalAccessor { }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        Assert.Equal(6, diagnostics.Count(diagnostic => diagnostic.Id == "EDGECOMP001"));
    }

    [Fact]
    public async Task RemovedCompatibilityAliasAcrossSourceFiles_ReportsEdgeComp001()
    {
        var declaration = "namespace IIoT.Edge.Application.Modules { public class ProcessCloudUploaderBase<TRecord, TPayload> { } }";
        var use = "using Legacy = IIoT.Edge.Application.Modules.ProcessCloudUploaderBase<object, object>; public sealed class Consumer { private Legacy value = new(); }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [declaration, use]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGECOMP001");
    }

    [Fact]
    public async Task ForeignTypesWithRemovedSimpleNames_DoNotReportEdgeComp001()
    {
        var source = """
            namespace Fixture
            {
                public class ProcessCloudUploaderBase<TRecord, TPayload> { }
                public class ProcessMesUploaderBase<TRecord> { }
                public class MesUploadChannelBase<TRecord> { }
                public interface ISignalInteraction { }
                public class SignalInteraction { }
                public interface ILogicalSignalAccessor { }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGECOMP001");
    }

    [Fact]
    public async Task StronglyTypedLogicalSignalAccessor_DoesNotReportEdgeComp001()
    {
        var source = "namespace IIoT.Edge.Application.Abstractions.Plc.Signals { public interface ILogicalSignalAccessor<TSignalKey> { } } public enum Signals { Ready } public sealed class Consumer { private IIoT.Edge.Application.Abstractions.Plc.Signals.ILogicalSignalAccessor<Signals> value = null!; }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [PluginMetadata + source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGECOMP001");
    }

    [Fact]
    public async Task PluginClassNamedCloudUploaderWithoutUploaderContract_PassesPlug005()
    {
        var diagnostics = await AnalyzeAsync(
            "IIoT.Edge.Module.Fixture",
            [PluginMetadata + "public sealed class FixtureCloudUploader { }"]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "PLUG005");
    }

    [Fact]
    public async Task PluginUploaderContractWithoutStandardChannelBase_ReportsPlug005RegardlessOfClassName()
    {
        var source = PluginMetadata + """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { }
            }
            public sealed class Transport
                : IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "PLUG005");
    }

    [Fact]
    public async Task PluginCloudUploaderWithStandardChannelBase_PassesPlug005()
    {
        var source = PluginMetadata + """
            namespace IIoT.Edge.Application.Abstractions.Cloud
            {
                public interface IProcessCloudUploader { }
            }
            namespace IIoT.Edge.Application.Modules.Cloud
            {
                public abstract class CloudUploadChannelBase<TCellData, TPayload> { }
            }
            public sealed class FixtureCloudUploader
                : IIoT.Edge.Application.Modules.Cloud.CloudUploadChannelBase<object, object>,
                  IIoT.Edge.Application.Abstractions.Cloud.IProcessCloudUploader { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "PLUG005");
    }

    [Fact]
    public async Task PluginDirectHardwareProfileServiceRegistration_ReportsPlug005()
    {
        var source = PluginMetadata + """
            namespace IIoT.Edge.Application.Abstractions.Modules
            {
                public interface IModuleHardwareProfileProvider { }
            }
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddSingleton<T>(IServiceCollection services) => services;
                }
            }
            public sealed class Registration
            {
                public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                        .AddSingleton<IIoT.Edge.Application.Abstractions.Modules.IModuleHardwareProfileProvider>(services);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "PLUG005");
    }

    [Fact]
    public async Task ForeignSimpleNameHardwareProfileRegistration_PassesPlug005()
    {
        var source = PluginMetadata + """
            namespace Fixture.Contracts
            {
                public interface IModuleHardwareProfileProvider { }
            }
            namespace Microsoft.Extensions.DependencyInjection
            {
                public interface IServiceCollection { }
                public static class ServiceCollectionServiceExtensions
                {
                    public static IServiceCollection AddSingleton<T>(IServiceCollection services) => services;
                }
            }
            public sealed class Registration
            {
                public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    => Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions
                        .AddSingleton<Fixture.Contracts.IModuleHardwareProfileProvider>(services);
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "PLUG005");
    }

    [Theory]
    [InlineData("try-add")]
    [InlineData("descriptor-add")]
    [InlineData("replace")]
    [InlineData("insert")]
    public async Task PluginServiceCollectionMutationBypasses_ReportPlug005(string form)
    {
        var expression = form switch
        {
            "try-add" => "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddSingleton<IIoT.Edge.Application.Abstractions.Modules.IModuleHardwareProfileProvider>(services)",
            "descriptor-add" => "services.Add(Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IIoT.Edge.Application.Abstractions.Modules.IModuleHardwareProfileProvider>())",
            "replace" => "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.Replace(services, Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IIoT.Edge.Application.Abstractions.Modules.IModuleHardwareProfileProvider>())",
            "insert" => "services.Insert(0, Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<IIoT.Edge.Application.Abstractions.Modules.IModuleHardwareProfileProvider>())",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
        var source = PluginMetadata + $$"""
            namespace IIoT.Edge.Application.Abstractions.Modules
            {
                public interface IModuleHardwareProfileProvider { }
            }
            namespace Microsoft.Extensions.DependencyInjection
            {
                public sealed class ServiceDescriptor
                {
                    public static ServiceDescriptor Singleton<T>() => new();
                }
                public interface IServiceCollection
                {
                    void Add(ServiceDescriptor descriptor);
                    void Insert(int index, ServiceDescriptor descriptor);
                }
            }
            namespace Microsoft.Extensions.DependencyInjection.Extensions
            {
                public static class ServiceCollectionDescriptorExtensions
                {
                    public static Microsoft.Extensions.DependencyInjection.IServiceCollection TryAddSingleton<T>(
                        Microsoft.Extensions.DependencyInjection.IServiceCollection services) => services;
                    public static Microsoft.Extensions.DependencyInjection.IServiceCollection Replace(
                        Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                        Microsoft.Extensions.DependencyInjection.ServiceDescriptor descriptor) => services;
                }
            }
            public sealed class Registration
            {
                public void Configure(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                    => {{expression}};
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        AssertSingle(diagnostics, "PLUG005");
    }

    [Theory]
    [InlineData("\"/api/v1/edge/device\"")]
    [InlineData("\"/api/\" + \"v1/edge/device\"")]
    public async Task ProductionCloudRouteLiteral_ReportsEdgeCloudCfg001(string expression)
    {
        var diagnostics = await AnalyzeAsync(
            "IIoT.Edge.Infrastructure.Integration",
            [$"public static class Routes {{ public const string Device = {expression}; }}"]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGECLOUDCFG001");
    }

    [Theory]
    [InlineData("interpolated")]
    [InlineData("format")]
    public async Task ComposedProductionCloudRoute_ReportsEdgeCloudCfg001(string form)
    {
        var body = form == "interpolated"
            ? "public string Build(string id) => $\"/api/v1/device/{id}\";"
            : "public string Build() => string.Format(\"/api/{0}/{1}\", \"v1\", \"device\");";
        var diagnostics = await AnalyzeAsync(
            "IIoT.Edge.Infrastructure.Integration",
            [$"public sealed class Routes {{ {body} }}"]);

        AssertSingle(diagnostics, "EDGECLOUDCFG001");
    }

    [Fact]
    public async Task ConfiguredCloudRouteValue_DoesNotReportEdgeCloudCfg001()
    {
        var source = "public sealed class Routes { public string Device(string configuredPath) => configuredPath; }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Infrastructure.Integration", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGECLOUDCFG001");
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("fully-qualified")]
    [InlineData("generic-holder")]
    public async Task ConcretePlcTransportOutsideOwner_ReportsEdgePlcOwn001(string form)
    {
        var source = form switch
        {
            "alias" => "using Client = System.Net.Sockets.TcpClient; public sealed class Service { private Client client = new(); }",
            "fully-qualified" => "public sealed class Service { private global::System.Net.Sockets.TcpClient client = new(); }",
            "generic-holder" => "public sealed class Holder<T> { } public sealed class Service { private Holder<System.Net.Sockets.TcpClient> client = new(); }",
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEPLCOWN001");
    }

    [Fact]
    public async Task DeviceCommWrongNamespace_DoesNotGainTransportOwnership()
    {
        var source = "namespace IIoT.Edge.Infrastructure.DeviceComm.Other { public sealed class Service { private System.Net.Sockets.TcpClient client = new(); } }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Infrastructure.DeviceComm", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEPLCOWN001");
    }

    [Fact]
    public async Task DeviceCommRegisteredServiceOwner_MayHoldTransport()
    {
        var source = "namespace IIoT.Edge.Infrastructure.DeviceComm.Plc.Services.Modbus { public sealed class Service { private System.Net.Sockets.TcpClient client = new(); } }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Infrastructure.DeviceComm", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEPLCOWN001");
    }

    [Fact]
    public async Task PlcPortDoesNotCountAsConcreteTransport()
    {
        var source = "public interface IPlcService { System.Threading.Tasks.Task ReadAsync(); } public sealed class Task { private IPlcService plc = null!; }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [PluginMetadata + source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEPLCOWN001");
    }

    [Theory]
    [InlineData("task.Wait()")]
    [InlineData("task.Result")]
    [InlineData("task.GetAwaiter().GetResult()")]
    [InlineData("System.Threading.Tasks.Task.WaitAll(task)")]
    [InlineData("System.Threading.Tasks.Task.WaitAny(task)")]
    public async Task SyncOverAsyncForms_ReportEdgeAsync001(string expression)
    {
        var returnsVoid = expression is "task.Wait()" or "System.Threading.Tasks.Task.WaitAll(task)";
        var source = returnsVoid
            ? $"public sealed class Service {{ public void Run(System.Threading.Tasks.Task<int> task) => {expression}; }}"
            : $"public sealed class Service {{ public int Run(System.Threading.Tasks.Task<int> task) => {expression}; }}";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        AssertSingle(diagnostics, "EDGEASYNC001");
    }

    [Fact]
    public async Task ValueTaskResult_ReportsEdgeAsync001()
    {
        var source = "public sealed class Service { public int Run(System.Threading.Tasks.ValueTask<int> task) => task.Result; }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        AssertSingle(diagnostics, "EDGEASYNC001");
    }

    [Fact]
    public async Task BusinessResultProperty_DoesNotCountAsTaskResult()
    {
        var source = "public sealed record BusinessOutcome(string Result); public sealed class Service { public string Run(BusinessOutcome value) => value.Result; }";

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Application", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEASYNC001");
    }

    [Fact]
    public async Task NonEventAsyncVoid_ReportsStableIdLocationAndMessage()
    {
        var source = """
            public sealed class Installer
            {
                public async void StartInstall()
                {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Installer", [source]);

        var diagnostic = AssertSingle(diagnostics, "EDGEASYNC002");
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal("Fixture1.cs", diagnostic.Location.GetLineSpan().Path);
        Assert.Equal(2, diagnostic.Location.GetLineSpan().StartLinePosition.Line);
        Assert.Equal(
            "方法 'Installer.StartInstall()' 是非事件 async void。原因：异常和完成状态无法由调用方观察；最短修复：返回 Task 并由事件 handler await；精确例外：第二参数继承 EventArgs 的真实事件处理器",
            diagnostic.GetMessage());
    }

    [Fact]
    public async Task EventHandlerAsyncVoid_IsAllowed()
    {
        var source = """
            public sealed class Window
            {
                public event System.EventHandler? Click;
                public Window() => Click += OnClick;
                private async void OnClick(object? sender, System.EventArgs e)
                {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Installer", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEASYNC002");
    }

    [Fact]
    public async Task EventShapedButUnregisteredAsyncVoid_ReportsEdgeAsync002()
    {
        var source = """
            public sealed class Window
            {
                private async void OnClick(object? sender, System.EventArgs e)
                {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Installer", [source]);

        AssertSingle(diagnostics, "EDGEASYNC002");
    }

    [Theory]
    [InlineData("public sealed record Query() : MediatR.IRequest<string>;")]
    [InlineData("public sealed class ViewModel { private MediatR.ISender sender = null!; }")]
    [InlineData("public sealed class Handler : MediatR.IRequestHandler<Query, string> { public System.Threading.Tasks.Task<string> Handle(Query request, System.Threading.CancellationToken token) => System.Threading.Tasks.Task.FromResult(string.Empty); } public sealed record Query() : MediatR.IRequest<string>;")]
    public async Task PresentationMediatRUseCases_ReportEdgePres001(string consumer)
    {
        var source = """
            namespace MediatR
            {
                public interface IRequest<T> { }
                public interface IRequestHandler<TRequest, TResponse> where TRequest : IRequest<TResponse>
                {
                    System.Threading.Tasks.Task<TResponse> Handle(TRequest request, System.Threading.CancellationToken token);
                }
                public interface ISender { }
            }
            """ + consumer;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Presentation.Navigation", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEPRES001");
    }

    [Fact]
    public async Task PresentationNotificationHandler_DoesNotReportEdgePres001()
    {
        var source = """
            namespace MediatR
            {
                public interface INotification { }
                public interface INotificationHandler<T> where T : INotification { }
            }
            public sealed record Updated() : MediatR.INotification;
            public sealed class RefreshHandler : MediatR.INotificationHandler<Updated> { }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Presentation.Navigation", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEPRES001");
    }

    [Fact]
    public async Task PresentationDirectChineseValidationIssue_ReportsEdgePres002()
    {
        var source = """
            namespace IIoT.Edge.Application.Common.Crud
            {
                public sealed record ValidationIssue(string Message, string? Field = null);
            }
            public sealed class Validator
            {
                public IIoT.Edge.Application.Common.Crud.ValidationIssue Validate()
                    => new("请修正无效字段");
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Presentation.Navigation", [source]);

        AssertSingle(diagnostics, "EDGEPRES002");
    }

    [Fact]
    public async Task PresentationLocalizedValidationIssue_DoesNotReportEdgePres002()
    {
        var source = """
            namespace IIoT.Edge.Application.Common.Crud
            {
                public sealed record ValidationIssue(string Message, string? Field = null);
            }
            public sealed class Resources { public string Get(string key) => key; }
            public sealed class Validator
            {
                public IIoT.Edge.Application.Common.Crud.ValidationIssue Validate(Resources resources)
                    => new(resources.Get("Validation_InvalidField"));
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Presentation.Navigation", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEPRES002");
    }

    private const string PluginMetadata = """
        [assembly: System.Reflection.AssemblyMetadata("EdgeModuleRole", "Entry")]
        [assembly: System.Reflection.AssemblyMetadata("IsEdgePluginModule", "true")]
        [assembly: System.Reflection.AssemblyMetadata("IsPackable", "true")]
        [assembly: System.Reflection.AssemblyMetadata("PluginModuleId", "Fixture")]
        """;

    private const string TaskPrelude = """
        namespace IIoT.Edge.Application.Abstractions.Plc
        {
            public interface IPlcTask { }
        }
        """;

    private static string RepositoryPrelude(string entityNamespace, string entityName)
        => $$"""
            namespace IIoT.Edge.SharedKernel.Domain { public interface IAggregateRoot { } }
            namespace IIoT.Edge.SharedKernel.Repository
            {
                public interface IReadRepository<T> where T : IIoT.Edge.SharedKernel.Domain.IAggregateRoot { }
                public interface IRepository<T> : IReadRepository<T> where T : IIoT.Edge.SharedKernel.Domain.IAggregateRoot
                {
                    System.Threading.Tasks.Task<int> SaveChangesAsync(System.Threading.CancellationToken token);
                }
            }
            namespace {{entityNamespace}}
            {
                public sealed class {{entityName}} : IIoT.Edge.SharedKernel.Domain.IAggregateRoot { }
            }
            """;

    private static async Task<ImmutableArray<Diagnostic>> AnalyzeAsync(
        string assemblyName,
        IReadOnlyList<string> sources,
        params MetadataReference[] additionalReferences)
    {
        var compilation = CreateCompilation(assemblyName, sources, additionalReferences);
        var compilerErrors = compilation.GetDiagnostics()
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
        Assert.True(
            compilerErrors.Length == 0,
            "Fixture compiler errors:" + Environment.NewLine + string.Join(Environment.NewLine, compilerErrors.Select(item => item.ToString())));

        var diagnostics = await compilation
            .WithAnalyzers(ImmutableArray.Create<DiagnosticAnalyzer>(new EdgeArchitectureAnalyzer()))
            .GetAnalyzerDiagnosticsAsync();
        return diagnostics
            .OrderBy(diagnostic => diagnostic.Location.GetLineSpan().Path, StringComparer.Ordinal)
            .ThenBy(diagnostic => diagnostic.Location.SourceSpan.Start)
            .ThenBy(diagnostic => diagnostic.Id, StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static CSharpCompilation CreateCompilation(
        string assemblyName,
        IReadOnlyList<string> sources,
        params MetadataReference[] additionalReferences)
    {
        var syntaxTrees = sources.Select((source, index) =>
            CSharpSyntaxTree.ParseText(
                source,
                new CSharpParseOptions(LanguageVersion.Preview),
                path: $"Fixture{index + 1}.cs"));
        return CSharpCompilation.Create(
            assemblyName,
            syntaxTrees,
            PlatformReferences.Value.AddRange(additionalReferences),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static MetadataReference CreateReference(string assemblyName, string source)
    {
        var compilation = CreateCompilation(assemblyName, [source]);
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(
            result.Success,
            "Reference compiler errors:" + Environment.NewLine + string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static readonly Lazy<ImmutableArray<MetadataReference>> PlatformReferences = new(() =>
    {
        var trustedAssemblies = (string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")
            ?? throw new InvalidOperationException("Trusted platform assemblies are unavailable.");
        return trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Where(path => path.Contains(
                $"{Path.DirectorySeparatorChar}shared{Path.DirectorySeparatorChar}Microsoft.NETCore.App{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToImmutableArray<MetadataReference>();
    });

    private static Diagnostic AssertSingle(ImmutableArray<Diagnostic> diagnostics, string id)
    {
        var diagnostic = Assert.Single(diagnostics.Where(item => item.Id == id));
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        return diagnostic;
    }
}
