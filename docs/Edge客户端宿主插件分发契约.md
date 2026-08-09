# Edge 客户端宿主与设备插件分发契约

> 状态：第 0～7 批候选代码保留；复审发现的问题尚未全部收口，当前不是生产部署基线。真实 Windows 验收、生产绑定迁移和部署仍未执行。本文档是当前活动技术契约，旧 `Phase 1/2/3`、`plugins/<ModuleId>`、“首装后再下载插件”和明文 `BootstrapSecret` 配置口径已由用户新结论取代。

本契约继承 `BR-DOM-*`、`BR-EDGE-*`、`BR-PLUGIN-*`、`BR-DATA-*` 和已关闭的 `BR-OPEN-003`、`BR-OPEN-014`。业务定义以工作区 [`docs/业务规则.md`](../../docs/业务规则.md) 为准，本文只定义 Edge 分发和运行实现边界。

## 1. 核心模型

- 一台具体现场设备对应一个独立设备插件、一个独立发布系列、一个 `ClientCode` 和一份独立业务文档。
- `ClientCode` 是设备插件唯一跨端业务身份，也是卡片、进程、目录、数据库、日志、缓存和补传的隔离键。
- `DeviceId` 仅是 Cloud 内部数据库键，不是 Edge 需要维护的第二业务身份。
- `ModuleId` 仅用于定位插件包内入口；`ProcessType` 仅表示 Cloud 工序分类；`TypeKey` 仅表示插件声明的业务记录类别。三者不得互相等同。
- 一台 Windows 电脑只有一个 Launcher，但可同时运行多个不同 `ClientCode` 的设备插件。
- Launcher、Host/Shell、SDK、Installer、更新框架、日志框架和缓存框架可通用；通用稳定能力进入 SDK，经批准的无状态插件族公共源码可以共享。每个设备插件的最终包、版本、manifest、配置、数据库、状态和运行目录仍独立，运行时不得从另一个插件目录借 DLL。

## 2. 标准目录与存储边界

安装根的活动布局为：

```text
current/                         # Velopack 管理的 Launcher/Host 可执行文件
data/IIoT/EdgeClient/
  host/
    host.db                      # 宿主公共可变数据
  launcher/
    iiot-binding.runtime.json    # 脱敏运行时 Binding
    launcher.update.json
  diagnostics/
plugins/
  <ClientCode>/
    app/
    config/
    db/
    logs/
    cache/
    context/
    buffers/
    data/
```

`host.db` 只能保存 Launcher 账号与锁定状态、语言和宿主设置、已安装设备插件注册表、不含秘密的 Binding 导入账本、更新状态/历史和宿主诊断索引。它不得保存 PLC、MES、生产事实或插件业务配置。

以下证据必须独立于 `host.db`，以便数据库损坏时仍可恢复：

- 签名发布 manifest 与插件 manifest；
- 安装/更新事务恢复日志；
- Windows Credential Manager 中的设备凭证；
- 进程锁、启动信号和 ready 证据。

## 3. 独立设备插件包

每个包必须满足：

- 恰好一个插件入口、一个 Profile 模板和一个机器配置模板；
- `plugin.json` 明确 `ModuleId`、版本、入口程序集、`ProcessType`、Host API 和 Host 兼容窗口；
- `data-capabilities.json` 按当前插件版本声明一个或多个 `TypeKey`、显示名、Schema 版本、作用范围、字段、查询模式和可公开字段；
- 插件自有托管 DLL、native DLL、资源、Schema、页面路由、入口和首次播种定义全部随包；
- 不得从另一个插件目录借 DLL；
- 不得使用手写 DLL 白名单或文件名前缀猜依赖归属。

打包从真实 publish 输出生成两份受签名发布包保护的精确证据：

1. `file-manifest.json`：每个最终相对路径的大小、SHA-256、类型、组件和精确版本。
2. `dependency-closure.json`：每个依赖的最终 `publishPath`、来源归属、大小、SHA-256 和版本。Host 公共依赖还必须引用精确 Host manifest、Host 版本和 manifest hash。

同名文件、路径偏移、版本漂移、缺失、多出或 hash 变化均必须 fail-closed。

## 4. Prepared Release 和完整离线环境

Prepared Release 使用签名动态插件计划，逐项固定插件发布记录、版本、`ModuleId`、支持工序、包 hash、逐文件清单 hash 和 Host 兼容范围。目标插件集合只能来自该签名计划，不得写死 AP/CP 或固定数量。

Cloud 生成的最终 Windows 安装包必须离线包含：

