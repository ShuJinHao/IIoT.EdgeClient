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

物理拆分前冻结基线登记 61 个项目与 32 个 required runner。2026-07-22 本地三仓源码候选已完成 V1–V5：Host 当前为 50 个 solution project / 26 个 required runner / 1 个中性 fixture，SDK 为 6 / 2，Private Plugins 为 6 / 4；三仓 owner runner均已分别执行，Host inventory/discovery/TRX/coverage 已机械对账。该本地结论不包含 Phase 9/10、Windows 远端/实机、formal/ledger、发布或部署。生产项目仍不得直接或传递引用 Tests/Testing/TestKit；`Host.Bootstrap -> Presentation.VisualTestData` 仅在 Debug 生效，Release 图不得包含该边。

拆仓前 `EdgePluginContractLedgerTests` 的两个 Architecture Fact 绑定旧单仓 Homogenization 源路径与 Phase 0 canonical ledger/formal 输入，只属于冻结证据 worktree。新 Host required runner 不得复制旧 ledger、重写 generator 到跨仓源码、接入冻结 worktree或生成第二套 ledger；这两个 Fact 必须随旧单仓输入物理退役，并由 V1–V4 的 nupkg/bundle/manifest/digest 组合证据接替当前三仓边界证明。发现数变化必须在滚动复盘中精确解释并重新生成 Host owner 的 discovery/counts/TRX 真值。

角色不是仅按目录推断，当前登记如下：

| 角色 | 当前项目 |
|---|---|
| Domain/Core | `IIoT.Edge.Domain` |
| Application | `IIoT.Edge.Application` |
| SharedKernel | `IIoT.Edge.SharedKernel` |
| SDK Contracts | 独立 `IIoT.Edge.Sdk` 仓的 `IIoT.Edge.Module.Contracts` 包 |
| UI Shared | 独立 `IIoT.Edge.Sdk` 仓的 `IIoT.Edge.UI.Shared` 包 |
| Module SDK | 独立 `IIoT.Edge.Sdk` 仓的 `IIoT.Edge.Module.Sdk` 包 |
| Infrastructure | `CloudClient`、`DeviceComm`、`Integration`、`Persistence.Dapper`、`Persistence.EfCore`、`Update` |
| Presentation | `Presentation.Navigation`、`Presentation.Panels`、`Presentation.Shell`、`Presentation.VisualTestData` |
| Host/Tool | `Host.Bootstrap`、`Host.DataPipeline`、`Shell`、`Launcher`、`Installer`、`RuntimeLayoutSync` |
| Plugin SDK | Contracts + Module.Sdk，只提供通用 contract/基础能力，不承载具体工序 |
| Concrete plugin | 独立 `IIoT.Edge.Plugins.Private` 仓的 `IIoT.Edge.Module.Homogenization`；Host 无源码副本 |
| Analyzer | 独立 `IIoT.Edge.Sdk` 仓的 `IIoT.Edge.Module.Analyzers` 包；作为生产构建 Analyzer 引用 |
| Test | Host、SDK、插件各自拥有本仓测试；V5 已按 26 / 2 / 4 个 owner runner分别执行，禁止恢复单仓混合 owner |
| Test fixture | `src/Testing/IIoT.Edge.TestPlugin`；只用于测试构建与 staging，禁止成为生产发布输入 |
| TestSupport | Host 只保留通用 `IIoT.Edge.Testing.*` 与中性 fixture companion；`Testing.Homogenization` 已归私有插件仓并通过其四个 owner runner |

### 2.1 允许的直接依赖方向

