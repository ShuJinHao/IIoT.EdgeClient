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

当前 solution 与仓库项目图均为 61 个项目，其中 32 个是 required 测试 runner，1 个是位于 `src/Testing` 的中性插件 fixture，Analyzer 实现项目不是测试项目。`IIoT.Edge.Module.TestPlugin.Companion` 是该 fixture artifact 的 plugin-owned TestSupport，不是第二个 fixture，也不计入 32 个 required runner。当前数量不得由历史文档回填。生产项目不得直接或传递引用 Tests/Testing/TestKit；`Host.Bootstrap -> Presentation.VisualTestData` 仅在 Debug 生效，Release 图不得包含该边。项目数只是 inventory，不是永久冻结；任何增减必须同时更新清单、构建图、真实发现数和滚动复盘。

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
| Test | 当前 32 个 `src/Tests` required runner；物理迁移时必须同步更新 inventory 与真实 runner 对账 |
| Test fixture | `src/Testing/IIoT.Edge.TestPlugin`；只用于测试构建与 staging，禁止成为生产发布输入 |
| TestSupport | `IIoT.Edge.Testing.*` 及 `IIoT.Edge.Module.TestPlugin.Companion`；不包含 case，后者只能随中性 fixture staged artifact 消费 |

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
- Write/commit：`EdgeUnitOfWorkFactory` 为每个 session 创建独占 `EdgeDbContext` 与 transaction，`EfUnitOfWorkRepository<T>` 只在该 session 内跟踪 Add/Update/Delete，只有 `EdgeUnitOfWork.FlushAsync`/`CommitAsync` 可调用 `DbContext.SaveChangesAsync`。旧 `EfRepository<T>`、repository `SaveChangesAsync`/`ExecuteDeleteAsync`/replace 端口和 open-generic 写 repository DI 已物理删除。
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

### 4.4 已落地的写事务边界

当前写路径使用显式、一次性、禁止 ambient/`AsyncLocal` 的 Unit of Work session：

- `IEdgeUnitOfWorkFactory.BeginAsync(ct)` 创建独占一个 `EdgeDbContext` 和一个 provider-supported non-deferred SQLite transaction 的 `IEdgeUnitOfWork : IAsyncDisposable`。
- 写 repository 只能由 `IEdgeUnitOfWork.Repository<T>()` 获取；删除 open-generic `IRepository<>` 的直接 DI 注册，Application handler 不得跨 session 缓存写 repository。
- session 可提供 `FlushAsync(ct)` 仅用于同一事务内生成 identity；它不是 durable commit，其他 connection 不得看到，session 未 `CommitAsync` 即 Dispose/取消必须整体 rollback。
- `CommitAsync` 只能成功一次。Save/commit 失败后 session 进入 faulted，使用 `CancellationToken.None` 尝试 rollback；rollback 再失败时只通过 `Exception.Data["IIoT.Edge.Persistence.RollbackException"]` 附着证据，不覆盖主失败。禁止盲目重试或返回成功；缓存失效、PLC stop/reload 和成功文案只能在 commit 成功后发生。
- SystemConfig、TaskBinding 的 delete+insert 以及 Hardware 的 Network/Serial/IO 必须在同一 session/transaction 中完成；小配置集使用同 context query + tracked remove/add，不用另一个 context 的 bulk delete 绕过事务和 ChangeTracker。
- `Update` 继续按主键 load 后 `CurrentValues.SetValues`，不得退化为 `DbContext.Update`，避免不存在记录被插入或 navigation graph 被错误追踪。
- Stateless `IReadRepository<T>` 保持独立只读端口，泄漏 context 的 `GetQueryable()` 已物理删除；任何跨多步一致性读取必须从当前 UoW session 获取。
- 每个 UoW connection 在 transaction 前设置 `PRAGMA foreign_keys=ON` 和 busy timeout，并由真实临时 SQLite 测试验证连接隔离、并发串行、flush 后 rollback、跨聚合 commit、外键、replace rollback 和主异常/rollback 异常优先级。
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

