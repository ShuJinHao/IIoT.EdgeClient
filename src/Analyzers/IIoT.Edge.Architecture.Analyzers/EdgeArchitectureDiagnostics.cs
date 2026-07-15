using Microsoft.CodeAnalysis;

namespace IIoT.Edge.Architecture.Analyzers;

internal static class EdgeArchitectureDiagnostics
{
    private const string Category = "IIoT.Architecture";
    private const string HelpBase = "https://github.com/ShuJinHao/IIoT.EdgeClient/blob/main/docs/Edge架构边界契约.md";

    internal static readonly DiagnosticDescriptor ProductionTestReference = Create(
        "WSARCH003",
        "生产项目不得引用测试资产",
        "生产程序集 '{0}' 引用了测试程序集 '{1}'。原因：测试替身不得进入生产依赖图；最短修复：删除该引用并把替身留在 Tests/Testing/TestKit；精确例外：无",
        "Production assemblies cannot reference Tests, Testing, TestKit, TestPlugin, xUnit, or test SDK assemblies.",
        "#wsarch003");

    internal static readonly DiagnosticDescriptor ProjectRoleReference = Create(
        "WSARCH004",
        "项目角色依赖方向错误",
        "程序集 '{0}' 的角色 '{1}' 不得使用程序集 '{2}' 的角色 '{3}'。原因：依赖必须服从已登记角色矩阵；最短修复：把端口下沉到允许层或删除越界引用；精确例外：仅契约登记的精确边",
        "Project and symbol dependencies must follow the version-controlled Edge role registry.",
        "#wsarch004");

    internal static readonly DiagnosticDescriptor RepositoryRoot = Create(
        "DDD004",
        "通用仓储仅服务批准聚合根",
        "符号 '{0}' 使用仓储实体 '{1}'。原因：通用仓储只批准五个登记 root，DeviceParam 仍未裁决；最短修复：使用批准 root 或改为经裁决的窄 store/read port；精确例外：{2}",
        "IRepository<T> and IReadRepository<T> are limited to the five explicitly approved roots.",
        "#ddd004");

    internal static readonly DiagnosticDescriptor DomainDependency = Create(
        "DDD001",
        "Domain/Core 不得依赖框架或上层",
        "Domain 符号 '{0}' 使用禁止依赖 '{1}'。原因：Domain 只能依赖 SharedKernel 和 BCL；最短修复：把端口下沉到 Domain/SharedKernel 并由外层实现；精确例外：无",
        "Domain/Core cannot use Application, Infrastructure, Presentation, Host, plugin, or provider framework symbols.",
        "#ddd001");

    internal static readonly DiagnosticDescriptor ApplicationProvider = Create(
        "DDD007",
        "Application 不得拥有具体 provider client",
        "Application 符号 '{0}' 持有具体 provider 类型 '{1}'。原因：Application 只能依赖 persistence/query 端口；最短修复：定义窄端口并把实现移到 Infrastructure；精确例外：名称含 Store 但不实现 provider API 的普通业务类型",
        "Application cannot own DbContext, DbSet, Dapper, SQLite, ADO.NET or other concrete provider clients.",
        "#ddd007");

    internal static readonly DiagnosticDescriptor PresentationDatabaseAccess = Create(
        "DATA001",
        "展示层或宿主越过数据库端口",
        "符号 '{0}' 直接使用数据库 API '{1}'。原因：ViewModel/Host/Plugin 不能拥有 EF、Dapper、SQLite 或 provider transaction；最短修复：改用窄 query/persistence port；精确例外：无",
        "Presentation, host, tool and plugin code cannot own concrete database APIs.",
        "#data001");

    internal static readonly DiagnosticDescriptor InnerLayerDatabaseAccess = Create(
        "DATA002",
        "Application 或 Domain 直接使用数据库 provider",
        "符号 '{0}' 直接使用数据库 API '{1}'。原因：Application/Domain 只能依赖 repository/UoW 端口；最短修复：把 provider 实现移到登记的 persistence owner；精确例外：IRepository/IReadRepository 端口调用",
        "Application and Domain cannot use raw SQL, EF, Dapper, SQLite, ADO.NET, or provider transactions.",
        "#data002");

