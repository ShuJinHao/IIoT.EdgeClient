# Edge 架构边界契约

本文档是 `IIoT.EdgeClient` 的长期架构边界与测试归口契约。它以当前源码、MSBuild 评估图、EF model、真实写入路径、插件装载/打包路径和测试资产为依据，为 `EDGE-ARCH-001` 提供权威输入；Analyzer 不得按目录名、接口名或 EF navigation 自行猜测业务边界。

当前证据基线：`9540f6661c1bb5309751fabe776b456db8f5d6ce`。在 `EDGE-BASELINE-MIG-BOOTSTRAP` 工具完成、首次 workflow/policy receipt 被精确消费且 base-owned required check 真正强制之前，受保护的项目图、测试资产和 Phase 0 基线不得变化；远端接入生效后，Owner、例外、聚合裁决和受保护资产也只能通过受审迁移回执演进，不能在同一候选提交中改规则、改基线并自我批准。

## 1. 状态语义

- **批准**：已有真实生产读写链和业务身份依据，可直接成为编译型门禁的 allowlist。
- **过渡**：当前生产路径确实存在，但不是目标架构；必须精确到 symbol/project/path，并登记 Owner、原因和到期日。
- **未裁决**：源码证据不足。禁止新增依赖或写路径，但 Analyzer 不得擅自把它判成 root、child 或 projection。
- **禁止**：没有现有生产依据，新增即以 error 阻断。
- **动态事实**：必须通过 Architecture/Conformance/Persistence/Workflow/Deployment test 证明，不能伪装成 Roslyn 编译错误。

## 2. 当前项目图与角色

当前 solution 为 32/32 项目。MSBuild 评估得到 97 条直接 `ProjectReference`（生产 61、测试 36）、97 条直接 `PackageReference`（生产 66、测试 31）和 35 个唯一 package identity；并集图、Debug 图和 Release 图均无 SCC 或自环。生产项目没有直接或传递引用 Tests/Testing/TestKit；`Host.Bootstrap -> Presentation.VisualTestData` 仅在 Debug 生效，Release 图不含该边。

角色不是仅按目录推断，当前登记如下：

| 角色 | 当前项目 |
|---|---|
| Domain/Core | `IIoT.Edge.Domain` |
| Application | `IIoT.Edge.Application` |
| SharedKernel | `IIoT.Edge.SharedKernel` |
| UI Shared | `IIoT.Edge.UI.Shared` |
| Module SDK | `IIoT.Edge.Module.Sdk` |
| Infrastructure | `CloudClient`、`DeviceComm`、`Integration`、`Persistence.Dapper`、`Persistence.EfCore`、`Update` |
| Presentation | `Presentation.Navigation`、`Presentation.Panels`、`Presentation.Shell`、`Presentation.VisualTestData` |
| Host/Tool | `Host.Bootstrap`、`Host.DataPipeline`、`Shell`、`Launcher`、`Installer`、`RuntimeLayoutSync` |
| Plugin family shared | `Module.DieCutting.Shared`，不是可加载插件 |
| Plugin entry | `DieCuttingAnode`、`DieCuttingCathode`、`Homogenization` |
| Test | 当前 7 个 `src/Tests` 项目；物理迁移前保持 Phase 0 精确图 |

### 2.1 允许的直接依赖方向

| Source role | 允许的直接 ProjectReference |
|---|---|
| Domain/Core | SharedKernel |
| Application | Domain/Core、SharedKernel |
| SharedKernel | 无 |
| UI Shared | SharedKernel |
| Module SDK | Application、SharedKernel |
| Infrastructure | Application；同层只允许登记的窄边，当前为 Integration/Update → CloudClient，Update 另可 → SharedKernel |
| Presentation | Application、UI Shared、SharedKernel；同层只允许 Navigation → Panels 的过渡边 |
| Host composition | Application、批准的 Infrastructure/Presentation/Shared/Host runtime contract；禁止具体 Plugin |
| Plugin implementation | Application、Module SDK、SharedKernel、UI Shared；其他默认禁止 |
| Tool | 默认无项目依赖；自定义 MSBuild orchestration 必须进入隐藏边 ledger |
| VisualTestData | 只能 → Application；只允许测试项目和精确 Debug-only Host 引用；Release closure/artifact 必须为零 |
| Test | 由目标 TestKind 项目自身的精确依赖矩阵决定，禁止用一个测试巨石获得所有生产层访问权 |

### 2.2 当前精确过渡边

以下不是层级通配，只批准当前精确边：

1. `DieCuttingAnode/Cathode -> DieCutting.Shared`：同一插件族共享实现；Shared 工程必须保持不可加载、不可独立打包。
2. `DieCutting.Shared/Homogenization -> Presentation.Navigation`：临时导航 seam，目标是抽出独立 contracts 后删除。
3. `Host.Bootstrap -> Presentation.VisualTestData`：仅批准当前精确 Debug condition；任何 Release、无条件或传递进入 artifact 的形式均禁止。
4. `Launcher -> Shell`：仅批准 `ReferenceOutputAssembly=false` 的 build-only 边。
5. `Launcher -> RuntimeLayoutSync` 的自定义 `<MSBuild Projects=...>`、测试 plugin staging 的 MSBuild 边必须进入统一图和环检测，不能因不是 `ProjectReference` 而漏检。