当前宿主加载边界允许 `entry + plugin-owned assembly/resources/config + 声明的非宿主依赖`。中性正例使用常规插件命名的 `IIoT.Edge.Module.TestPlugin.Companion`：入口程序集真实引用 companion 类型，触发时必须由同一 plugin ALC 从 staged 目录加载。预检使用 PE metadata 校验引用，并精确拒绝当前已知 Host、Infrastructure、非共享 Presentation、Shell/Launcher/Installer/RuntimeLayoutSync 程序集；不得通配拒绝所有 `IIoT.Edge.Module.*`，也不得回退到默认 ALC、测试输出根或源项目 `bin`。生产打包仍需静态 pack allowlist 收紧全量 module build output，真实包内容由 `EDGEPLUGCON001` 持续阻断验证。

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
| `PLUG005` | 插件硬件/样例契约必须经 module builder，Cloud uploader 必须使用标准通道基类 | 已启用 error；普通插件私有服务注册为正例 |
| `PLUG006` | 静态 pack item/metadata/dependency allowlist | 改打包入口后 error |
| `EDGEOUT001` | 模块 Task 禁直接 outbound，允许 DataPipeline | 已启用 error |
| `EDGEOUT002` | 模块 Task 必须在任务边界处理 DataPipeline 入队异常 | 已启用 error |
| `EDGECOMP001` | 已物理删除且无真实调用方的兼容契约不得回流 | 已启用 error |
| `EDGECLOUDCFG001` | 生产 C# 的 Cloud API 路由只能来自配置快照 | 已启用 error |
| `EDGEPLCOWN001` | PLC driver/transport 只在登记 owner 构造/持有/释放 | 已启用 error |
| `EDGEASYNC001` | 禁同步等待 Task | 已启用 error；已删除 Cloud endpoint 配置读取的真实阻塞路径 |
| `EDGEASYNC002` | 禁非事件 `async void` | 已修 Installer helper 并启用 error |
| `EDGEPRES001` | Presentation 禁止定义/发送 MediatR request 或 handler，事件 notification handler 可保留 | 已启用 error；用例必须回归 Application |
| `EDGEPRES002` | Presentation 禁止直接构造可见中文 `ValidationIssue` 文案 | 已启用 error；必须使用稳定 key/本地化边界 |
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

