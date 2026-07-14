# Edge 架构边界契约

本文档是 `IIoT.EdgeClient` 的长期架构边界与测试归口契约。它以当前源码、MSBuild 评估图、EF model、真实写入路径、插件装载/打包路径和测试资产为依据，为 `EDGE-ARCH-001` 提供权威输入；Analyzer 不得按目录名、接口名或 EF navigation 自行猜测业务边界。

当前项目与测试真值由 `scripts/tests/edge-test-inventory.json` 和 `scripts/tests/required-test-counts.json` 登记，并由真实 solution 枚举、`dotnet test --list-tests` 与 TRX 对账。合法演进统一复核当前任务授权、完整 diff、before/after inventory、Release build、发现/执行数量和 0 Skip；已确认功能退役时直接删除其源码和专属测试，不保留兼容工程或 dummy case。

## 1. 状态语义

- **批准**：已有真实生产读写链和业务身份依据，可直接成为编译型门禁的 allowlist。
- **过渡**：当前生产路径确实存在，但不是目标架构；必须精确到 symbol/project/path，并登记 Owner、原因和到期日。
- **未裁决**：源码证据不足。禁止新增依赖或写路径，但 Analyzer 不得擅自把它判成 root、child 或 projection。
- **禁止**：没有现有生产依据，新增即以 error 阻断。
- **动态事实**：必须通过 Architecture/Conformance/Persistence/Workflow/Deployment test 证明，不能伪装成 Roslyn 编译错误。

## 2. 当前项目图与角色

当前 solution 与仓库项目图均为 32 个项目，其中 8 个是 required 测试项目，1 个是位于 `src/Testing` 的中性插件 fixture，新增的 Analyzer 实现项目不是测试项目。生产项目不得直接或传递引用 Tests/Testing/TestKit；`Host.Bootstrap -> Presentation.VisualTestData` 仅在 Debug 生效，Release 图不得包含该边。项目数只是 inventory，不是永久冻结；任何增减必须同时更新清单、构建图、真实发现数和滚动复盘。

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
| Plugin SDK | `Module.Sdk`，只提供通用 contract 和基础能力，不承载具体工序 |
| Concrete plugin | 只作当前实现库存；任何具体工序都不是宿主规则来源，未来仓库归属由下一份独立计划裁决 |
| Analyzer | `IIoT.Edge.Architecture.Analyzers`；只作为生产构建的 Analyzer 引用 |
| Test | 当前 8 个 `src/Tests` required 项目；物理迁移时必须同步更新 inventory 与真实 runner 对账 |
| Test fixture | `src/Testing/IIoT.Edge.TestPlugin`；只用于测试构建与 staging，禁止成为生产发布输入 |

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

1. `当前具体插件 -> Presentation.Navigation`：现有树中的精确临时导航 seam；宿主/插件仓拆分前必须抽出稳定 UI registration contract，不得把某一工序名称或实现结构固化进宿主规则，具体插件也不得传递获得 Panels 实现。
2. `Host.Bootstrap -> Presentation.VisualTestData`：仅批准当前精确 Debug condition；任何 Release、无条件或传递进入 artifact 的形式均禁止。
3. `Launcher -> Shell`：仅批准 `ReferenceOutputAssembly=false` 的 build-only 边。
4. `Launcher -> RuntimeLayoutSync` 的自定义 `<MSBuild Projects=...>`、测试 plugin staging 的 MSBuild 边必须进入统一图和环检测，不能因不是 `ProjectReference` 而漏检。

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

宿主契约当前不批准任何具体工序的模块本地 raw SQLite owner。具体插件确需持久化时，应由插件业务文档裁决窄 persistence port、schema、migration、transaction 和 UI 查询边界，并由插件仓测试负责；宿主只验证包隔离和 contract，不复制已移除工序的数据库实现，也不得把历史实现当作新插件正例。

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

- Plugin entry、Module SDK 必须有显式角色 metadata；`Module.Sdk` 不能继续靠名称和测试跳过识别，禁止新增插件族 `*.Shared` 业务工程。
- 插件默认只允许 Application、Module SDK、SharedKernel、UI Shared。当前 Navigation 是精确过渡 seam。
- 具体插件禁止互引，也不得通过 family shared 工程共享具体工序实现；确需通用能力时只能进入经过裁决的 Module SDK 或稳定 contract。
- Host/Application/Core/Shared/Infrastructure/Presentation 禁止引用动态发现的具体插件 symbol；配置中的稳定 ModuleId 字符串不按 type reference 误报。

### 5.2 Outbound

