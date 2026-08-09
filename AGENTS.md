# IIoT.EdgeClient Instructions

工作区 `../docs/总规则.md` 是唯一默认必读入口。本文件只负责 EdgeClient 路由和少量不可缺失的硬边界，不重新定义跨端业务层级。

## 业务规则路由

- 跨端业务真值与已关闭裁决统一读取 `../docs/业务规则.md`；第 0～7 批候选代码继续保留，但复审发现的问题尚未全部收口，文档复审和后续源码整改完成前不是生产部署基线。
- EdgeClient 项目业务细化读取 `docs/客户端规则.md`。该文件只细化 Launcher 动态发现与启动、`ClientCode` 设备插件身份、宿主/插件数据隔离、完整离线安装、可诊断启动、PLC 缓存、Cloud/MES 补偿和真实 UI，并通过 `BR-*` 编号继承总纲。
- AP/CP、MG1/MG2、12 个 PLC、弹夹和当前 MES 身份只是当前现场或待裁决事实；不得把它们提升为 Launcher、Shell、Host、SDK 或新工序的通用规则。
- `docs/业务规则临时和待后续处理问题/业务规则.md` 是冻结历史证据，只用于原字节/hash 核验、历史追溯和删除门禁审计，不得作为活动实现依据，也不是修改 Launcher、安装、更新、Cloud/MES 或 PLC 时的必读输入。该原文继续保持 712 行、53,376 字节和 SHA-256 `63ee46c9f8880268be1b44ef64ab677577ee5d47e724af04995d8544aae3b764`；只有六项删除门禁、用户明确确认与 `GAP-PLC-023` 关闭后才能删除本路由。

## 技术契约路由

- 宿主、插件包、Catalog、下载、安装、更新和版本上报：`docs/Edge客户端宿主插件分发契约.md`。
- 项目图、分层、聚合/Persistence owner、插件 seam、Analyzer 和测试物理归口：`docs/Edge架构边界契约.md`。
- PLC/IO 选择、状态、任务绑定和页面展示：`docs/PLC选择与状态展示控制.md`；Avalonia 页面组合再读 `docs/Edge客户端UI第二层规范.md`。
- 发布、安装、更新和部署验收：`docs/客户端部署.md` 与工作区 `../docs/上传部署总览.md` 的 Edge 目标章节。
- 新工序与跨端接入步骤：`../docs/新工序接入手册.md`；设备插件在安装、启动、目录隔离和 Cloud 业务链中统一以 Cloud 下发的 `ClientCode` 标识，具体 DTO、清单字段和落盘格式由宿主插件分发契约承载。

## 不可缺失的 Edge 硬边界

- Launcher/Shell 必须进入可登录、可诊断、可修配置的真实 UI；单个插件、PLC、MES、Cloud、IO 或业务配置问题不得升级为整机 fatal。
- 一个物理上位机只有一个 Launcher；每个已安装 `ClientCode` 设备插件恰好贡献一个启动卡和一个运行绑定。同一 `ClientCode` 同时只允许一个 Shell 进程，不同 `ClientCode` 必须允许并发；插件名称、`ModuleId`、profile 或物理宿主不能成为另一套防重身份。`ModuleId` 如因装载机制保留，只能是系统维护且在同一活动安装组合内唯一的隐藏技术定位符，人员不选择、不维护。
- Launcher 必须先取得整机单实例锁，再按“更新恢复 → `host.db` → 凭据迁移 → Binding/插件验证 → 开放登录和设备卡片”启动；生产环境缺少有效 `ClientCode` 时禁止用 `EdgeHostDefault` 或空 profile 启动 Shell，开发入口必须单独标识。
- Launcher、Shell、Host 和 SDK 必须保持通用；新工序和新现场设备只通过独立设备插件扩展，Host 不拥有具体工序源码、点位、状态机、MES 字段或按插件名称分支。
- `host.db` 只保存 Launcher/宿主公共可变数据；每个设备插件的配置、数据库、PLC、MES、运行数据、日志和缓存必须在 `plugins/{ClientCode}` 边界内严格隔离。Cloud/MES bootstrap secret、token、签名凭据和其它现场密钥必须按 `ClientCode` 进入实际运行 Launcher 的 Windows 账户 Credential Manager；`host.db`、插件普通文件/数据库和运行时 Binding 只能保存脱敏引用或状态。
- Cloud 下载界面只让人员选择一个或多个设备插件；系统据此解析真实插件产物和全部所选插件共同兼容的最高已批准 `stable/win-x64` Host，不再要求人员分别配对“设备”和“技术插件包”，也不允许手工绕过 Host 兼容结果。生成包必须包含通用 Launcher/Shell/Host、所选插件、`.deps.json`、`.runtimeconfig.json`、.NET/runtime/native/DLL、Avalonia/Skia/Velopack、语言资源、页面路由、首次播种定义、配置、验签、更新和恢复所需的完整离线闭包。缺任一必需项必须在生成或安装前失败，禁止留到客户端首次打开时才暴露。
- 受签名安装信封中的每 `ClientCode` 短期原凭证，其普通文件明文副本只可存在于受控安装载荷和安装 staging，不能复制到其它普通文件。Installer 验签后必须把原凭证值作为 Pending 项导入实际 Launcher 账户的 Windows Credential Manager，再生成只含凭据引用的脱敏运行时 Binding；原子提交成功后只删除 staging 明文副本，Credential Manager 中的 Pending 项必须保留到该 `ClientCode` 激活成功或 Pending 过期。共享文件组必须原子安装，但凭据激活按 `ClientCode` 在该设备插件真实 Ready 后独立完成；一个设备失败、未启动或 Pending 过期不得改变其它设备的有效会话。
- 签名选择清单、安装绑定信封、插件/activation manifest、更新恢复日志、跨进程锁和启动信号仍是文件级安装、验签或恢复证据；不得因为引入 `host.db` 而改成数据库里的第二份伪证据。
- 插件私有数据库只在首次创建时执行一次插件自有播种；后续启动不得重复播种。PLC 稳定配置以插件私有数据库为权威，运行状态来自实时运行缓存；配置修改必须执行“写前失效并提升版本 → 数据库提交并重载 → 写后再次失效并加载已提交值”，旧版本并发读取不得回填缓存。缓存或数据库不可用必须返回 `Unavailable` 并跳过权威上报，绝不能伪装成空 PLC 清单。
- Cloud 与 MES 可以共享 `ClientCode + CompletionId + TypeKey` 完成事实和无业务语义框架，但身份凭据、probe、gate、queue 状态、retry、fallback、deadletter 和回执必须分离；Cloud `DeviceId`、bootstrap、Token、完整 `DeviceSession` 不得进入 MES，MES 编码、站号、签名 Token 和回执不得进入 Cloud。
- 完整 Cloud `DeviceSession` 只留在 Host；插件只能获得不含秘密的设备身份视图。监控热循环只能读取内存快照，稳定配置不得每 500ms 查询 SQLite。
- UI 只展示真实路由、数据、状态、命令和权限；Release 不得注入视觉验收或模拟业务数据。

## 任务边界

- 读取、修改、验证和部署模式继承工作区 `../docs/总规则.md`；本文件不复制测试命令、CI 门禁或部署步骤。
- 历史事实只从 Git、不可变发布/部署证据和工作区事故文档追溯；不新建滚动复盘、历史核心类文档、日期式治理快照或日期式 release 文档。