当前实现有 23 个默认 error compiler diagnostics：`WSARCH003/004`、`DDD001/004/007`、`DATA001/002/005/006`、`PLUG001/002/003/004/005`、`EDGEOUT001/002`、`EDGECOMP001`、`EDGECLOUDCFG001`、`EDGEPLCOWN001`、`EDGEASYNC001/002`、`EDGEPRES001/002`；`WSARCH001` 由同次 build 的 project-graph gate 执行。`AnalyzerReleases.Shipped.md` 与 `AnalyzerReleases.Unshipped.md` 是 compiler ID catalog，机器门禁必须校验它们恰好 23 个唯一 `IIoT.Architecture/Error` ID，并与 `EdgeArchitectureDiagnostics.Create`、AnalyzerTests 读取到的 `SupportedDiagnostics` 精确集合相等；禁止由诊断家族 Regex 猜测。AnalyzerTests 当前 160 条，隔离 build fixture 当前 7 个正例、43 个反例，另有 2 项 CLI/图路径 bypass check。Analyzer 项目自排除必须比较 `System.IO.Path.GetFullPath` 产生的规范化绝对项目身份，禁止用未经规范化的拼接路径或项目名排除；隔离 build fixture 必须锁定唯一 canonical 表达式，并证明 Analyzer 项目没有 self `ProjectReference`，避免 Windows 路径分隔符差异形成 restore 环。Task 到 outbound/DataPipeline sink 的 invocation graph 必须穿透 helper/interface/override/delegate；委托参数按调用点/路径传播，且 simple/compound assignment 必须同时捕获 local/field/property/parameter，批准的外部 method/constructor delegate 参数仍跟踪实际 callback，未知来源统一 fail-closed。只有仓外不可达且全部 source incoming delegate 参数完整绑定的普通非虚 helper 可跳过独立未知根；public、constructor、interface、override、virtual 及其他外部可达入口始终独立 fail-closed。source lambda/local function 使用 tree+span 身份，metadata symbol 使用程序集+限定身份，不能因同名匿名函数或不同调用点碰撞。Application、ModuleSdk、SharedKernel 外部边界除精确 allowlist 外对 `Task`、`void`、同步值和 custom awaitable 统一 fail-closed，无法解析的非 Task delegate 也不例外；`catch` 后重抛、恒 false filter 或非通用异常捕获不能伪装为已处理。Analyzer 必须对 generated code 执行 `Analyze | ReportDiagnostics`，所有 descriptor 必须 `NotConfigurable`。project graph 必须以 `-Force` 扫描根/嵌套隐藏 `.editorconfig`/`.globalconfig`、evaluated Analyzer config items，以及仓内 `.csproj/.props/.targets/.proj` 的 raw/inactive/imported/target-time PropertyGroup；`RunAnalyzers=false`、`RunAnalyzersDuringBuild=false`、架构 ID 的 `NoWarn`/`WarningsNotAsErrors`、任何 IIoT.Architecture ID/category severity 和多 ID `#pragma` 都必须以 `WSARCH006` fail-closed。系统 `SuppressMessageAttribute` 使用 Roslyn semantic model 精确识别；对含候选的项目把每个 `#if/#elif` 条件替换为独立扫描符号，在有界穷举内覆盖 active、inactive、`false` 和矛盾结构分支，超过上限直接失败。跨项目 const 导致 constructor symbol 不完整时必须由 exact attribute type 识别，并对 unresolved 参数 fail-closed；裸名、Attribute 后缀、全限定名、local/global alias 均不得绕过，同名 fake type/namespace alias 及不可满足 fake 分支不得误报。`bin/obj` 排除必须相对当前 `RepositoryRoot`，不得因 fixture 物理位于上层 `obj` 而漏扫。其余 Rule ID 仍是后续实施范围，不得把本次语义门禁扩张解读为生产 pack allowlist 已完成。

## 8. Edge 当前测试分类与物理归口

`Regression` 只是 cross-cutting `RegressionId`，不是 TestKind 或物理项目。当前机器真值为 61 个 solution 项目、32 个 required runner、1 个中性插件 fixture，Release 发现 1275 case。Unit 只允许 `Pure + Parallel`；其他 Pure runner 也受控并行；Filesystem、SQLite、Avalonia 和 Windows runner 必须物理命名且串行。