- 模块 Production Task 只能生成 `DataPipelineRecord` 并调用 `IDataPipelineService.EnqueueAsync`。
- Task 内禁止直接持有或调用 HttpClient、MES/Cloud client、request executor、uploader、`UploadAsync`/`PostAsync`/`SendAsync` 或 helper 包装后的同义绕过。
- MES channel/Cloud channel 的批准 outbound owner 位于 Application/Infrastructure/模块已登记 channel seam；Cloud/MES probe、gate、retry、fallback、deadletter 继续严格分离。

### 5.3 PLC transport owner

- 第三方 McpX、S7 Plc、IModbusMaster、TcpClient、SerialPort 的构造、字段持有和释放只允许 `Infrastructure.DeviceComm/Plc/Services`、`PlcServiceFactory` 和 `PlcTransportOwner<T>`。
- Plugin/Application/Presentation/Host 可以依赖批准的 `IPlcConnectionManager`/`IPlcService` 端口，不得构造、持有或释放具体 driver/transport。
- `Task.Wait`、`Task<T>.Result`、`GetAwaiter().GetResult()` 必须按 symbol/operation 识别；业务对象名为 `Result` 不得误报。
- `async void` 只允许真实 event handler。Installer 的启动 helper 已改为可等待的 `StartInstallAsync`；UI/timer event handler 是 Analyzer 正例。

### 5.4 插件包动态边界

当前打包和 runtime sync 复制整个 module build output，实际插件目录包含 Application、Domain、Presentation、SharedKernel、UI Shared、Module SDK 等宿主程序集；现有包测试只拦现场数据，没有拦重复宿主 runtime。目标包必须使用静态 pack allowlist，只携带 entry、plugin-owned assembly/resources/config 和声明的非宿主依赖；真实包内容由 `EDGEPLUGCON001` 阻断验证。

## 6. 稳定 Rule ID 与启用状态

所有 P0/P1 可静态证明的违规最终配置为 error。Rule title、原因、违规 symbol/edge、最短修复和精确例外必须同时输出。

| Rule ID | Edge 语义 | 当前启用策略 |
|---|---|---|
| `WSARCH001` | ProjectReference、自定义 MSBuild edge 和组件依赖无环 | 已启用 project-graph error |
| `WSARCH003` | production → Tests/Testing/TestKit 永久禁止；VisualTestData 仅精确 Debug，Release closure/artifact 为零 | 已启用 Analyzer + project-graph error |
| `WSARCH004` | 项目角色矩阵、package capability matrix、隐藏 build edge 必须在受版本控制的 registry | 已启用 Analyzer + project-graph error；现存窄边精确登记 |
| `WSARCH005` | 已退役 namespace/type/API 不得回归 | 可立即 error；按 symbol，不按字符串 |
| `WSARCH008` | TestKit 不含 case，生产不引用；friend assembly 必须精确存在 | 新 TestKit 创建时立即 error |
| `DDD001` | Domain/Core 禁 provider/framework/upper layer | 已启用 error |
| `DDD002` | 批准 aggregate 不暴露 public setter/可变集合 | ratchet；NetworkDevice 两集合精确有期限 waiver |
| `DDD003` | child 不得独立写；root/child 以本契约 registry 为准 | 部分 error；DeviceParam 未裁决，禁止新增 repository |
| `DDD004` | 通用写 Repository 只允许登记的 5 个 root | 已启用 error，不能只信 `IAggregateRoot` marker |
| `DDD005` | 聚合外部不得修改 child | 当前无批准 child，延期，不得按 navigation 猜测 |
| `DDD006` | Domain event 只能由所属 aggregate 产生 | 当前无 DomainEvent abstraction，延期；MediatR `INotification` 不等同 DomainEvent |
| `DDD007` | Application 禁具体 Store/DbContext/provider client | 已启用 error；按 symbol，不按 `Store` 名称 |
| `DATA001` | ViewModel/Controller/Endpoint 禁 DbContext/DbSet/Dapper/SQLite/具体 repository | 已启用 error；窄 query/persistence port 为正例 |
| `DATA002` | Application/Domain 禁 raw SQL、EF API、provider transaction | 已启用 error；repository port 为正例 |
| `DATA003` | Query handler 禁写库、写事件和聚合 mutation | 可立即 error；本地 collection `.Add` 为正例 |
| `DATA004` | Command/Query 禁 transport object/body/lifetime 泄漏 | 可立即 error |
| `DATA005` | provider commit 只允许登记 owner | 已启用 error；不证明现有事务正确 |
| `DATA006` | Dapper 写只允许当前 persistence owner | 已启用 error；解析 Dapper extension symbol |
| `DATA007` | migration/schema DDL 只允许唯一 owner | 先禁止新增 owner；SchemaRepair 迁移后收紧为零例外 |
| `PLUG001` | 插件禁止 Host/Infrastructure/DataPipeline/具体 Presentation | 已启用 error；Navigation 临时 exact seam |
| `PLUG002` | 具体插件禁止互引，禁止插件族 Shared 业务工程 | 已启用 error；通用能力只能进入 SDK/contract |
| `PLUG003` | Host/Application/Core/Shared/Infra/Presentation 禁具体插件 symbol | 已启用 error，动态发现全部入口 |
| `PLUG004` | 插件/Shared/SDK 角色、manifest、identity metadata 完整 | 已补 metadata 并启用 Analyzer + project-graph error |
| `PLUG005` | 插件公开签名/注册不得泄漏 forbidden implementation type | 可立即 error |
| `PLUG006` | 静态 pack item/metadata/dependency allowlist | 改打包入口后 error |
| `EDGEOUT001` | 模块 Task 禁直接 outbound，允许 DataPipeline | 已启用 error |
| `EDGEPLCOWN001` | PLC driver/transport 只在登记 owner 构造/持有/释放 | 已启用 error |
| `EDGEASYNC001` | 禁同步等待 Task | 已启用 error；已删除 Cloud endpoint 配置读取的真实阻塞路径 |
| `EDGEASYNC002` | 禁非事件 `async void` | 已修 Installer helper 并启用 error |
| `EDGEFRIEND001` | `InternalsVisibleTo` exact ledger、目标存在、无 wildcard/stale friend | 删除 stale friend 后 error |
| `QUALITY001` | 生产、测试基础设施、测试 case 三条重复代码 ratchet | PR hard gate，不冒充编译错误 |
| `COMPAT001` | 旧 alias/adapter/wrapper/fallback/双轨路径必须有真实 consumer、到期和测试 | 无 consumer 或到期项物理删除；禁止新增调用方 |
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
- 具体 plugin → 具体 plugin、plugin → family Shared 反例；plugin → Module SDK/稳定 contract 正例。
- Task 直连 uploader、经 helper 绕过反例；`IDataPipelineService` 正例。
- `Task<T>.Result` 与业务 record/property `.Result`；event `async void` 正例和 helper `async void` 反例。
- Debug-only VisualTestData 正例；无条件、`!=Release`、props/transitive/artifact copy 反例。