- Installer stub 和 Velopack Setup；
- Launcher 与 Host/Shell 的 EXE、`.deps.json`、`.runtimeconfig.json`、托管/native 依赖和 .NET 自包含运行时；
- 本次选定的每个独立设备插件包；
- SDK 公共契约、资源、语言文件、Schema、页面路由、入口和播种定义；
- 安装载荷 Binding v3、插件选择清单和首装技术配置；
- 覆盖全部载荷字节的签名 `payload-manifest.json`。

干净 Windows 不得要求预装 .NET，首次启动不得上网补 DLL 或 Runtime。缺少 Shell EXE 或自包含运行时时直接失败，禁止回退执行系统 `dotnet`。

Cloud 组包前、最终 EXE 生成后和 Windows 安装前均重新验证路径、大小、SHA-256、版本、类型和所属组件。

## 5. Binding v3 和凭证

必须区分两种 Binding：

### 5.1 安装载荷 Binding

每个设备项包含：

- `ClientCode`、设备名称、工序；
- 独有插件版本、包摘要、`ModuleId` 和入口；
- `plugins/<ClientCode>/` 下全部独立目录；
- `baseUrl` 以及下表唯一 `paths` 字段；
- 每个 `ClientCode` 独立的短期 pending 凭证、生成记录和有效期。

它只存在于受签名安装载荷和 staging，不得原样复制到正式运行目录。

Binding v3 的路径字段、运行配置键和 Cloud 路由固定为：

| Binding `paths` 字段 | 运行配置唯一键 | 路由值 |
|---|---|---|
| `deviceInstance` | `CloudApi:Paths:DeviceInstance` | `/api/v1/edge/bootstrap/device-instance` |
| `bootstrapRefresh` | `CloudApi:Paths:BootstrapRefresh` | `/api/v1/edge/bootstrap/edge-refresh` |
| `activateDevice` | `CloudApi:Paths:ActivateDevice` | `/api/v1/edge/bootstrap/device-activate` |
| `activateDeviceConfirm` | `CloudApi:Paths:ActivateDeviceConfirm` | `/api/v1/edge/bootstrap/device-activation-confirm` |
| `identityDeviceLogin` | `CloudApi:Paths:IdentityDeviceLogin` | `/api/v1/human/identity/edge-login` |
| `humanIdentityRefresh` | `CloudApi:Paths:HumanIdentityRefresh` | `/api/v1/human/identity/refresh` |
| `humanSessionValidation` | `CloudApi:Paths:HumanSessionValidation` | `/api/v1/human/identity/session` |
| `deviceLog` | `CloudApi:Paths:DeviceLog` | `/api/v1/edge/device-logs` |
| `passStationBatchTemplate` | `CloudApi:Paths:PassStationBatchTemplate` | `/api/v1/edge/pass-stations/{typeKey}/batch` |
| `capacityHourly` | `CloudApi:Paths:CapacityHourly` | `/api/v1/edge/capacity/hourly` |
| `capacitySummary` | `CloudApi:Paths:CapacitySummary` | `/api/v1/edge/capacity/summary` |
| `capacitySummaryRange` | `CloudApi:Paths:CapacitySummaryRange` | `/api/v1/edge/capacity/summary/range` |
| `recipeByDeviceTemplate` | `CloudApi:Paths:RecipeByDeviceTemplate` | `/api/v1/edge/recipes/device/{deviceId}` |
| `clientReleaseCatalogTemplate` | `CloudApi:Paths:ClientReleaseCatalogTemplate` | `/api/v1/edge/client-releases/device/{deviceId}/catalog` |
| `clientVersionReport` | `CloudApi:Paths:ClientVersionReport` | `/api/v1/edge/client-releases/version-reports` |
| `runtimeHeartbeat` | `CloudApi:Paths:RuntimeHeartbeat` | `/api/v1/edge/runtime-heartbeats` |
| `edgeHostPlcRuntimeStates` | `CloudApi:Paths:EdgeHostPlcRuntimeStates` | `/api/v1/edge/edge-hosts/plc-runtime-states` |