### 2.3 已确认的项目/包技术债

- `Presentation.Panels` 持有具体 `log4net` provider 实现，属于 Presentation → Infrastructure 泄漏；迁移前只能精确过渡，禁止扩大为 Presentation 可任意引用 provider。
- Plugin 经 Navigation 传递得到具体 Panels；只批准两条 direct Navigation seam，不批准整个传递闭包成为永久 contract。
- Application 的 `Microsoft.Extensions.Hosting.Abstractions` 当前无源码消费证据，应删除验证；删除前只列精确 legacy exception。
- Shell 的 `Microsoft.EntityFrameworkCore.Design` 应迁回 persistence/design-time owner；不得据此批准普通 Host 使用 EF API。
- 14 个生产项目存在 16 条 `InternalsVisibleTo`。必须建立精确 friend ledger；`IIoT.Edge.TestSimulator` 不在当前项目图中，是 stale friend，需删除。

## 3. 聚合边界裁决

EF navigation、`DbSet` 和 cascade 只表达 ORM 关系，不自动证明 DDD ownership。当前真实写入事务裁决如下：

| 类型 | 裁决 | 依据与约束 |
|---|---|---|
| `NetworkDeviceEntity` | 批准 AggregateRoot；存在过渡冲突 | 有独立保存命令和仓储。`IoMappings`、`PlcTaskBindings` 两个 public mutable `ICollection` 不是批准的 child collection，先精确 waiver，后改私有 backing field/只读视图或移除。 |
| `IoMappingEntity` | 批准“按 NetworkDeviceId 分区的独立配置 AggregateRoot” | 由独立仓储、replace use case 和 schema reconciliation 写入；生产代码不经 `NetworkDevice.IoMappings` 修改。 |
| `PlcTaskBindingEntity` | 批准“按 NetworkDeviceId 分区的独立配置 AggregateRoot” | 由独立服务按 `(NetworkDeviceId, TaskKey)` 删除/重建并提交；不是当前 NetworkDevice child。 |
| `SerialDeviceEntity` | 批准 AggregateRoot | 独立保存命令、仓储，无 navigation child。 |
| `SystemConfigEntity` | 批准 persistence/state root | 以 Key 为业务身份和唯一键，是配置状态持久化根，不冒充 rich domain aggregate。 |
| `DeviceParamEntity` | 未裁决、dormant | 除实体、DbSet、EF configuration、migration 和实体单测外没有生产读写者。不得进入批准 root registry；任何新增 `IRepository<DeviceParamEntity>` 必须失败，直到单独裁决删除、独立 state root 或 NetworkDevice child。 |

当前批准 root 精确为 5 个；当前没有源码证据足以批准任何 Aggregate child。

`IoMappingEntity`、`PlcTaskBindingEntity` 与 `NetworkDeviceEntity` 的双向 navigation，以及三类外键的 `Cascade`，属于历史 ORM/lifecycle 策略，不是聚合所有权。删除 NetworkDevice 时 IO、binding、device-param 的物理生命周期必须由独立 policy test 明确，不能让 SQLite cascade 暗中替业务作决定。

## 4. Persistence 与数据 Owner

### 4.1 EF Core Owner

- Model/DbSet：`Infrastructure.Persistence.EfCore/EdgeDbContext`。
- Read：`EfReadRepository<T>`；不得向调用方泄漏无法释放 context 的 `IQueryable`。
- Write/commit：当前仅 `EfRepository<T>` 可调用具体 `DbContext.SaveChanges*`；Application 调用 `IRepository.SaveChangesAsync`/`ExecuteDeleteAsync` 是批准端口，不得按方法名误报。
- Runtime migration：`ApplyMigrations`，唯一 composition caller 为 `AppStartupInitializer`。
- Design-time：`EdgeDbContextFactory`。
- `EdgeSqliteSchemaRepair` 是有期限的历史例外；目标是迁成真实 migration 后物理删除，不能成为第二套 schema 演进机制。

### 4.2 Dapper/SQLite workflow state Owner

- Connection/transaction/commit：`SqliteConnectionFactory`、`DapperRepositoryBase<TEntity>`。
- `pipeline_cloud.db`：CapacityBuffer、DeviceLogBuffer、CloudRetry、CloudFallback、CloudDeadLetter。
- `pipeline_mes.db`：MesRetry、MesFallback、MesDeadLetter。
- 上述是 durable workflow state/projection，不是 AggregateRoot；Application/Domain/Presentation 不得直接使用 Dapper、SQLite connection 或 raw SQL。
- DDL 总入口为 `InitializeDapperTablesAsync`，唯一 composition caller 为 `AppStartupInitializer`。

### 4.3 模块本地 Persistence Owner

`DieCuttingProductionRecordStore` 是当前唯一批准的插件本地 raw SQLite owner。它保存 append-only 本地生产 projection，不是 AggregateRoot。UI 只能依赖 `IDieCuttingProductionRecordStore` 窄 port，不能注入 SQLite/DbContext/具体 EF repository。新增模块本地数据库 owner 必须单独登记，不能复制该例外。

### 4.4 目标写事务边界