    internal static readonly DiagnosticDescriptor ProviderCommitOwner = Create(
        "DATA005",
        "Provider commit 只能由登记 owner 执行",
        "符号 '{0}' 调用了 provider commit '{1}'。原因：具体 DbContext commit 只属于 IIoT.Edge.Infrastructure.Persistence.EfCore；最短修复：经 repository/UoW 端口提交；精确例外：EfCore owner",
        "Concrete DbContext SaveChanges calls are limited to the registered EF owner.",
        "#data005");

    internal static readonly DiagnosticDescriptor DapperWriteOwner = Create(
        "DATA006",
        "Dapper 写入只能由登记 owner 执行",
        "符号 '{0}' 调用了 Dapper 写 API '{1}'。原因：raw Dapper write 只属于 IIoT.Edge.Infrastructure.Persistence.Dapper；最短修复：经窄 persistence port 写入；精确例外：Dapper owner",
        "Dapper write operations are limited to the registered Dapper owner.",
        "#data006");

    internal static readonly DiagnosticDescriptor PresentationMediatRUseCase = Create(
        "EDGEPRES001",
        "Presentation 不得定义或发送 MediatR use case",
        "展示层符号 '{0}' 使用了 MediatR use-case 类型 '{1}'。原因：Command/Query 与 handler 属于 Application，ViewModel 只能依赖窄 facade；最短修复：把 use case 下沉到 Application 并注入窄展示端口；精确例外：INotificationHandler<T> 展示刷新",
        "Presentation cannot define IRequest/IRequestHandler use cases or inject ISender; notification handlers remain allowed.",
        "#edgepres001");

    internal static readonly DiagnosticDescriptor DirectVisibleValidationText = Create(
        "EDGEPRES002",
        "Presentation 校验消息必须走资源键",
        "展示层符号 '{0}' 直接构造了可见中文校验消息 '{1}'。原因：可见文本必须可本地化；最短修复：通过资源服务解析消息后再构造 ValidationIssue；精确例外：无",
        "Presentation ValidationIssue messages cannot embed visible Chinese literals.",
        "#edgepres002");

    internal static readonly DiagnosticDescriptor PluginForbiddenReference = Create(
        "PLUG001",
        "插件使用了禁止的宿主实现",
        "插件符号 '{0}' 使用禁止的宿主实现 '{1}'。原因：插件只能依赖 Application、Module SDK、SharedKernel、UI Shared 和精确 Navigation seam；最短修复：改用稳定 contract；精确例外：Presentation.Navigation 的已登记 symbol seam",
        "Concrete plugins cannot use Host, Infrastructure, DataPipeline runtime, Panels, Shell, or VisualTestData implementation symbols.",
        "#plug001");

    internal static readonly DiagnosticDescriptor PluginCrossReference = Create(
        "PLUG002",
        "具体插件不得互相依赖",
        "插件 '{0}' 使用另一具体插件符号 '{1}'。原因：工序实现不得互引或经 family Shared 共享；最短修复：把稳定通用 contract 裁决到 Module SDK；精确例外：无",
        "Concrete plugins cannot reference another concrete plugin or a plugin-family Shared project.",
        "#plug002");

    internal static readonly DiagnosticDescriptor HostPluginReference = Create(
        "PLUG003",
        "宿主与公共层不得引用具体插件",
        "非插件符号 '{0}' 使用具体插件符号 '{1}'。原因：宿主必须通过动态发现和稳定契约装载插件；最短修复：删除静态类型引用并改用 manifest/contract；精确例外：稳定 ModuleId 字符串不是类型引用",
        "Host, Application, Domain, Shared, Infrastructure and Presentation cannot use concrete plugin symbols.",
        "#plug003");

    internal static readonly DiagnosticDescriptor PluginRoleMetadata = Create(
        "PLUG004",
        "插件与 SDK 必须声明稳定角色 metadata",
        "程序集 '{0}' 的插件角色 metadata 无效：{1}。原因：Entry/SDK/fixture 必须显式区分，不能靠名称或测试跳过识别；最短修复：声明 EdgeModuleRole、IsEdgePluginModule、PluginModuleId、IsPackable 并保持 plugin.json 一致；精确例外：无",
        "Concrete plugin entries and Module SDK assemblies must expose explicit, stable role metadata.",
        "#plug004");

    internal static readonly DiagnosticDescriptor PluginChannelRegistration = Create(
        "PLUG005",
        "插件通道和宿主契约必须走模块 builder",
        "插件符号 '{0}' 使用了非标准模块注册或通道基类 '{1}'。原因：硬件 profile、信号 profile、开发样例和 Cloud 通道只能经 IEdgeProcessModuleBuilder/CloudUploadChannelBase 注册；最短修复：改用对应 builder 方法或标准通道基类；精确例外：普通插件私有服务注册",
        "Plugin hardware/sample contracts must use IEdgeProcessModuleBuilder and plugin Cloud uploaders must use CloudUploadChannelBase.",
        "#plug005");