- `passStationBatchTemplate` 必须原样保留且只保留一个 `{typeKey}`；`recipeByDeviceTemplate`、`clientReleaseCatalogTemplate` 必须各原样保留且只保留一个 `{deviceId}`。
- Binding v3 不接受 `plcSnapshot`、`passStationBatch`、`CloudApi:Paths:PlcSnapshot`、`CloudApi:Paths:PassStationBatch` 或少 `/edge` 段的 PLC 路由作为别名。Cloud 生成器、Installer v3 parser/materializer、Launcher 启动前置和 Host 运行消费者必须逐字段使用同一表，未知、缺失、重复或别名字段一律失败关闭；Launcher importer 不参与 v3 物化。
- 安装校验必须针对“Cloud Binding 动态注入值覆盖 Host 包内默认值”后的最终合并配置执行，不能只校验 Binding JSON 或让默认值掩盖缺字段。上表 17 项必须全部非空、为以单个 `/` 开头且不含 scheme/host/`//` 的相对路由，模板占位逐字匹配；最终 `ICloudApiEndpointProvider` 对每一项的解析值必须与表中 Cloud 注入值逐字相同。任何未知字段、旧别名、未消费字段、默认值回退或最终值漂移都必须在开放登录和设备卡片前失败关闭。

### 5.2 运行时 Binding

运行时 Binding 保留相同的非秘密事实，但将原始 pending 凭证替换为 Windows Credential Manager 引用，并记录每台设备的激活状态和 `CredentialOwnerSid`。

- pending 引用：`IIoT.Edge/Pending/{GenerationId}/{ClientCode}`；
- Refresh Token 引用：`IIoT.Edge/Session/{ClientCode}`；
- Access Token 仅存进程内存；
- 凭证必须写入实际运行 Launcher 的 Windows 账户，写入后立即回读；
- Launcher 当前 Windows SID 必须与 `CredentialOwnerSid` 逐字一致；不一致时提示使用实际运行账户重新安装，不得跨账户复制凭证；
- 账户不一致、Credential Manager 不可用或回读不一致时安装或启动失败，不回退明文。

`EdgeBindingMaterializer` 是 Binding v3 从 wire JSON 到无秘密 runtime Binding 和 machine config 的唯一物化器，正式调用方只能是 Installer。Launcher importer 只保留 Binding v2 只读迁移；遇到 v3 不得二次导入、重写、补路由或从 Host `appsettings` 补齐。当前未发布的不完整 v3 候选字节直接拒绝，不新增 schema v4。

Binding Schema 版本化。新增必填字段或路由时，Cloud 生成器、Installer parser/materializer、Launcher 启动前置、v2 只读迁移器和 Host 运行消费者必须同批更新，未知版本或缺字段一律失败。

## 6. 安装事务

新 Binding v3 只能由 Installer 物化，并在一个可恢复事务中按以下顺序执行：

1. 解压到全新 staging。
2. 验证发布签名、逐文件 hash、精确版本、全部依赖闭包及插件选择清单。
3. 严格校验所有 `ClientCode`、独立目录和 Binding v3 17/17 路由。
4. 将各设备 pending 凭证写入当前 Windows 账户 Credential Manager，立即回读对账并记录 Owner SID。
5. 通过唯一物化器生成无原始凭证的 runtime Binding 和每个 `ClientCode` 的 machine config，再对“Cloud 动态注入覆盖 Host 默认值”后的最终值逐项对账。
6. 离线自检 Installer、Launcher、Shell、Host 和全部插件。
7. 将文件、runtime Binding、machine config 和凭证变更作为同一组原子切换，任一失败恢复切换前文件、配置和凭证。
8. 全部成功后删除 staging 中含原始 pending 凭证的字节，写独立恢复证据并启动 Launcher。
9. 用户之后分别启动设备卡片，各设备独立激活。

文件安装是整组原子事务，凭证激活按设备独立。任一安装步骤失败必须恢复切换前文件、配置和凭证，不启动残缺 Launcher，不显示安装成功，并保留可读诊断。

## 7. 设备独立激活

- 生成安装包不得立即覆盖现场旧凭证。
- pending 默认有效期 7 天，只能换取“仅允许激活”的临时会话，不能上传生产、日志、心跳或 PLC 状态。
- 插件真实加载并 ready 后才能确认激活，然后签发正式 Access/Refresh 会话并撤销该 `ClientCode` 的旧 Refresh 会话。
- 任一 `ClientCode` 的启动、失败或过期不得改变另一个 `ClientCode`。
- pending 过期只拒绝新 bootstrap，不杀死已签发有效会话。
- Ready 安装包在激活或过期前按 `GenerationId` 受控保存；下载中断重试返回同一字节，不得重新生成凭证。

Credential Manager 只降低配置文件、备份和离线拷贝的泄漏风险，不能防御同一 Windows 账户下的恶意进程、Windows Local Administrators 组成员或内存窃取。

## 8. Launcher 和更新