只把 `IRepository<T>` 从 Singleton 改为 Scoped 不是修复：Shell 由根 `ServiceProvider` 解析服务，Scoped 实例会被根作用域长驻；`SaveHardwareConfigHandler` 通过三次 `ISender.Send` 仍会形成三个独立提交。目标必须是显式、一次性、禁止 ambient/`AsyncLocal` 的 Unit of Work session：

- `IEdgeUnitOfWorkFactory.BeginAsync(ct)` 创建独占一个 `EdgeDbContext` 和一个 provider-supported non-deferred SQLite transaction 的 `IEdgeUnitOfWork : IAsyncDisposable`。
- 写 repository 只能由 `IEdgeUnitOfWork.Repository<T>()` 获取；删除 open-generic `IRepository<>` 的直接 DI 注册，Application handler 不得跨 session 缓存写 repository。
- session 可提供 `FlushAsync(ct)` 仅用于同一事务内生成 identity；它不是 durable commit，其他 connection 不得看到，session 未 `CommitAsync` 即 Dispose/取消必须整体 rollback。
- `CommitAsync` 只能成功一次。Save 失败不得预先清空状态；transaction commit 异常进入 faulted/commit-unknown，禁止盲目重试或返回成功。缓存失效、PLC stop/reload 和成功文案只能在 commit 成功后发生。
- SystemConfig、TaskBinding 的 delete+insert 以及 Hardware 的 Network/Serial/IO 必须在同一 session/transaction 中完成；小配置集使用同 context query + tracked remove/add，不用另一个 context 的 bulk delete 绕过事务和 ChangeTracker。
- `Update` 继续按主键 load 后 `CurrentValues.SetValues`，不得退化为 `DbContext.Update`，避免不存在记录被插入或 navigation graph 被错误追踪。
- Stateless `IReadRepository<T>` 可保持 Singleton，但必须物理删除无人消费且泄漏 context 的 `GetQueryable()`；任何跨多步一致性读取必须从当前 UoW session 获取。
- 每个 UoW connection 必须实际证明 `PRAGMA foreign_keys=1`，并验证 busy timeout/并发错误映射；当前只在一次临时 connection 上设置 `foreign_keys` 不构成每连接保证。
- Cloud 文件 projection 不放进 SQLite transaction。保留 fail-closed saga：数据库与文件任一步失败都不得报告成功，并以独立原子补偿恢复禁用状态。

## 5. 插件、Outbound 与 PLC Owner

### 5.1 插件 contract seam

- Plugin entry、family shared、Module SDK 必须有显式角色 metadata；`Module.Sdk` 不能继续靠名称和测试跳过识别。
- 插件默认只允许 Application、Module SDK、SharedKernel、UI Shared。当前 Navigation 是精确过渡 seam。
- 模切 `DieCuttingDataViewModel` 直接依赖 `Presentation.Panels.IDeviceSelectionService` 是现存越界，目标是把 interface/selection key 下沉到 Application/UI contract；不得把整个 Panels 加入永久 allowlist。
- 具体插件禁止互引；具体入口引用其 family `.Shared` 是正例。
- Host/Application/Core/Shared/Infrastructure/Presentation 禁止引用动态发现的具体插件 symbol；配置中的稳定 ModuleId 字符串不按 type reference 误报。

### 5.2 Outbound

- 模块 Production Task 只能生成 `DataPipelineRecord` 并调用 `IDataPipelineService.EnqueueAsync`。
- Task 内禁止直接持有或调用 HttpClient、MES/Cloud client、request executor、uploader、`UploadAsync`/`PostAsync`/`SendAsync` 或 helper 包装后的同义绕过。
- MES channel/Cloud channel 的批准 outbound owner 位于 Application/Infrastructure/模块已登记 channel seam；Cloud/MES probe、gate、retry、fallback、deadletter 继续严格分离。

### 5.3 PLC transport owner

- 第三方 McpX、S7 Plc、IModbusMaster、TcpClient、SerialPort 的构造、字段持有和释放只允许 `Infrastructure.DeviceComm/Plc/Services`、`PlcServiceFactory` 和 `PlcTransportOwner<T>`。
- Plugin/Application/Presentation/Host 可以依赖批准的 `IPlcConnectionManager`/`IPlcService` 端口，不得构造、持有或释放具体 driver/transport。
- `Task.Wait`、`Task<T>.Result`、`GetAwaiter().GetResult()` 必须按 symbol/operation 识别；业务对象名为 `Result` 不得误报。
- `async void` 只允许真实 event handler。Installer 的 `StartInstall` helper 是现存违规，须改为可等待的 `Task`；UI/timer event handler 是 Analyzer 正例。

### 5.4 插件包动态边界

当前打包和 runtime sync 复制整个 module build output，实际插件目录包含 Application、Domain、Presentation、SharedKernel、UI Shared、Module SDK 等宿主程序集；现有包测试只拦现场数据，没有拦重复宿主 runtime。目标包必须使用静态 pack allowlist，只携带 entry、plugin-owned assembly/resources/config 和声明的非宿主依赖；真实包内容由 `EDGEPLUGCON001` 阻断验证。

## 6. 稳定 Rule ID 与启用状态

所有 P0/P1 可静态证明的违规最终配置为 error。Rule title、原因、违规 symbol/edge、最短修复和精确例外必须同时输出。