| Source role | 允许的直接依赖 |
|---|---|
| Domain/Core | Host 内 `ProjectReference -> SharedKernel`；SDK contract 只经包传递或显式包引用 |
| Application | Host 内 `ProjectReference -> Domain/Core、SharedKernel`；`PackageReference -> Contracts、Module.Sdk` |
| SharedKernel | `PackageReference -> Contracts` |
| UI Shared | SDK 仓内独立项目；不得引用 Host，实现通过正式包交付 |
| Module SDK | SDK 仓内只 `ProjectReference -> Contracts`；不得引用 Host 或具体插件 |
| Infrastructure | Application；同层只允许登记的窄边，当前为 Integration/Update → CloudClient，Update 另可 → SharedKernel |
| Presentation | Host 内 Application、SharedKernel；`PackageReference -> UI Shared/Module SDK`；同层只允许 Navigation → Panels 的过渡边 |
| Host composition | Application、批准的 Infrastructure/Presentation/Shared/Host runtime contract；禁止具体 Plugin |
| Plugin implementation | 只允许 `PackageReference -> Contracts、Module SDK、UI Shared、Analyzer` 及公共第三方包；禁止任何 Host 项目/源码/程序集引用 |
| Tool | 默认无项目依赖；自定义 MSBuild orchestration 必须进入隐藏边 ledger |
| VisualTestData | 只能 → Application；只允许测试项目和精确 Debug-only Host 引用；Release closure/artifact 必须为零 |
| Test | 由目标 TestKind 项目自身的精确依赖矩阵决定，禁止用一个测试巨石获得所有生产层访问权 |

### 2.2 当前精确过渡边

以下不是层级通配，只批准当前精确边：

1. 原 `具体插件 -> Presentation.Navigation` 过渡 seam 已在本地候选关闭；UI 注册改走 `IIoT.Edge.Module.Contracts.UI` DTO 与 `IIoT.Edge.UI.Shared.PluginSystem`，不得恢复旧边。
2. `Host.Bootstrap -> Presentation.VisualTestData`：仅批准当前精确 Debug condition；任何 Release、无条件或传递进入 artifact 的形式均禁止。
3. `Launcher -> Shell`：仅批准 `ReferenceOutputAssembly=false` 的 build-only 边。
4. `Launcher -> RuntimeLayoutSync` 的自定义 `<MSBuild Projects=...>`、测试 plugin staging 的 MSBuild 边必须进入统一图和环检测，不能因不是 `ProjectReference` 而漏检。

### 2.3 已确认的项目/包技术债

- `Presentation.Panels` 持有具体 `log4net` provider 实现，属于 Presentation → Infrastructure 泄漏；迁移前只能精确过渡，禁止扩大为 Presentation 可任意引用 provider。
- 旧 Plugin -> Navigation/Panels 传递闭包已从生产插件源码移除；后续门禁应按包程序集身份证明该边保持为零。
- Application 的 `Microsoft.Extensions.Hosting.Abstractions` 当前无源码消费证据，应删除验证；删除前只列精确 legacy exception。
- Shell 的 `Microsoft.EntityFrameworkCore.Design` 应迁回 persistence/design-time owner；不得据此批准普通 Host 使用 EF API。
- 14 个生产项目存在 16 条 `InternalsVisibleTo`。必须建立精确 friend ledger；`IIoT.Edge.TestSimulator` 不在当前项目图中，是 stale friend，需删除。

## 3. 聚合边界裁决

EF navigation、`DbSet` 和 cascade 只表达 ORM 关系，不自动证明 DDD ownership。当前真实写入事务裁决如下：

| 类型 | 裁决 | 依据与约束 |
|---|---|---|
| `NetworkDeviceEntity` | 批准 AggregateRoot | 有独立保存命令和仓储。`IoMappings`、`PlcTaskBindings` 使用私有 backing field 和只读视图，EF 只通过字段完成关系 fix-up；外部不能经 navigation 修改集合。 |
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
- 插件开发样本只提交 `ModuleDevelopmentSeedRequest` DTO；Host 的 `ModuleDevelopmentSeedWriter` 是 DTO → Domain 唯一写入端口，同一请求只创建一个 UoW/transaction，delete 与 identity 允许 non-durable `FlushAsync`，最终只允许一次 `CommitAsync`。本地候选尚未运行 SQLite rollback/同 PlcCode 重建验证，状态保持 `NOT-VERIFIED`。
- Cloud 文件 projection 不放进 SQLite transaction。保留 fail-closed saga：数据库与文件任一步失败都不得报告成功，并以独立原子补偿恢复禁用状态。

## 5. 插件、Outbound 与 PLC Owner

### 5.1 插件 contract seam

- Plugin entry、Contracts、Module SDK、UI Shared、Analyzer 必须有显式包/角色 metadata；Analyzer 将 `IIoT.Edge.Module.Contracts` 与 `IIoT.Edge.Module.Sdk` 识别为 SDK 面，不能把 Contracts 误判成具体插件。
- 插件生产源码只允许正式 SDK 包及公共第三方依赖；Application、Domain、SharedKernel、Infrastructure、Presentation、Host 均禁止，Navigation 不再有过渡例外。
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