- Velopack 生命周期 hook 必须是进程第一步；随后取得固定整机互斥量 `Global\IIoT.Edge.Launcher`。一台 Windows 电脑只能运行一个 Launcher。第二个 Launcher 必须在创建 UI、打开数据库或写共享文件前退出；互斥量创建、权限或取得失败一律失败关闭。取得遗弃锁后仍必须先执行恢复，不得跳过。
- Launcher 为每个 `ClientCode` 生成一张设备卡片，显示 Cloud 注入的设备名称、工序、版本、运行状态和更新内容。
- 每个 Shell 使用规范化 `ClientCode` 的独立互斥锁防重；Launcher 整机锁与 Shell 防重锁是两层不同边界，不得互相替代。
- 锁创建失败、身份缺失、目录冲突或 ready 证据不完整时必须阻断，禁止 fail-open。
- ready 同时核对 PID、`ClientCode`、`ModuleId`、插件版本、包摘要和真实加载结果。
- 不同 `ClientCode` 可并发；同一 `ClientCode` 仅允许一个进程。
- Profile/MachineProfile 只是从 `ClientCode` 确定性派生的隐藏兼容文件，不是身份。
- 生产环境没有有效 `ClientCode`、运行时 Binding 或目标插件时必须在数据库、网络和插件加载前失败关闭并显示稳定原因码，禁止用 `EdgeHostDefault`、空 `Modules.Enabled`、静态 Default catalog 或其它默认 profile 启动空 Shell。显式开发 fixture 不得进入生产安装包、Launcher 发现或恢复链；生产包不得包含可启动的 `launcher.profiles.json` Default 卡。

Launcher 启动顺序只允许以下一种表达，前一步未成功不得开放后一步：

1. 执行 Velopack 生命周期 hook。
2. 取得 `Global\IIoT.Edge.Launcher` 整机锁。
3. 恢复未完成的安装/更新事务。
4. 恢复并迁移 `host.db`。
5. 以“只读盘点 → Credential Manager 写入/回读 → 脱敏 staging → 对账 → 原子切换”迁移旧明文 bootstrap/refresh；失败时原源和原运行配置原样保留。
6. 校验 Binding v3、17/17 路由、最终 machine config、凭证引用及 `CredentialOwnerSid`。
7. 逐 `ClientCode` 校验插件 manifest、逐文件 hash、包摘要和精确版本。
8. 全部通过后才初始化可交互主窗口，开放登录、设备卡片和 Shell 启动。

插件更新只能沿该设备已绑定发布系列进行。更新在 staging 验证完整包、精确依赖闭包、签名、Host 兼容性和 Binding 事实后，按 `ClientCode` 原子替换 `plugins/<ClientCode>/app`。更新不得覆盖该设备的现场 PLC、MES 或业务配置。

## 9. 宿主库与旧目录迁移

迁移顺序固定为：

1. 先依据独立文件恢复日志完成未结束的更新恢复，或明确阻断共享写入。
2. 只读盘点旧 Launcher JSON、更新状态和 Profile 目录。
3. 保持原文件不变，在 staging 创建带 Schema 版本的 `host.db`，并单事务导入宿主公共状态。
4. 按 `ClientCode` 迁移旧凭据到实际 Launcher 账户 Credential Manager，回读成功后写入脱敏引用；再迁移各插件目录。
5. 对账 Binding、插件、数量、唯一约束、关键字段、数据库可读性和文件 hash。
6. 将 Binding、凭据、`host.db` 和目录作为同一可恢复事务原子切换，并保留回退副本。
7. 全部通过后才开放登录和设备卡片。

任一步骤失败必须恢复原 Binding、原凭证、原宿主数据和原插件目录。无法从旧 Profile 唯一确认 `ClientCode` 时停止，不按设备名称、AP/CP、工序或 MES 编号猜测。

本章的七个 Launcher 前置门和第 8 步交互开放是唯一启动顺序。插件进程不得各自写共享 `host.db`，主窗口不得在前置步骤全部完成前创建或开放交互。

## 10. 首次初始化、PLC 快照和生产数据