AnalyzerTests 必须是 Pure/Architecture 测试，不得启动 Shell、SQLite、PLC、MES、Cloud、容器或 UI。project graph/MSBuild 负例使用隔离临时 fixture，并断言 `dotnet build` 以指定 Rule ID 失败。

当前实现有 17 个默认 error compiler diagnostics：`WSARCH003/004`、`DDD001/004/007`、`DATA001/002/005/006`、`PLUG001/002/003/004`、`EDGEOUT001`、`EDGEPLCOWN001`、`EDGEASYNC001/002`；`WSARCH001` 由同次 build 的 project-graph gate 执行。AnalyzerTests 当前 51 条，隔离 build fixture 当前 2 个正例、5 个反例。其余 Rule ID 仍是后续实施范围，不得把本次语义门禁扩张解读为 Persistence 事务、插件包隔离或全部测试物理拆分已完成。

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
| 插件仓自有 WorkflowTests | Workflow | 由插件业务契约确定 | 具体工序采集、门禁、上传、补偿和恢复；不属于宿主测试项目模板或工作区统一硬前置 |
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

- `RepositoryHygieneTests` 已删除 4 条被 Analyzer/project-graph 等价或更强覆盖的 layer/project/plugin metadata case；余下安全/部署/UI/source golden 动态事实继续按 Security/Deployment/UI/Golden 物理迁移，旧类型尚未整体删除。
- `ArchitectureBoundaryContractTests` 已物理删除；其中 6 条 layer/plugin/outbound 源码 Regex case 已由 Analyzer/project graph 取代，7 条仍需文件系统或运行时事实的 case 迁入 `ModuleDynamicConformanceTests`。
- `NonUiRegressionTests` 当前 590 case：按 Aggregate/Application/Persistence/Workflow/Contract 真实依赖拆分，`RegressionId` 保留迁移对账。
- `Shell.Tests` 当前 207 case：Architecture、Startup、UI、Workflow 继续分开；不再新增仓库卫生正则 case。
- Update、Installer、Launcher、UI.Shared：保留正确项目职责，跨职责 case 逐条迁移；功能退役引起的专属 case 删除按 before/after inventory 说明，通用回归不得随之丢失。

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