当前宿主加载边界允许 `entry + plugin-owned assembly/resources/config + 声明的非宿主依赖`，并把 `IIoT.Edge.Module.Contracts`、`IIoT.Edge.Module.Sdk`、`IIoT.Edge.UI.Shared` 作为 Host 提供的共享程序集。中性正例继续使用 `IIoT.Edge.Module.TestPlugin.Companion`；中性 fixture 本身只消费正式 SDK 包与 plugin-owned companion，两个 Host staging owner 的实际 artifact 均不含 Application、Domain 或 SharedKernel。预检以 PE metadata 精确拒绝 Application、Domain、SharedKernel、Host、Infrastructure、Presentation、Shell/Launcher/Installer/RuntimeLayoutSync，不得回退到默认 ALC、测试输出根或源项目 `bin`。插件仓唯一 `eng/PackEdgePlugin.ps1` 已由 V3/V4 以静态 allowlist 生成包并证明排除 SDK/Host DLL；Phase 9/10 发布组合仍未执行。

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
| `DDD002` | 批准 aggregate 不暴露 public setter/可变集合 | 当前 NetworkDevice 两集合已由 required Domain 行为测试证明声明类型和运行时视图均不可写；通用静态规则仍属后续实施范围，不保留实体例外 |
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
| `PLUG001` | 插件禁止 Application/Domain/SharedKernel/Host/Infrastructure/DataPipeline/Presentation 实现 | V1–V4 已验证包级与 runtime边界；不得恢复例外 |
| `PLUG002` | 具体插件禁止互引，禁止插件族 Shared 业务工程 | 已启用 error；通用能力只能进入 SDK/contract |
| `PLUG003` | Host/Application/Core/Shared/Infra/Presentation 禁具体插件 symbol | 已启用 error，动态发现全部入口 |
| `PLUG004` | Contracts/SDK/UI/Analyzer/插件角色、manifest、identity metadata 完整 | V1–V5 已完成本地包、Analyzer/project-graph/runtime验证 |
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