| Rule ID | Edge 语义 | 当前启用策略 |
|---|---|---|
| `WSARCH001` | ProjectReference、自定义 MSBuild edge 和组件依赖无环 | 可立即 error |
| `WSARCH003` | production → Tests/Testing/TestKit 永久禁止；VisualTestData 仅精确 Debug，Release closure/artifact 为零 | 可立即 error + artifact gate |
| `WSARCH004` | 项目角色矩阵、package capability matrix、隐藏 build edge 必须在 ledger | 可立即 error；现存窄边精确登记 |
| `WSARCH005` | 已退役 namespace/type/API 不得回归 | 可立即 error；按 symbol，不按字符串 |
| `WSARCH008` | TestKit 不含 case，生产不引用；friend assembly 必须精确存在 | 新 TestKit 创建时立即 error |
| `DDD001` | Domain/Core 禁 provider/framework/upper layer | 可立即 error |
| `DDD002` | 批准 aggregate 不暴露 public setter/可变集合 | ratchet；NetworkDevice 两集合精确有期限 waiver |
| `DDD003` | child 不得独立写；root/child 以本契约 registry 为准 | 部分 error；DeviceParam 未裁决，禁止新增 repository |
| `DDD004` | 通用写 Repository 只允许登记的 5 个 root | 可立即 error，不能只信 `IAggregateRoot` marker |
| `DDD005` | 聚合外部不得修改 child | 当前无批准 child，延期，不得按 navigation 猜测 |
| `DDD006` | Domain event 只能由所属 aggregate 产生 | 当前无 DomainEvent abstraction，延期；MediatR `INotification` 不等同 DomainEvent |
| `DDD007` | Application 禁具体 Store/DbContext/provider client | 可立即 error；按 symbol，不按 `Store` 名称 |
| `DATA001` | ViewModel/Controller/Endpoint 禁 DbContext/DbSet/Dapper/SQLite/具体 repository | 可立即 error；窄 query/persistence port 为正例 |
| `DATA002` | Application/Domain 禁 raw SQL、EF API、provider transaction | 可立即 error；repository port 为正例 |
| `DATA003` | Query handler 禁写库、写事件和聚合 mutation | 可立即 error；本地 collection `.Add` 为正例 |
| `DATA004` | Command/Query 禁 transport object/body/lifetime 泄漏 | 可立即 error |
| `DATA005` | provider commit 只允许登记 owner | 可立即 error；不证明现有事务正确 |
| `DATA006` | Dapper 写只允许当前 persistence owner | 可立即 error；解析 Dapper extension symbol |
| `DATA007` | migration/schema DDL 只允许唯一 owner | 先禁止新增 owner；SchemaRepair 迁移后收紧为零例外 |
| `PLUG001` | 插件禁止 Host/Infrastructure/DataPipeline/具体 Presentation | error；Navigation 临时 exact seam，Panels 越界先修 |
| `PLUG002` | 具体插件禁止互引 | 可立即 error；family Shared 为正例 |
| `PLUG003` | Host/Application/Core/Shared/Infra/Presentation 禁具体插件 symbol | 可立即 error，动态发现全部入口 |
| `PLUG004` | 插件/Shared/SDK 角色、manifest、identity metadata 完整 | 修 SDK metadata 后 error |
| `PLUG005` | 插件公开签名/注册不得泄漏 forbidden implementation type | 可立即 error |
| `PLUG006` | 静态 pack item/metadata/dependency allowlist | 改打包入口后 error |
| `EDGEOUT001` | 模块 Task 禁直接 outbound，允许 DataPipeline | 可立即 error |
| `EDGEPLCOWN001` | PLC driver/transport 只在登记 owner 构造/持有/释放 | 可立即 error |
| `EDGEASYNC001` | 禁同步等待 Task | 可立即 error |
| `EDGEASYNC002` | 禁非事件 `async void` | 修 Installer helper 后 error |
| `EDGEFRIEND001` | `InternalsVisibleTo` exact ledger、目标存在、无 wildcard/stale friend | 删除 stale friend 后 error |
| `QUALITY001` | 生产、测试基础设施、测试 case 三条重复代码 ratchet | PR hard gate，不冒充编译错误 |
| `EDGEPLUGCON001` | manifest/load/package/dependency/runtime isolation | 动态 Conformance/Deployment gate |

## 7. Analyzer 与 Architecture 测试要求

仓库独立拥有 `IIoT.Edge.Architecture.Analyzers` 和 `IIoT.Edge.Architecture.AnalyzerTests`，不引用 Cloud/AICopilot Analyzer。每条规则至少覆盖：

- 合法正例和直接反例；诊断 ID、位置和消息稳定。
- alias/global using/fully-qualified name、泛型、helper 包装、跨文件和继承/接口实现。
- 正斜杠/反斜杠、大小写、property indirection、conditional reference、transitive shared project、自定义 MSBuild edge。
- repository port `SaveChangesAsync` 与 provider `DbContext.SaveChangesAsync` 区分。
- `List/Dictionary.Add` 与 persistence Add 区分；非 Dapper `ExecuteAsync` 不误报。
- 普通 MediatR `INotification` 与 DomainEvent 区分；名称含 `Store` 但不是 provider 不误报。
- EF navigation 不等于 Aggregate child；批准的 5 root、未裁决 DeviceParam 精确对账。
- 具体 plugin → 具体 plugin 反例、plugin → family Shared 正例。
- Task 直连 uploader、经 helper 绕过反例；`IDataPipelineService` 正例。
- `Task<T>.Result` 与业务 record/property `.Result`；event `async void` 正例和 helper `async void` 反例。
- Debug-only VisualTestData 正例；无条件、`!=Release`、props/transitive/artifact copy 反例。