红灯用例先在隔离 candidate worktree 中对旧生产实现运行并保留失败输出和命令；不得把故意失败的提交推入 main。功能退役和生产修复完成后，按真实剩余 project/test/count 建立 after inventory，并执行全部 required runner 与 0 Skip 对账。

## 10. 测试清单与计数的单维护者演进

当前清单记录 32 个 solution/仓库项目、8 个 required 测试项目和 1 个非生产插件 fixture；当前 Release 真实发现数为 Analyzer 51、Update 17、Installer 15、Launcher 112、Module Contract 53、NonUI 590、Shell 207、UI Shared 14，合计 1059。清单只表达当前可执行事实，不冻结源码正文、程序集哈希、所有权或审批关系。

项目或测试资产合法演进时：

1. 记录当前任务授权、允许路径、开始 commit、完整 before inventory 和既有脏改动。
2. 准备候选并复核完整 diff；生产语义、测试迁移和机械 inventory/count 更新在记录中分别列明。
3. 运行 Release build、真实 discovery、全部 required runner 和 0 Skip 对账。
4. 生成 after inventory，逐项说明项目和 runner 的增减原因；计数下降必须能映射到物理删除或已说明的测试归口变化。
5. 已退役功能的专属测试可以删除；通用插件、DataPipeline、启动和安全 RegressionId 必须证明仍有有效覆盖。
6. `required-test-counts.json` 不得由普通 CI 运行中自动重写；只有本批候选经完整验证后才显式更新。
7. 只有当前任务明确授权时才 commit、push 或修改 workflow/远端设置。

## 11. 实施批次与退出条件

1. **中性插件测试 seam**：宿主/SDK 只使用 TestPlugin 验证发现、装载、真实后台服务 start/stop、DI release/dispose、capture → `EnqueueAsync` 返回 → callback、入队等待取消、UI 注册和包隔离；通用生命周期不得借用具体工序。真实 `DataPipelineService + ProcessQueueTask` 另行验证 accepted record 到 durable consumer 完成、runtime 取消时 active/queued 两项均结清且不补偿，以及 provider 自取消按失败只补偿一次；取消 drain 只清理内存 outlet/pending，不证明 shutdown durable。`IDataPipelineService` 没有下游完成句柄，禁止把两层拼成一个同步 completion contract；具体工序测试不代表宿主门禁。fixture 必须保持 `IsPackable=false` 且不进入任何生产 catalog/bundle/release。
2. **EDGE-ARCH-001（已完成本批启用范围）**：Edge 专属 Analyzer/AnalyzerTests、project graph 和 owner registry 已建立；17 个 compiler diagnostics 默认 error，`WSARCH001` 项目图无环门禁与 2 正/5 反真实 build fixture 已进入两份 Windows required CI。未启用 Rule ID 留在后续批次，不冒充完成。
3. **EDGE-PERSIST-001/002**：先建立跨调用方污染、commit 失败恢复、replace 原子性、cascade policy 等确定性红测，再以显式一次性 UoW session 修复。
4. **EDGE-BOUNDARY-002 / EDGE-PLUG-CON-001**：收口 Panels/SDK/async/schema/navigation 越界，建立 manifest/load/dependency/capability/ViewId/package conformance。
5. **EDGE-TEST-PHYSICAL-001+ / EDGE-QUALITY-001**：物理迁移测试并建立 duplication、coverage、mutation ratchet；每批对账 discovery、runner、Skip 和 RegressionId。
6. **EDGE-COMPAT-001**：清点并删除没有真实 consumer 或已经到期的旧 alias、adapter、wrapper、fallback、双写和影子路径；不得以 `Legacy/Compat` 目录或专门兼容测试延续死代码。
7. **最终终审**：Windows Release build/test、全部 required runner、annotations、artifact 和 25 分钟 p95；禁止以无说明删测、Skip、过滤或放宽断言换绿。

宿主仓与插件仓拆分属于用户下一份独立计划，不是本契约当前实施批次、依赖或进度项。

## 12. 非声明范围

- 本契约不改变 PLC/MES/Cloud、设备身份、生产数据、UI、配置、数据库或部署行为。
- 本契约不证明当前 EF transaction、插件包隔离或启动链已经正确；它明确登记了必须由后续失败测试关闭的风险。
- `ArchitectureBoundaryContractTests` 旧入口已在等价或更强 semantic gate 落地后物理删除且源码零引用；`RepositoryHygieneTests` 仍只保留未完成物理归口的动态事实，不得再新增 Analyzer 可证明的正则边界 case。
- 本契约不授权部署、发布、生产数据操作、创建新仓库或修改 remote。