当前实现有 23 个默认 error compiler diagnostics：`WSARCH003/004`、`DDD001/004/007`、`DATA001/002/005/006`、`PLUG001/002/003/004/005`、`EDGEOUT001/002`、`EDGECOMP001`、`EDGECLOUDCFG001`、`EDGEPLCOWN001`、`EDGEASYNC001/002`、`EDGEPRES001/002`；`WSARCH001` 由同次 build 的 project-graph gate 执行。Analyzer 源码、`EdgeArchitectureDiagnostics.Create`、release docs、`SupportedDiagnostics` 对账与当前 160 条 AnalyzerTests 由 SDK 仓独占；Host 只消费固定 `2.0.0` `IIoT.Edge.Module.Analyzers` 包。Host 机器门禁必须从已解析包内的 `AnalyzerReleases.Shipped.md`、`AnalyzerReleases.Unshipped.md` 与 Analyzer DLL读取并校验恰好 23 个唯一 `IIoT.Architecture/Error` ID，禁止保留第二份源码真值或由诊断家族 Regex 猜测。根 `Directory.Build.targets` 必须向每个生产项目注入唯一的 `PrivateAssets=all`、`GeneratePathProperty=true`、包含 `analyzers` asset 的 PackageReference，测试与显式插件 fixture 排除；任何 Analyzer 源码 `ProjectReference` 必须 `WSARCH006` fail-closed。隔离 build fixture 当前 7 个正例、43 个反例，另有 2 项 CLI/图路径 bypass check；fixture 必须锁定包 metadata、解析后包根和 Shell project-graph 参数。Shell 与隔离 fixture 的 project-graph `Exec` 传给子进程的仓根和 solution 都必须求值为 canonical 绝对路径，目录参数不得带尾随分隔符，避免 Windows 反斜杠转义闭合引号；Shell 固定主仓路径不得改成可由 CLI 覆盖的属性而形成图路径旁路，fixture root 必须在规范化后精确去尾且继续只对显式 fixture role 生效。Task 到 outbound/DataPipeline sink 的 invocation graph 必须穿透 helper/interface/override/delegate；委托参数按调用点/路径传播，且 simple/compound assignment 必须同时捕获 local/field/property/parameter，批准的外部 method/constructor delegate 参数仍跟踪实际 callback，未知来源统一 fail-closed。只有仓外不可达且全部 source incoming delegate 参数完整绑定的普通非虚 helper 可跳过独立未知根；public、constructor、interface、override、virtual 及其他外部可达入口始终独立 fail-closed。source lambda/local function 使用 tree+span 身份，metadata symbol 使用程序集+限定身份，不能因同名匿名函数或不同调用点碰撞。Application、ModuleSdk、SharedKernel 外部边界除精确 allowlist 外对 `Task`、`void`、同步值和 custom awaitable 统一 fail-closed，无法解析的非 Task delegate 也不例外；`catch` 后重抛、恒 false filter 或非通用异常捕获不能伪装为已处理。Analyzer 必须对 generated code 执行 `Analyze | ReportDiagnostics`，所有 descriptor 必须 `NotConfigurable`。project graph 必须以 `-Force` 扫描根/嵌套隐藏 `.editorconfig`/`.globalconfig`、evaluated Analyzer config items，以及仓内 `.csproj/.props/.targets/.proj` 的 raw/inactive/imported/target-time PropertyGroup；`RunAnalyzers=false`、`RunAnalyzersDuringBuild=false`、架构 ID 的 `NoWarn`/`WarningsNotAsErrors`、任何 IIoT.Architecture ID/category severity 和多 ID `#pragma` 都必须以 `WSARCH006` fail-closed。系统 `SuppressMessageAttribute` 使用 Roslyn semantic model 精确识别；对含候选的项目把每个 `#if/#elif` 条件替换为独立扫描符号，在有界穷举内覆盖 active、inactive、`false` 和矛盾结构分支，超过上限直接失败。跨项目 const 导致 constructor symbol 不完整时必须由 exact attribute type 识别，并对 unresolved 参数 fail-closed；裸名、Attribute 后缀、全限定名、local/global alias 均不得绕过，同名 fake type/namespace alias 及不可满足 fake 分支不得误报。`bin/obj` 排除必须相对当前 `RepositoryRoot`，不得因 fixture 物理位于上层 `obj` 而漏扫。其余 Rule ID 仍是后续实施范围，不得把本次语义门禁扩张解读为生产 pack allowlist 已完成。

## 8. Edge 当前测试分类与物理归口

`Regression` 只是 cross-cutting `RegressionId`，不是 TestKind 或物理项目。拆仓前冻结 inventory 为 61 个 solution project、32 个 required runner、1 个中性 fixture、1334 个 Release case；其中两个 canonical-ledger Architecture Fact 只绑定冻结 Phase 0 证据拓扑，不能复制到产品 Host。2026-07-22 V5 当前 owner 真值如下；Unit 只允许 `Pure + Parallel`，其他 Pure runner 也受控并行，Filesystem、SQLite、Avalonia 和 Windows runner 必须物理命名且串行。

| Owner | 项目 / required runner | 当前机器真值 |
|---|---:|---|
| Host | 50 / 26 | `edge-test-inventory.json`、`discovered-test-inventory.json`、`required-test-counts.json` 与 26 份 TRX/coverage report 对齐；Host discovery 为 1062，failed=0、skipped=0 |
| SDK | 6 / 2 | AnalyzerTests 与 UI.Shared.Tests 分别由 SDK 仓执行，均 failed=0、skipped=0 |
| Private Plugins | 6 / 4 | Homogenization Conformance、ConformanceFilesystem、Workflow、WorkflowFilesystem 由插件仓执行，均 failed=0、skipped=0 |

Host 每个 runner 的当前精确发现数只以 `scripts/tests/required-test-counts.json` 为机器真值，不再在本文复制第二份易漂移表。`IIoT.Edge.Testing.Core`、`Testing.Modules` 和 `Testing.Protocols` 只承载 Host 通用测试能力；`Testing.Homogenization` 只归 Private Plugins；`IIoT.Edge.Module.TestPlugin.Companion` 只承载中性 fixture 自有类型，均不包含 case。`IIoT.Edge.TestPlugin` 是 `IsPackable=false` 的唯一中性 fixture，其项目图只消费 SDK 包与 plugin-owned companion。旧 NonUI、Shell、Module Contract、Launcher、Installer、Update 大桶已迁空并物理删除；具体插件源码质量 Fact 也随源码归插件现有 runner，禁止在 Host 保留旧路径副本。