| Required runner | TestKind | Runtime / mode | Cases |
|---|---|---|---:|
| `IIoT.Edge.Application.Tests` | Application | Pure / Parallel | 47 |
| `IIoT.Edge.Architecture.AnalyzerTests` | Architecture | Pure / Parallel | 160 |
| `IIoT.Edge.Architecture.Tests` | Architecture | Filesystem / Serial | 5 |
| `IIoT.Edge.Caching.UnitTests` | Unit | Pure / Parallel | 12 |
| `IIoT.Edge.Cloud.ContractFilesystemTests` | Contract | Filesystem / Serial | 6 |
| `IIoT.Edge.Cloud.ContractTests` | Contract | Pure / Parallel | 87 |
| `IIoT.Edge.Deployment.Tests` | Deployment | Filesystem / Serial | 8 |
| `IIoT.Edge.DeviceBootstrap.IntegrationTests` | Integration | Filesystem / Serial | 9 |
| `IIoT.Edge.Domain.Tests` | Aggregate | Pure / Parallel | 22 |
| `IIoT.Edge.Excel.IntegrationTests` | Integration | Filesystem / Serial | 3 |
| `IIoT.Edge.Installer.UiTests` | UI | Avalonia / Serial | 2 |
| `IIoT.Edge.Installer.UnitTests` | Unit | Pure / Parallel | 6 |
| `IIoT.Edge.Launcher.FilesystemTests` | Integration | Filesystem / Serial | 67 |
| `IIoT.Edge.Launcher.UiTests` | UI | Avalonia / Serial | 13 |
| `IIoT.Edge.Launcher.UnitTests` | Unit | Pure / Parallel | 33 |
| `IIoT.Edge.Mes.ContractTests` | Contract | Pure / Parallel | 23 |
| `IIoT.Edge.Module.ConformanceTests` | Conformance | Filesystem / Serial | 59 |
| `IIoT.Edge.Module.Homogenization.ConformanceFilesystemTests` | Conformance | Filesystem / Serial | 24 |
| `IIoT.Edge.Module.Homogenization.ConformanceTests` | Conformance | Pure / Parallel | 13 |
| `IIoT.Edge.Module.Homogenization.WorkflowFilesystemTests` | Workflow | Filesystem / Serial | 8 |
| `IIoT.Edge.Module.Homogenization.WorkflowTests` | Workflow | Pure / Parallel | 101 |
| `IIoT.Edge.Persistence.FilesystemTests` | Persistence | Filesystem / Serial | 10 |
| `IIoT.Edge.Persistence.Tests` | Persistence | SQLite / Serial | 32 |
| `IIoT.Edge.Platform.WindowsTests` | Deployment | Windows / Serial | 1 |
| `IIoT.Edge.Plc.ContractNetworkTests` | Contract | Network / Serial | 5 |
| `IIoT.Edge.Plc.ContractTests` | Contract | Pure / Parallel | 40 |
| `IIoT.Edge.Runtime.WorkflowTests` | Workflow | Pure / Parallel | 249 |
| `IIoT.Edge.Shell.FilesystemTests` | Integration | Filesystem / Serial | 32 |
| `IIoT.Edge.Shell.UiTests` | UI | Avalonia / Serial | 122 |
| `IIoT.Edge.Startup.IntegrationTests` | Integration | SQLite / Serial | 42 |
| `IIoT.Edge.UI.Shared.Tests` | UI | Avalonia / Serial | 17 |
| `IIoT.Edge.Update.ContractTests` | Contract | Filesystem / Serial | 17 |

`IIoT.Edge.Testing.Core`、`Testing.Homogenization`、`Testing.Modules` 和 `Testing.Protocols` 只承载共享测试能力，不包含 case；`IIoT.Edge.Module.TestPlugin.Companion` 只承载中性 fixture 自有类型，同样不包含 case。`IIoT.Edge.TestPlugin` 是 `IsPackable=false` 的唯一中性 fixture。旧 NonUI、Shell、Module Contract、Launcher、Installer、Update 大桶已迁空并物理删除；`RepositoryHygieneTests` 73 条与 `ArchitectureBoundaryContractTests` 13 条源文本大桶亦已物理删除，发现清单两类 identity 均为 0。历史基线固定为 1091 runner case / 964 声明，由 regression ledger 精确映射到当前 case、Analyzer、static gate、行为测试或已授权退役决策，unknown 必须为 0。

## 9. 已落地的高风险语义