    internal static readonly DiagnosticDescriptor ProductionTaskOutbound = Create(
        "EDGEOUT001",
        "模块生产任务绕过 DataPipeline",
        "生产任务 '{0}' 经调用 '{1}' 到达外部出口 '{2}'。原因：生产任务只能创建记录并调用 IDataPipelineService.EnqueueAsync；最短修复：删除 HTTP/uploader 调用并经 DataPipeline 入队；精确例外：IDataPipelineService.EnqueueAsync",
        "Production PLC tasks cannot reach HTTP, MES/Cloud clients, request executors, or uploaders, including through helpers and interface dispatch.",
        "#edgeout001");

    internal static readonly DiagnosticDescriptor ProductionTaskEnqueueGuard = Create(
        "EDGEOUT002",
        "模块生产任务必须处理入队异常",
        "生产任务 '{0}' 的 DataPipeline 入队调用未由 Exception catch 保护。原因：队列或持久化异常不得逃逸并中断 PLC 任务；最短修复：在任务边界捕获 Exception 并记录明确失败结果；精确例外：无",
        "Production PLC tasks must translate IDataPipelineService.EnqueueAsync exceptions at the task boundary.",
        "#edgeout002");

    internal static readonly DiagnosticDescriptor RemovedCompatibilityContract = Create(
        "EDGECOMP001",
        "已删除兼容契约不得回流",
        "符号 '{0}' 使用或重新声明了已删除兼容契约 '{1}'。原因：无真实调用方的 alias/wrapper/fallback 已物理删除；最短修复：使用当前强类型通道、信号契约或模块 builder；精确例外：无",
        "Removed uploader, signal-interaction, static PLC profile, and untyped signal-accessor contracts cannot be reintroduced.",
        "#edgecomp001");

    internal static readonly DiagnosticDescriptor CloudRouteLiteral = Create(
        "EDGECLOUDCFG001",
        "Cloud API 路由不得硬编码在生产 C#",
        "符号 '{0}' 硬编码 Cloud API 路由 '{1}'。原因：生产路由只能来自 CloudApi 配置快照；最短修复：通过 ICloudApiPathProvider/CloudApiConfigSnapshot 获取路径；精确例外：测试程序集",
        "Production C# code cannot hard-code /api/v1 routes; paths must come from the Cloud API configuration snapshot.",
        "#edgecloudcfg001");

    internal static readonly DiagnosticDescriptor PlcTransportOwner = Create(
        "EDGEPLCOWN001",
        "PLC concrete transport 越过唯一 owner",
        "符号 '{0}' 持有、构造或释放 PLC transport '{1}'。原因：具体 driver/transport 只能由 DeviceComm/Plc/Services、PlcServiceFactory 或 PlcTransportOwner<T> 拥有；最短修复：依赖 IPlcConnectionManager/IPlcService 端口；精确例外：登记 owner",
        "Concrete PLC drivers and transports have one registered owner.",
        "#edgeplcown001");

    internal static readonly DiagnosticDescriptor SyncOverAsync = Create(
        "EDGEASYNC001",
        "禁止同步等待 Task",
        "符号 '{0}' 同步等待 Task：'{1}'。原因：同步等待会阻塞 UI/runtime 并破坏取消；最短修复：改为 await 并传播 CancellationToken；精确例外：无",
        "Task.Wait, Task<T>.Result and GetAwaiter().GetResult() are forbidden.",
        "#edgeasync001");

    internal static readonly DiagnosticDescriptor AsyncVoid = Create(
        "EDGEASYNC002",
        "async void 仅允许真实事件处理器",
        "方法 '{0}' 是非事件 async void。原因：异常和完成状态无法由调用方观察；最短修复：返回 Task 并由事件 handler await；精确例外：第二参数继承 EventArgs 的真实事件处理器",
        "Non-event async void methods are forbidden.",
        "#edgeasync002");

    private static DiagnosticDescriptor Create(
        string id,
        string title,
        string message,
        string description,
        string anchor)
        => new(
            id,
            title,
            message,
            Category,
            DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            description,
            helpLinkUri: HelpBase + anchor);
}