## 9. 已落地的高风险语义

- 通用插件生命周期只使用中性 `TestPlugin`，覆盖发现、装载、start/stop、DI release/dispose、capture → enqueue → callback 与取消。插件根配置含 NUL 或无法规范化时必须跨平台一致拒绝并非阻断回落默认根，不能接受 Windows 原生路径截断后的残缺目录。具体工序配置与打包契约只留在对应插件 runner。
- Shell 运行数据根等启动期配置路径同样必须在平台路径 API 前及环境变量/token 展开后显式拒绝 NUL；无效值只产生 `RUNTIME_DATA_ROOT_INVALID` 等诊断并回落 profile 默认目录，不能利用 Windows/Unix 对非法字符的不同处理形成静默路径漂移或 fatal 启动。
- 启动 filesystem/path adapter 的批准翻译集合精确为 `ArgumentException`、`NotSupportedException`、`IOException`、`UnauthorizedAccessException`、`SecurityException`；未知异常与直接 OCE 原实例传播。插件 manifest/load 使用两类显式 typed exception，批准的 JSON/reflection/assembly/filesystem 失败包装时保留 inner exception；配置层不得再外包 broad catalog catch，catalog 激活只处理 `ModulePluginLoadException`，独立有效插件继续装载。resolver/preflight 的 internal seam 只服务确定性测试，生产默认绑定真实 API，所有 probe/loader 单次执行。
- SQLite/Persistence 已物理分为 Filesystem 与 SQLite isolated runner，并落地第 4 节的显式 UoW；覆盖 session 隔离/串行、flush rollback、跨聚合 commit、一次提交、每连接 pragma、外键、replace rollback 和主异常优先级。
- DataPipeline 覆盖 accepted record 到 durable consumer、Cloud/MES active+queued 取消的逐项零丢失/零重复 shutdown 持久化、每通道单一总 deadline、存储成功返回后的 durable commit、停止钩子统一等待、记录出队后从开始/完成到失败/补偿/critical 降级的全部日志 best-effort、日志订阅者异常不丢记录/不改提交/不覆盖主异常、完整 critical payload 反序列化、critical 写失败 runtime fault、provider 自取消补偿、non-retryable exception、retry/fallback/deadletter 与 Cloud/MES 分离。MES HTTP 另以真实 in-flight handler 证明 caller cancellation 原样传播，transport/self-timeout 仍是普通失败。
- 启动链对缺配置、PLC/MES/Cloud 不可达、IO/module profile 问题保持非阻断；取消测试使用 `TaskCompletionSource`/barrier 后显式 `Cancel`，不用几十毫秒机器时钟碰运气。
- 同一启动批中的背景服务彼此独立，单服务失败后继续启动后续服务并汇总诊断；已 claim record 的 caller cancellation 必须先释放 claim 再原样重抛。PassStation retry 按 record 验证，混合批次保留有效源、只 deadletter 无效源；device-status endpoint 缺失保持 pending。
- 内存缓存覆盖 typed hit/miss、single-flight、null cache、expiration、factory failure 与 invalidation；内部只保留单一 `CacheEntry` 存储路径，无调用方的 raw-value 兼容分支已物理删除。

## 10. 清单、质量与兼容对账