AnalyzerTests 必须是 Pure/Architecture 测试，不得启动 Shell、SQLite、PLC、MES、Cloud、容器或 UI。project graph/MSBuild 负例使用隔离临时 fixture，并断言 `dotnet build` 以指定 Rule ID 失败。

## 8. Edge 目标测试分类与物理归口

`Regression` 是 cross-cutting `RegressionId`，不是新垃圾桶；任何回归 case 必须归入真实 TestKind。一个测试项目只有一个主 TestKind 和受控 Runtime/依赖集合。

`IIoT.Edge.Architecture.Analyzers` 是随构建运行的 Analyzer 实现项目，不是测试项目、不得声明 TestKind；它只由 `IIoT.Edge.Architecture.AnalyzerTests` 负责正反例验证。

| 目标项目/测试域 | 主 TestKind | 允许 Runtime | 主要职责 |
|---|---|---|---|
| `IIoT.Edge.Architecture.AnalyzerTests` | Architecture | Pure/Roslyn | 每条 compiler rule 正反例、别名/helper/泛型/跨文件 |
| `IIoT.Edge.Architecture.Tests` | Architecture | Filesystem/MSBuild/Reflection | 32 项目图、隐藏 build edge、friend ledger、EF model registry、Release closure |
| `IIoT.Edge.Domain.Tests` | Aggregate | Pure | 5 个批准 root 的创建、mutation、不变量；DeviceParam 只保留裁决证据，不扩写路径 |
| `IIoT.Edge.Application.Tests` | Application | Pure/FakeTime | Command/Query/Policy/Handler，不引用 EF/Dapper/Avalonia/真实 HTTP |
| `IIoT.Edge.Plc.ContractTests` | Contract | Pure/Loopback | PLC frame、address、byte order、错误码和版本化 fixture |
| `IIoT.Edge.Mes.ContractTests` | Contract | Pure/Loopback | MES payload、签名、endpoint 和错误映射 |
| `IIoT.Edge.Cloud.ContractTests` | Contract | Pure/Loopback | Cloud paths、DeviceId 身份链、release catalog/download contract |
| `IIoT.Edge.Update.ContractTests` | Contract | Pure/Filesystem | update source、catalog、package、hash/size 和安全 contract |
| `IIoT.Edge.Module.ConformanceTests` | Conformance | Filesystem/AssemblyLoad | manifest、identity、依赖环、capability、ViewId、装载、包内容 |
| `IIoT.Edge.Persistence.Tests` | Persistence | SQLite isolated | EF/Dapper transaction、migration、cascade policy、并发/失败恢复 |
| `IIoT.Edge.Runtime.WorkflowTests` | Workflow | FakeTime/SQLite isolated | PLC lifecycle、DataPipeline、retry/fallback/deadletter、Cloud/MES 分离、取消/竞争 |
| `IIoT.Edge.Module.Homogenization.WorkflowTests` | Workflow | FakeTime/SQLite isolated | 匀浆采集、门禁、上传、补偿和恢复 |
| `IIoT.Edge.Module.DieCutting.WorkflowTests` | Workflow | FakeTime/SQLite isolated | 模切采集、门禁、上传、补偿和恢复 |
| `IIoT.Edge.Startup.IntegrationTests` | Integration | IsolatedProcess | 缺配置/PLC/MES/Cloud/IO/module profile 均非阻断，真实 Shell 组合路径 |
| `IIoT.Edge.Shell.UnitTests` | Unit | Pure/InProcess | Shell ViewModel 和纯宿主策略，不再承载仓库治理 |
| `IIoT.Edge.Shell.UiTests` | UI | Avalonia Headless | Shell 窗口、绑定、焦点和 automation |
| `IIoT.Edge.Launcher.UnitTests` | Unit | Pure/InProcess | Launcher 账号、进程、更新策略 |
| `IIoT.Edge.Launcher.UiTests` | UI | Avalonia Headless | Launcher 窗口、交互和 automation |
| `IIoT.Edge.Installer.UnitTests` | Unit | Pure/InProcess | 参数、布局和安装策略 |
| `IIoT.Edge.Installer.UiTests` | UI | Avalonia Headless | Installer 窗口、交互和 automation |
| `IIoT.Edge.UI.Shared.Tests` | UI | Avalonia Headless | 共享控件、资源、automation、主题生命周期 |
| `IIoT.Edge.GoldenTests` | GoldenEval | Pure | PLC/MES/manifest/package versioned fixture；禁止自证输入和生产现场数据 |
| `IIoT.Edge.Deployment.Tests` | Deployment | Filesystem/PowerShell/Loopback | staging、manifest、hash、resume、rollback、禁止生产数据泄漏 |
| `IIoT.Edge.Platform.WindowsTests` | Deployment | Windows Release | Velopack、Installer、快捷方式、ProgramData、DPI 和真实 artifact 边界 |

