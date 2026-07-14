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
            public interface IProcessMesUploader
            {
                System.Threading.Tasks.Task UploadAsync();
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly IProcessMesUploader uploader;
                public Task(IProcessMesUploader uploader) => this.uploader = uploader;
                public System.Threading.Tasks.Task RunAsync() => uploader.UploadAsync();
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
            public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            public sealed class Helper : IHelper
            {
                private readonly IProcessCloudUploader uploader;
                public Helper(IProcessCloudUploader uploader) => this.uploader = uploader;
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
            public interface IProcessCloudUploader { System.Threading.Tasks.Task UploadAsync(); }
            public sealed class Holder<T> { public Holder(T value) => Value = value; public T Value { get; } }
            public static class GenericHelper
            {
                public static System.Threading.Tasks.Task Send<T>(Holder<T> holder) where T : IProcessCloudUploader
                    => holder.Value.UploadAsync();
            }
            public sealed class Task : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                private readonly Holder<IProcessCloudUploader> holder;
                public Task(Holder<IProcessCloudUploader> holder) => this.holder = holder;
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
            public interface IProcessMesUploader { System.Threading.Tasks.Task UploadAsync(); }
            public abstract class BaseTask : IIoT.Edge.Application.Abstractions.Plc.IPlcTask
            {
                public System.Threading.Tasks.Task RunAsync() => SendCoreAsync();
                protected abstract System.Threading.Tasks.Task SendCoreAsync();
            }
            public sealed class Task : BaseTask
            {
                private readonly IProcessMesUploader uploader;
                public Task(IProcessMesUploader uploader) => this.uploader = uploader;
                protected override System.Threading.Tasks.Task SendCoreAsync() => uploader.UploadAsync();
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
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
                public System.Threading.Tasks.Task RunAsync() => pipeline.EnqueueAsync(new object());
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Module.Fixture", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEOUT001");
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
    public async Task SyncOverAsyncForms_ReportEdgeAsync001(string expression)
    {
        var source = expression == "task.Wait()"
            ? "public sealed class Service { public void Run(System.Threading.Tasks.Task<int> task) => task.Wait(); }"
            : $"public sealed class Service {{ public int Run(System.Threading.Tasks.Task<int> task) => {expression}; }}";

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
                private async void OnClick(object? sender, System.EventArgs e)
                {
                    await System.Threading.Tasks.Task.Yield();
                }
            }
            """;

        var diagnostics = await AnalyzeAsync("IIoT.Edge.Installer", [source]);

        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "EDGEASYNC002");
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