- `edge-test-inventory.json`、`discovered-test-inventory.json` 和 `required-test-counts.json` 是唯一机器清单；普通 CI 只验证，不自动改写。
- `EDGE-SPLIT-LEDGER-001` 的唯一 canonical 生成器是 `eng/Generate-EdgePluginContractLedger.ps1`；它必须以真实 MSBuild `Compile`/`ReferencePath`、evaluated `ProjectReference`、Roslyn semantic model、候选程序集 PE `AssemblyRef` 和候选插件 zip 生成四层独立依赖账本，并绑定 artifact、View/Page、resource、已发布历史组合和全部输入 SHA-256/size。每层必须记录完整输入/items、owner family、total/forbidden/unknown 计数，summary 再记录四层 forbidden count；strict schema 与 validator 必须分别拒绝缺字段、未知字段和伪造 count。Phase 0–3 的 package 层必须显式为 `not-applicable-before-EDGE-SPLIT-040`，不能伪装为 evaluated zero；Phase 4–5 必须读取真实 zip 并逐 entry hash/size 对账。`unknownAssemblies`、`unclassifiedSymbols` 必须恒为 0；`EDGE-SPLIT-020` 与 `EDGE-SPLIT-030` carry set 以 Phase 0 exact item/count 冻结，随后只允许 retained-exact 或在目标批次 closed/zero，禁止增长、替换身份或由并行脚本、namespace grep、手工 JSON 形成第二真相源。每个 Phase 使用 implementation/evidence commit pair：只在 clean implementation commit 生成 ledger，仅排除 canonical ledger 路径，随后恰好一个 ledger-only evidence commit；final canonical validator 必须证明该 commit 的唯一 parent 是 recorded implementation HEAD、唯一差异是 ledger 且最终工作树完全 clean，禁止因 commit hash 自引用而记录虚假 clean/final HEAD。noncanonical/pending smoke 不是 final 证据。project/source/test/manifest/view/resource 的当前数量是可重算观测值，不是跨 Phase 永久常量。
- schema v2 的完整 semantic-evidence identity 包含源路径/位置、精确 symbol、symbol kind、完整 owner assembly identity 和 usage kind；carry item 按 `sourcePath + ownerAssembly + symbol` 聚合，`count`/occurrence 等于该身份的底层语义证据记录数。stale schema v1 的窄语法/调用次数不得继续作为 carry 真值。审查通过的 schema v2 候选为 `EDGE-SPLIT-020` 22 exact items / 122 semantic-evidence occurrences，`EDGE-SPLIT-030` 6 个私有 Presentation exact items / 11 semantic-evidence occurrences；仅 final canonical evidence 完成后才正式冻结。`IIoT.Edge.UI.Shared` 为 approved stable SDK UI surface，候选账本的 34 条 UiShared usages 不是 Phase 3 carry；Phase 3 只归零私有 Presentation seam，同时对 UiShared 稳定契约做 package/API/runtime 边界对账。
- Phase 0 formal closure 的唯一顺序是 `I（全部实现/测试/schema/tracked docs） → clean I 上唯一 generator → E（唯一 direct parent 为 I，且只改 canonical ledger） → E 上无参数 actual formal`。`scripts/tests/Invoke-EdgePluginContractFormalValidation.ps1` 必须保持 `param()`、固定仓根与 `eng/baselines/edge-plugin-contract-ledger.json`，不得暴露 ledger/root/command/fixture/skip 等 override，也不得调用 generator；`E` 之后禁止再补写 tracked 文档或代码，任何 tracked 漂移都使该证据链失效并要求重新形成 I/E。
- formal precondition 必须精确证明：worktree 与 index 完全 clean；ledger 的 `sourceState` 绑定 `I` 的真实 HEAD/tree、`cleanObserved=true`、`dirtyPaths=[]` 且唯一 `excludedPaths` 为 canonical ledger；当前 `E` 是 `I` 的唯一直接子提交，parent inventory 只有 `I`，`I..E` 距离为 1，唯一 diff path 为 canonical ledger，tree entry mode 为 `100644`，committed blob 与 worktree ledger 原字节恒等。相同 precondition 必须在启动 authority 前执行两次并逐字段相等，不能用第一次结果跨越并发漂移。
- actual formal 必须只启动一个 direct-child coordinator，由其产生 authority/replay `1/1` 的签名临时机器事实；descriptor 必须绑定 direct-child PID/start、I、E、canonical ledger 与收据 digest。正式收据验证和 fast consumer 必须分别保持 `RequireFormal` 以及 `-RequireAuthorityReceipt -RequireFormalAuthorityReceipt`，随后复核 repository post-state；受控 run root 只能按已知 marker/file allowlist 做 non-recursive exact cleanup，未知项、symlink/reparse、身份漂移或 recursive cleanup 都 fail-closed，cleanup 后再次复核仓库与收据原字节身份。
- formal 成功输出必须通过独立 `eng/edge-plugin-contract-formal-validation-result.schema.json` 的 strict schema v1：未知字段拒绝，`mode=formal-clean`、`formal=true`、`passed=true`、I/E、ledger/receipt/public-key digest、authority/replay `1/1`、双 descriptor direct-child binding、两个 Require switches、`postStateStable=true` 与 `cleanupComplete=true` 均为固定事实。新增该 schema 不得改写既有 request/descriptor/receipt/authority-result 四份 schema v1 的字节或语义；`cleanupComplete` 不表示删除 final receipt，收据保留为本次机器验证输出。
- static owner 的正式输入集合必须为 11 个受审源，formal 专属 mutation 必须保持 `10/10`，formal-result schema 必须以 `receiptPath='.artifacts/../x'` 的 path-traversal 负例保持 `1/1`，既有 lifecycle/protocol mutation 库保持 `87 + 22 + 3 = 112`，不得用 formal 新库替换、重命名或少算 legacy 库。上述数字是待真实入口执行并核对的机器契约，不是文档写入即产生的运行证据；AuthorityProtocol 的 synthetic formal fixture 只能证明 schema、签名绑定和 fail-closed ownership，绝不能替代 E 上的无参数 actual formal。
- `Test-EdgePluginContractLedgerBehavior.ps1` 必须在任何 behavior mutant 之前 fail-closed 执行 `Test-EdgePluginContractLedgerPrimitives.ps1` 并传播非零退出；required Architecture xUnit 必须同时断言 primitives 和 behavior 通过标记，不得只在文档列独立命令或依赖维护者手动补跑。
- required 执行必须每项目独立 TRX/coverage。历史冻结 inventory 的 1334 只适用于冻结 Phase 0 单仓拓扑；若未来在其合法 clean I/E 上执行 formal，必须由该冻结图自己的 post-E full gate 证明，不能移植到产品 Host。当前物理三仓以 Host 26 个 runner 的 discovery/counts/TRX/coverage 全等，以及 SDK 2 个、Private Plugins 4 个 owner runner分别 0 failed / 0 skipped 为唯一 V5 真值。
- regression ledger 的退役证据必须是可执行契约，不是自由文本豁免。当前 `EDGE-DIECUT-RETIRE-001` 由 schema v3 精确绑定冻结 source commit/tree、41 条唯一旧声明、4 个历史源路径、受控 token/path pattern、`retired-diecut` disposition/decision、非空 reason 和 current discovery 零回流；非文档 active input 只允许 canonical ledger generator 与 ledger 两处命中。`Test-EdgeRetiredFeatureEvidence.ps1` 必须扫描生产/测试/UI/solution/project/config/`.github`/打包发布与全部其它脚本，第三处命中直接失败；独立 fixture 必须证明活动源码、第三治理文件、40/42、重复 oldKey、错误 disposition/decision 和旧声明回流均非零退出。该门禁只接入 smoke 与 runtime-pack 的 preflight，不新增发布入口，也不执行 package/release/deploy。
- compatibility inventory 必须对 alias/adapter/wrapper/compat/legacy/shadow/obsolete/fallback/双写/影子候选逐项提供真实 consumer 证据与有期迁移窗口，并对每个真实声明精确登记 symbol/path/调用证据；宽 token disposition 不得掩护未登记 symbol。每个 `MigrationWindow` symbol 必须绑定唯一 window ID、replacement/deletion/latest removal 约束和逐 symbol 真实 coverage test；缺失/未知 window、token/path 不归属、无 coverage、零 consumer、新增 consumer、未分类或未登记候选直接失败。coverage test 必须是 required runner 中真实执行且 0 skipped 的行为测试，不得用空壳或注释代替。
- duplication 分 production/test-support/tests 的 exact/near ratchet；物理三仓后 Host coverage 只对 Host 26 个 owner runner和 Host 生产源码负责，并继续对 `EdgeMemoryCacheService` 设高风险阈值。`DataPipelineNonRetryableException` 已迁入 SDK Contracts 唯一源码 owner，Host coverage 不得保留指向已不存在 Host 路径的伪阈值，也不得借跨仓源码/PDB fallback 把 SDK 源码重新接入；其 throw/catch 行为继续由 Host runtime workflow 覆盖，SDK/Plugin runner 由各仓独立执行。mutation 固定 Domain Aggregate + MTP report-only 范围与证据，当前 baseline 必须与真实 report/trace 的全部状态数和重算 score 精确一致，伪分数或等总量状态漂移由独立 behavior fixture 拒绝。
- RuntimeLayoutSync 等包含 Windows/Unix 分支的发布工具必须由同一 Deployment runner 显式覆盖两套平台决策；文件 mode 副作用以内部窄 seam 驱动 preserve/default/API-unavailable 分支，生产默认仍调用真实平台 API。coverage 不得通过平台源码排除、warning suppression 或降低 baseline 获得绿色。
- coverage、duplication、mutation、compatibility 的 current baseline 只负责与本次真实结果对账，不能作为自己的历史上限。CI 必须额外运行 `Test-EdgeGovernanceBaselineMonotonicity.ps1`，相对 PR base 的已提交 baseline 做机械单调校验；BaseRef 必须是候选 HEAD 的祖先且不能等于 HEAD，当前 PR 首次引入文件时以最早已提交 bootstrap commit 为锚，未提交 bootstrap 失败。覆盖率/高风险质量阈值不得降低，runner/report 当前数量仍由 inventory/discovery/TRX 精确对账但不做历史永久下限；重复窗口不得放宽且 clone 不得增加；report-only mutation 历史只固定工具/目标/语义范围并要求 score 不下降，不冻结源码变化带来的 mutant identity、测试数或状态绝对数量。compatibility token 不得删除；既有 `MigrationWindow` 的 ID/symbol/caller 上限不得增长或延长期限，当前批不得新增兼容窗口。带 compatibility-like 名称但经当前 inventory 证明唯一声明、真实可执行 caller 且状态为 `OrdinaryAbstraction` 的正常架构抽象允许新增或增长，不得被历史 ID exact-freeze；零 caller、伪注释 caller、未登记 symbol 和迁移语义伪装仍 fail-closed。对应 behavior fixture 必须同时证明四类质量阈值放宽、候选 HEAD 伪 BaseRef、无历史 bootstrap、新迁移面均失败，并证明 runner/mutant 集合合法变化和机器可验证的普通抽象演进不被误杀。
- 项目/case 变化必须先复核授权和完整 diff，再显式更新 inventory，并记录 before/after 原因。仅用户授权时才 commit、push 或修改远端。