共享测试基础设施只按 runtime capability 拆为 `IIoT.Edge.Testing.Core`、`IIoT.Edge.Testing.Persistence`、`IIoT.Edge.Testing.Protocols`、`IIoT.Edge.Testing.Avalonia`。这些 TestKit 不包含 `[Fact]`/`[Theory]` 或任何测试 case，生产项目不得引用，也不得重新形成全仓 `TestDoubles.cs` 巨石。

当前旧桶迁移方向：

- `RepositoryHygieneTests` 74 case：layer/namespace/forbidden API 迁 Analyzer；project/build graph 迁 Architecture；安全/部署/UI/source golden 分别归 Security/Deployment/UI/Golden，旧类型最终物理删除。
- `Module.ContractTests` 138 case：源码 Regex 边界迁 Analyzer；协议归 Contract；manifest/load/package 归 Conformance。
- `NonUiRegressionTests` 584 case：按 Aggregate/Application/Persistence/Workflow/Contract 真实依赖拆分，`RegressionId` 保留迁移对账。
- `Shell.Tests` 211 case：Architecture、Startup、UI、Workflow 分开；不再同时承担仓库卫生和 UI。
- Update 17、Installer 15、Launcher 112、UI.Shared 14：保留正确项目职责，跨职责 case 逐条迁移，不因改名减少 1091 runner 基线。

## 9. 必须先建立的失败测试

下列 Persistence/Conformance tests 应先在旧实现上稳定失败，再修生产代码；禁止先写宽松断言让现状变绿：

1. 两个调用方共享旧 `EfRepository<T>` 时，A 的 Save 不得提交 B 的 pending operation。
2. 通过 fail-once `SaveChangesInterceptor` 让第一次保存失败后，第二次受控保存不得丢失 pending operation。
3. SystemConfig replace 的 insert 失败时，旧值必须仍存在。
4. PlcTaskBinding replace 的 insert 失败时，旧 rows 必须仍存在。
5. Network/Serial/IO 任一步被确定性 command/interceptor 拒绝时，硬件配置三表整体不得部分落库。
6. UoW 在未 Commit 时 Dispose 或取消必须整体 rollback。
7. `FlushAsync` 可生成 identity，但其他 connection 在 Commit 前不可见，session Dispose 后该 identity/row 必须消失。
8. 两个并发 replace 用 `Barrier`/`TaskCompletionSource` 编排，最终只能是完整 A 或完整 B，禁止空档、混合结果和 `sleep` 竞争测试。
9. 一个 UoW 只能 Commit 一次；二次 Commit、Commit 后继续写和 commit-unknown 后盲重试必须失败。
10. 每个真实 UoW connection 都必须断言 `PRAGMA foreign_keys=1`，不能用初始化临时 connection 的结果代替。
11. 删除 NetworkDevice 对 IO/binding/device-param 的生命周期必须符合显式 policy，且不得伤及无关设备，而不是仅接受 cascade 结果。
12. API/DI 架构测试必须证明没有直接注入式 `IRepository<>`、没有 `GetQueryable()`，所有写入经显式 UoW session。
13. `HardwareConfigCrudService.ApplyModuleTemplateAsync` 在 `SaveIoMappingsCommand` 返回 Failure 或持久化异常时不得返回“重置成功”，成功文案只能在真实保存成功后产生。
14. EF model 中批准 root 精确 5 个、未裁决 1 个，projection/state/store owner 精确对账。
15. 插件包不得包含 host/application/domain/presentation/test/dev/runtime-data assemblies/files。
16. 插件经 Navigation 不得获得未批准的 Panels implementation symbol。
17. Installer 非事件 helper 异常必须可由调用方观察。

当前 `EfRepository<T>` 以 Singleton 注册且每个闭合泛型共享 `_pendingOps`；任一调用方 Save 会取走所有调用方操作，并在真实 commit 前清空队列。`ExecuteDeleteAsync` 与后续 Add/Save 使用不同 DbContext，多处 replace/hardware save 不是原子事务。这些是生产缺陷候选，不属于 Analyzer 例外，必须由上述确定性 Persistence tests 和独立生产修复批次关闭。

Phase 0 已冻结测试源码和测试项目，因此红灯用例先在隔离 candidate worktree 中对旧生产实现运行并保留 test source digest、失败输出和命令；不得把故意失败的提交推入 protected/main required lane。完成生产修复并取得全绿候选后，才按第 10 节用精确 receipt 批准最终 project/test/count/digest delta。

## 10. Phase 0 基线迁移回执

现有 Phase 0 baseline 精确冻结 32 个项目、7 个测试项目、964 declaration、1010 execution template、1091 runner、项目/build/workflow/policy 输入。新增 Analyzer/AnalyzerTests 或物理拆分测试会合法改变这些资产，因此必须先建立受审迁移回执，不能直接重生成 baseline。

`EDGE-BASELINE-MIG-BOOTSTRAP` 已在 `scripts/tests/baselines/migrations/` 落下 v1 validator、trusted-base 提取 wrapper、reference schema 和 92 条隔离正反 self-test。它当前仍是 **bootstrap 工具和本地受控证据**：既有两份 required workflow / Phase 0 policy 尚未接入新入口，远端 branch protection 与第二位独立 reviewer 也尚未建立，因此不能宣称 receipt 已成为不可规避 trust root，也不能用本地 pending receipt 放行任何 Analyzer、项目图、测试源码或 baseline 变化。