- 每个 `ClientCode` 的设备插件独立拥有自己的数据库 Schema、migration 历史、播种完成记录和事务边界；Host、Launcher、Cloud、SDK 与其它插件不得读取或写入其 PLC、IO、MES、任务、参数或生产业务表。
- 每个 `ClientCode` 的新空插件数据库执行 EF Migration、插件自有播种和“初始化完成”标记，必须在该插件自己的同一数据库事务中提交。
- 初始化失败整体回滚并在下次启动重试；成功后正常启动永不重播。插件升级只执行明确 migration，人员删除的 PLC 或配置不得复活。
- PLC 稳定配置从插件独立数据库加载后缓存，实时状态来自采集内存。Dashboard、状态上报和 SDK 读取共用同一内存快照；监控 500ms 热循环不得查询 EF/SQLite，也不得按 PLC 形成任务绑定/IO/recovery N+1，配置变化只通过版本栅栏刷新快照。
- 快照必须包含权威性、配置版本、采集时间、PLC 编码/名称/地址/协议/启用状态、运行状态、最后真实通信时间和错误。
- 缓存或数据库不可用时返回 `Unavailable` 并跳过本轮上报，不得发送空数组。只有携带新权威清单版本和明确清空意图的零 PLC 快照才能清空 Cloud 投影。
- `ClientCode + CompletionId + TypeKey` 标识的真实完成事实先写短期防断电交接记录，Cloud/MES 各自直传；失败只进入本 `ClientCode` 的对应通道 retry/fallback/deadletter。两条通道可以复用无业务语义框架，但凭据、会话、门控、状态和回执必须独立。
- 完整 Cloud `DeviceSession` 及其 bootstrap/Access/Refresh/Activation Token 只留在 Host 出站层；插件和 MES uploader 只能获得不含秘密的设备身份视图。Cloud 的 `DeviceId`、Token、会话和门控不进入 MES，MES 的上位机编码、站号、签名 Token 和业务回执不进入 Cloud。
- 自 SDK `2.0.13` 起，正式 v3 MES 契约固定为 `DevicePluginUploadContext(DevicePluginIdentity)` 和 `IProcessMesUploaderV3`；SDK `2.0.14` 保持该边界并增加私库 lifecycle/configuration v1。身份视图只包含 `ClientCode/ModuleId/ProcessType`，`CompletionId/TypeKey` 继续来自完成记录。`IProcessMesUploader/ProcessUploadContext` 只保留 v2 ABI 且标记废弃；正式 v3 插件只实现 legacy 接口时 Host 必须拒绝，不得回退。插件 DI 不得导出完整 `IDeviceService`。
- 补传默认 30 分钟，链路恢复立即触发；成功确认后删除对应补传记录，第 20 次失败原子转入本通道死信。
- 正常成功生产数据不得长期写客户端生产历史表或 Excel；Cloud 是历史查询真值。旧本地历史只读保留，未经另行授权不得自动删除。

## 11. Catalog、上报与兼容退出

- Cloud catalog 和下载计划以已绑定设备插件为单位，返回该设备独有插件系列的发布版本。
- Host 版本、通道和架构由 Cloud 自动计算所有选定插件共同兼容的最高已批准 `stable/win-x64` 版本，页面只读展示。
- 版本上报包含 `ClientCode`、Host 版本/API、插件 `ModuleId`、插件版本、包 hash 和启用状态。上报失败只进入诊断/独立重试，不得阻断 PLC/MES/Cloud 生产链。
- 过渡期 Edge 可只读解析 Binding v2，但新安装只接受 v3。迁移不得将 ModuleId/Profile/MachineProfile 升格为业务身份。
- 完成真实 Windows 与当前生产设备迁移验收后，下一独立版本才能删除 v2 写入、ModuleId 绑定、旧明文凭证和旧播种运行路径。
- 本批源码版本分别为 SDK `2.0.14`、Host `2.0.17`、AP `2.0.22`、CP `2.0.22`，Host API 代际仍为 `2.0.0`。AP/CP 数值相同不表示共同版本链；两者必须分别保留发布记录和验收证据。SDK `2.0.12/2.0.13` 与 Host/插件旧版本证据保持不变，不得覆盖或倒写为已发布；兼容状态以三仓 clean-main 精确 SHA 门为准。

## 12. 验收要求

至少自动验证：

- 任意 P3 设备插件不修改通用打包/部署框架即可发布；
- 下载包中 ClientCode 集合、Binding、插件选择、实际目录和签名逐文件清单严格一致；
- 删除、多出或篡改任意 DLL、native 文件、Runtime、Schema、路由或 manifest 都在组包或安装前失败；
- 安装载荷可含短期 pending 凭证，正式运行目录和普通 JSON 不得含原始凭证；
- 同一 `ClientCode` 重复启动被阻断，不同 `ClientCode` 并发成功；
- `host.db` 和旧目录迁移在任一故障点可回滚，坏库不影响独立更新恢复日志；
- 新数据库只播种一次，删除 PLC 后重启不复活；
- PLC 快照预热后周期路径无 EF/SQLite 查询，异常不上传空数组；
- Cloud/MES 直传、单通道失败、恢复立即补传、成功删除、20 次死信、ACK 丢失、断电重启和幂等分别有测试证据。

未执行真实 Windows 安装、生产迁移或部署时，交付结论必须明确标记 `NOT-RUN`。