## 11. CI 退出条件

三仓 Windows required CI 在获得 Phase 9/10 与 CI 入口单独授权后，必须在 25 分钟 hard timeout 内聚合 Host 26、SDK 2、Private Plugins 4 个 owner runner，并完成 Release build、Analyzer/project graph、正反 build fixture、source-quality、各仓 discovery/执行、Host TRX/coverage、compatibility、duplication与基线单调校验；不得退化回单仓源码 checkout或跨仓 ProjectReference。mutation 使用独立 report job，并先执行伪分数/状态漂移 behavior fixture，不伪装成编译错误。故意执行非法 native build 的 PowerShell fixture在完成全部断言后必须显式清零 `$LASTEXITCODE`，不得把预期失败残留为 job 失败。本批仅有 macOS 本地 V1–V5 owner 结果；Windows 远端 job 与 Windows 实机 Installer/Velopack/DPI 验收在取得对应证据前不得写成已验证。

宿主、SDK 与私有插件的 Phase 6–8 本地物理拆分及 V1–V5 已于 2026-07-22 在三个独立本地 Git 仓收口；冻结 Phase 0 formal/ledger 候选不接入产品 Host。Phase 9–10、新 remote、远端 NuGet、Windows CI/实机、生产部署和 `stable` 均未授权且未执行。`deploy/Deploy-Changed.ps1` 是生产 CD 入口，不属于本地三仓 V1–V5，本批不得运行发布或部署。

## 12. 非声明范围

- 本契约不改变 PLC/MES/Cloud、设备身份、生产数据、UI 或部署行为。
- 本批显式 UoW 关闭不等于第 5 节的插件包隔离和其他临时 seam 已修复；这些必须在各自生产批次独立关闭。
- 文件系统/源码动态事实已归入 `IIoT.Edge.Architecture.Tests`；Analyzer 可静态证明的边界不得新增重复 Regex case。
- 本契约不授权部署、发布、生产数据操作、创建新仓库或修改 remote。