目标采用“可信消费器 bootstrap + 授权回执提交 + 消费回执提交”协议：

### 0. 可信消费器 bootstrap

- 先把通用 receipt validator 和自身隔离正反测试放入既有 CODEOWNERS 覆盖的受控目录；旧 baseline、solution、policy、workflow 和测试源码保持不变，使 bootstrap 提交能由旧门禁独立验收。
- 后续 required workflow 必须从 `TrustedBaseRevision` 的 Git blob 提取 validator 到临时目录、复核 blob object id 后执行，禁止直接执行 candidate 自己的 validator。仓库内 candidate wrapper 只能作为防误用入口；没有 base-owned required check 时，它本身不是信任根。
- trusted validator 对每次提交比较 base/candidate 的受保护资产集合：baseline、policy/behavior、workflow、solution、所有 project/build/test source、runner、NuGet、CODEOWNERS 和 waiver。无 receipt 时这些资产必须与 base 相同；有 receipt 时只允许精确路径、状态和 SHA-256。
- bootstrap validator/wrapper/schema/self-test 后续升级只能使用唯一 `EDGE-BASELINE-TRUST-UPGRADE-001`：receipt 的全部显式变化必须都是 trust implementation path，不能夹带 workflow、baseline、项目、测试或生产路径；旧 trusted validator 负责核对精确 hash/mode/diff，升级候选仍需单独跑完整新 self-test。普通 receipt 不得替换 trusted implementation。
- trust implementation 只允许上述 validator、wrapper、schema、self-test 四个精确路径；四项在 trusted base 和普通候选中都必须存在且保持 `100644`。目录内新增第五个“信任脚本”或删除任一信任资产都必须失败，不能靠目录前缀自动获得升级权限。
- 首次接入目标 workflow 必须逐字节锁定 trigger/env/top-level key、canonical job 仅有 `runs-on` / `timeout-minutes` / `steps` 三个直接键、Windows runner、25 分钟预算、完整 SHA pinned checkout、精确 candidate ref、无条件且不可 soft-fail 的 gate step envelope，以及紧邻的 setup-dotnet 前缀。注释伪造 checkout、self-hosted runner、top-level permissions、尾置 job-level env/defaults/container/services/strategy、gate 尾部 `if: false` / `continue-on-error: true` 都必须拒绝。

### A. 授权回执提交

- 先在隔离 candidate worktree 准备完整最终候选：红灯测试对旧生产实现得到预期失败，生产修复后同一测试转绿；据最终候选计算全部 digest。隔离候选不是可信 base，也不得直接 push 到 main。
- 只新增一次性 receipt/ledger，不修改旧 baseline、policy、workflow、solution 或项目文件。
- receipt 固定：旧 baseline SHA-256、候选新 baseline SHA-256、全部受保护资产的新 SHA-256、允许变更路径精确集合、项目/测试/count 增减、Rule ID、Owner、批准人、原因、到期日、唯一 MigrationId。
- receipt 必须在旧门禁下独立 build/test/CI 全绿并进入可信 base；作者不能把未进入 base 的本地 receipt 当批准。
- AuthorizationOnly 固定为 trusted base 的单父直接子提交，只能新增一个 `mode 100644` pending receipt；receipt 使用严格 UTF-8/JSON、大小写敏感字段、注册 Rule/Owner/Approver、最长 7 天、最大 1 MiB、最多 5000 个变化，路径必须满足 Windows 可移植性。`approvedBy` 仍只是回执声明，身份真实性必须由远端独立 Code Owner / branch rule 证明。

### B. 消费回执提交

- candidate 只能消费 trusted base 上尚未使用且未过期的 receipt，并且必须是该 authorization base 的单父直接子提交。PR 校验必须 checkout / 传递 `pull_request.head.sha`，不得拿 GitHub synthetic merge SHA 冒充迁移候选；远端合并策略需限制为 squash/rebase，merge-shaped consumption fail-closed。
- 实际 diff、全部保护资产 digest、project/test/count delta 必须与 receipt 完全一致；多一个、少一个或 hash 不同都失败。
- pending receipt 必须逐字节、同 `100644` mode 移到 `consumed/`；消费后 MigrationId 不可重复。目标 testProjects/testSource/declaration/execution/projected/runner 声明不得下降，但 declaration/execution/runner 仍含 baseline 声明值，不能替代 Phase 0 scanner、真实 discovery 和 1091+ runner 全量验收。
- 过期、候选废弃或 intervening main 变化时，允许从“当前含唯一 pending receipt 的 trusted base”用一个单父直接子提交把同一 blob 移到 `cancelled/`；取消可接受已过期 receipt，但不能改字节、mode 或夹带其他路径。consumed/cancelled MigrationId 都永久阻止重放。
- 无 pending 且 protected diff 为空时是 Immutable；只有上述 AuthorizationOnly、Consume、Cancel、隔离 TrustUpgrade 四种状态可改变 receipt/protected 状态，其他路径一律 fail-closed。
- 迁移只允许正常 descendant push。若现有 policy 无法安全引入可信消费器，必须重新取得当前轮明确授权后，才可讨论精确 lease 的一次性历史过渡；Cloud 的历史授权不得自动扩大到 Edge。