- 通用插件生命周期只使用中性 `TestPlugin`，覆盖发现、装载、start/stop、DI release/dispose、capture → enqueue → callback 与取消。具体工序配置与打包契约只留在对应插件 runner。
- SQLite/Persistence 已物理分为 Filesystem 与 SQLite isolated runner，并落地第 4 节的显式 UoW；覆盖 session 隔离/串行、flush rollback、跨聚合 commit、一次提交、每连接 pragma、外键、replace rollback 和主异常优先级。
- DataPipeline 覆盖 accepted record 到 durable consumer、Cloud/MES active+queued 取消的逐项零丢失/零重复 shutdown 持久化、每通道单一总 deadline、存储成功返回后的 durable commit、停止钩子统一等待、记录出队后从开始/完成到失败/补偿/critical 降级的全部日志 best-effort、日志订阅者异常不丢记录/不改提交/不覆盖主异常、完整 critical payload 反序列化、critical 写失败 runtime fault、provider 自取消补偿、non-retryable exception、retry/fallback/deadletter 与 Cloud/MES 分离。MES HTTP 另以真实 in-flight handler 证明 caller cancellation 原样传播，transport/self-timeout 仍是普通失败。
- 启动链对缺配置、PLC/MES/Cloud 不可达、IO/module profile 问题保持非阻断；取消测试使用 `TaskCompletionSource`/barrier 后显式 `Cancel`，不用几十毫秒机器时钟碰运气。
- 同一启动批中的背景服务彼此独立，单服务失败后继续启动后续服务并汇总诊断；已 claim record 的 caller cancellation 必须先释放 claim 再原样重抛。PassStation retry 按 record 验证，混合批次保留有效源、只 deadletter 无效源；device-status endpoint 缺失保持 pending。
- 内存缓存覆盖 typed hit/miss、single-flight、null cache、expiration、factory failure 与 invalidation；内部只保留单一 `CacheEntry` 存储路径，无调用方的 raw-value 兼容分支已物理删除。

## 10. 清单、质量与兼容对账

- `edge-test-inventory.json`、`discovered-test-inventory.json` 和 `required-test-counts.json` 是唯一机器清单；普通 CI 只验证，不自动改写。
- required 执行必须每项目独立 TRX/coverage，最终证明 `discovered = trxTotal = executed = passed = 1275`、`failed = skipped = 0`。
- compatibility inventory 必须对 alias/adapter/wrapper/compat/legacy/shadow/obsolete/fallback/双写/影子候选逐项提供真实 consumer 证据与有期迁移窗口，并对每个真实声明精确登记 symbol/path/调用证据；宽 token disposition 不得掩护未登记 symbol。每个 `MigrationWindow` symbol 必须绑定唯一 window ID、replacement/deletion/latest removal 约束和逐 symbol 真实 coverage test；缺失/未知 window、token/path 不归属、无 coverage、零 consumer、新增 consumer、未分类或未登记候选直接失败。coverage test 必须是 required runner 中真实执行且 0 skipped 的行为测试，不得用空壳或注释代替。
- duplication 分 production/test-support/tests 的 exact/near ratchet；coverage 覆盖所有 32 runner，并对 `EdgeMemoryCacheService` 和 `DataPipelineNonRetryableException` 设高风险阈值；mutation 固定 Domain Aggregate + MTP report-only 范围与证据。
- 项目/case 变化必须先复核授权和完整 diff，再显式更新 inventory，并记录 before/after 原因。仅用户授权时才 commit、push 或修改远端。

## 11. CI 退出条件

Windows required CI 必须在 25 分钟 hard timeout 内完成 Release build、Analyzer/project graph、正反 build fixture、source-quality、inventory/discovery、32 runner 执行、TRX/Skip 对账、coverage、compatibility、duplication 和 regression ledger；mutation 使用独立 report job，不伪装成编译错误。当前 macOS 工作树的 32 runner/TRX 对账已实测全绿；Windows 远端 job 与 Windows 实机 Installer/Velopack/DPI 验收在取得对应证据前不得写成已验证。

宿主仓与插件仓拆分属于用户下一份独立计划，不是当前测试批次、依赖或进度项。`deploy/Deploy-Changed.ps1` 是生产 CD 入口，不属于 required 测试 CI；本批不运行发布或部署。

## 12. 非声明范围

- 本契约不改变 PLC/MES/Cloud、设备身份、生产数据、UI 或部署行为。
- 本批显式 UoW 关闭不等于第 5 节的插件包隔离和其他临时 seam 已修复；这些必须在各自生产批次独立关闭。
- 文件系统/源码动态事实已归入 `IIoT.Edge.Architecture.Tests`；Analyzer 可静态证明的边界不得新增重复 Regex case。
- 本契约不授权部署、发布、生产数据操作、创建新仓库或修改 remote。