reference JSON schema 只帮助 author/reviewer 检查字段；字节上限、ordinal 排序、Windows path、时间窗口、状态拓扑、跨字段与单调约束以 trusted validator 为权威。首次 workflow/policy 接入必须用精确 receipt 完成，并让 required job 实际运行本 self-test；在 base-owned required check、squash/rebase-only、禁止 direct/force/admin bypass 和独立 reviewer 尚未具备时，只能称为“受控迁移证据”。

## 11. 实施批次与退出条件

1. **EDGE-ARCH-001A 边界契约**：冻结本文的项目角色、5+1 聚合裁决、persistence/plugin/PLC owner、Rule ID 和已知债务。退出：文档链接、`rg` 死链检查、工作树 diff 审核通过。
2. **EDGE-BASELINE-MIG-BOOTSTRAP**：v1 validator/wrapper/schema 和 92 条隔离 self-test 已实现；本批仍需旧门禁、本地 full suite、远端 Windows CI 独立验收。首次 workflow/policy 接入另做精确 receipt，并把 self-test 加入 required job。退出：无 receipt 变化、伪/过期/超大/无效 UTF-8 receipt、重放、取消、trust-upgrade 隔离、路径/mode/hash/count 漂移、非单父拓扑、workflow gate 篡改均有稳定正反证据；未接远端前仍不得关单。
3. **EDGE-ARCH-001B 隔离 Analyzer 候选**：在独立 worktree 建仓库专属 Analyzer/AnalyzerTests、project graph/owner ledger；无现存违规的规则立即 error，现存 Panels/SDK/async/schema/navigation 债务使用精确 Owner+原因+到期日 waiver，不等待所有生产债务修复。
4. **EDGE-BASELINE-MIG-ARCH-001**：据第 3 步最终全绿候选计算精确 digest；独立授权 receipt 先进入可信 base，再正常提交候选消费。退出：正反 fixture 使约定违规 `dotnet build` 红、合法 alias/helper/generic/跨文件场景绿，1091 既有 case 不减少。
5. **EDGE-PERSIST-001 隔离红灯候选**：独立于 Analyzer，在单独 worktree 编写第 9 节的并发、失败恢复、replace 原子性、cascade policy tests，对旧生产实现取得可复现失败；保留命令、test source digest 和失败输出，不 push 故意失败的 main 候选。
6. **EDGE-PERSIST-002 隔离修复候选**：以第 4.4 节的显式一次性 UoW session 替代 Singleton pending queue；禁止只改 Scoped、ambient/`AsyncLocal` 或继续让 handler 自行分段 commit。修复 replace/hardware transaction、每连接 pragma、模板保存结果传播并删除无 owner `GetQueryable`，使红灯转绿；据最终候选计算精确 project/test/count/asset digest。
7. **EDGE-BASELINE-MIG-PERSIST-001**：只把第 6 步最终候选的精确授权 receipt 作为独立提交送入可信 base并全绿；随后正常提交全绿候选消费 receipt。退出：SQLite migration/启动回归、1091 既有 case 不减少，新增 Persistence case 全绿。
8. **EDGE-BOUNDARY-002**：用同样的隔离候选/receipt 协议收口现存越界并逐项删除 Analyzer waiver：下沉 DeviceSelection contract、移走 Presentation log4net、删除无用 Hosting/EF Design/stale friend、修 Installer helper、声明 SDK role、迁移 SchemaRepair、裁决 DeviceParam/navigation/cascade。
9. **EDGE-PLUG-CON-001**：收紧静态 pack item，建立真实 manifest/load/package conformance，插件包不再复制宿主 runtime；涉及新测试/基线时继续使用独立 receipt。
10. **EDGE-TEST-PHYSICAL-001+**：按 Aggregate → Application → 四类 Contract/Conformance → Persistence → Workflow → Startup/Unit/UI/Golden/Deployment 顺序物理迁移；每批都对账 declaration、InlineData、execution、runner、Skip 和 RegressionId，并各自消费精确 receipt。
11. **EDGE-QUALITY-001**：建立生产代码、测试基础设施、测试 case 三条 duplication baseline/ratchet；新增 clone 阻断 PR，例外必须 Owner+原因+到期日。
12. **远端终审**：Windows Release 全量、1091+新增 case、annotations、artifact、25 分钟 p95；确认第二位 reviewer/team 后再配置 required Code Owner/branch protection。禁止以删测试、Skip、过滤或放宽断言换绿。

## 12. 非声明范围

- 本契约不改变 PLC/MES/Cloud、设备身份、生产数据、UI、配置、数据库或部署行为。
- 本契约不证明当前 EF transaction、插件包隔离或启动链已经正确；它明确登记了必须由后续失败测试关闭的风险。
- 当前源码字符串 `RepositoryHygiene`/`ArchitectureBoundaryContractTests` 仍作为 Phase 0 ratchet 保留，直到等价或更强的 semantic gate 已远端全绿、case 完整迁移且旧入口零引用后才可删除。
- 本契约不把 Cloud 的 force-with-lease 授权延伸到 Edge，也不授权部署、发布或生产数据操作。
