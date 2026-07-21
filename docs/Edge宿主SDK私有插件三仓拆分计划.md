# Edge 宿主、SDK 与私有插件三仓拆分计划

> 版本：v0.3（单维护者、单真实链路版）
> 状态：Phase 0–5 已授权在既有 `IIoT.EdgeClient` 仓实施；Phase 6–10、建远端仓、远端发包、发布与部署未授权
> 日期：2026-07-17
> 当前范围：只定义 `IIoT.EdgeClient` 的拆分目标、批次、门禁与未来三仓协作方式
> 权威上位规则：[总规则](../../docs/总规则.md)、[客户端规则](客户端规则.md)、[Edge 架构边界契约](Edge架构边界契约.md)、[Edge 客户端宿主插件分发契约](Edge客户端宿主插件分发契约.md)、[三项目测试架构治理总计划](../../docs/三项目测试架构治理总计划.md)

## 0. 拍板结论

本次拆仓采用三个 Git 仓库：

1. `IIoT.EdgeClient`：宿主、Launcher、Shell、Installer、Application、Domain、Infrastructure 和运行时装载。
2. `IIoT.Edge.Sdk`：插件公共契约、SDK 基类、共享 UI、构建期 Analyzer 和兼容验证入口。
3. `IIoT.Edge.Plugins.Private`：私有工序插件单仓，首个真实插件为 Homogenization。

仓库名是建仓前可一次性确认的工作名，不影响本文的边界裁决。

执行顺序固定为：

```text
先在当前仓抽干净契约
→ 宿主和真实插件改吃同一批正式 SDK 包
→ 收紧插件包白名单与宿主运行时装载
→ 用唯一真实兼容链验证
→ 再物理拆成三仓
→ 最后改造现有唯一发布入口的影响映射
```

本计划不是“少测版”。工业级测试保留，但不建设平行打包器、平行加载器、平行安装器、平行 catalog 或第二套发布链。所有检查必须复用正式实现、真实产物、真实页面和真实状态。

## 1. 为什么拆，以及不解决什么

### 1.1 要解决的问题

- 真实插件当前直接引用宿主 `Application`、`SharedKernel`、`Presentation.Navigation`、`UI.Shared` 和 `Module.Sdk` 项目，无法独立开发、构建和发布。
- 当前 `Module.Sdk` 自身继续引用宿主 `Application` 和 `SharedKernel`，还不是可以脱离宿主的稳定 SDK。
- 宿主加载器当前把 `Application`、`Domain`、`Presentation.Navigation` 等宿主内部程序集当作共享契约，插件边界过宽。
- 当前插件打包脚本复制整个模块 build output，插件包可能携带宿主 DLL 或无意带入的新依赖。
- 宿主、SDK 和插件的版本演进没有形成可机器验证的“代际内兼容、跨代组合切换”闭环。
- 真实插件测试虽然已经存在，但拆仓后必须明确归属，并且不能用中性 TestPlugin 冒充真实工序验收。

### 1.2 目标

- 插件项目对宿主源码 `ProjectReference` 为零，只消费已发布的 SDK NuGet 包。
- 宿主不引用任何具体工序插件源码或类型，只通过公共契约、manifest 和运行时装载发现插件。
- SDK 仓不依赖宿主仓或私有插件仓，也不承载任何具体工序业务。
- 插件包只包含插件自有程序集、插件自有依赖、资源、配置和真实 manifest；SDK/宿主共享 DLL 只由宿主提供。
- 同一 `hostApiVersion` 代际内保持二进制兼容；跨代不在新宿主里长期背负旧 API，而由 Launcher 选择或提示匹配的宿主/插件组合。
- 三个仓库均可从独立 clean checkout 构建和测试，跨仓只通过正式包、不可变 artifact 与 digest 对接。
- 保持现有生产发布唯一入口 `deploy/Deploy-Changed.ps1`，拆仓只改变其影响解析和内部调度，不增加第二个对外入口。

### 1.3 非目标

- 不创建 GitHub Organization、GitHub App、CODEOWNERS 强制审批、第二 reviewer 或自定义授权状态机。
- 不建设兼容性服务、审批后台、质量仪表盘或跨仓 webhook 网络。
- 不在同一宿主进程并行装载多代 SDK，也不为每一代建立永久兼容 adapter。
- 不把 PLC、MES、Cloud、配方、生产任务或匀浆业务逻辑上提到 SDK。
- 不因拆仓修改现有生产数据语义、上传出口、设备身份链或现场业务流程。
- 本计划本身不创建远端仓库、不发布 NuGet、不上传 `stable`、不执行部署、不连接生产或现场设备。

## 2. 当前客观基线

开始实施前必须重新生成一次机器基线；以下是编写本文时用于定方向的当前事实，不作为未来批次的永久数量地板。

### 2.1 项目与测试

- 当前 solution：61 个项目。
- required runner：32 个。
- Phase 0 guard 接入后的 Release 发现：1334 个测试（基线 1332，新增账本正例与负例门禁 2 个），要求 `discovered = executed = passed`、`failed = 0`、`skipped = 0`。
- 中性插件 fixture：1 个，即 `src/Testing/IIoT.Edge.TestPlugin`；它只证明通用宿主机制，不证明 Homogenization 真实兼容。
- Homogenization 真实测试当前包括 Conformance、Conformance Filesystem、Workflow、Workflow Filesystem 四类 runner。

拆仓会改变各仓项目数和测试数。允许数字有解释地迁移，不允许为维持 61/32/1280 保留空项目、dummy case 或双跑旧入口。每批必须保存 before/after inventory，并证明 RegressionId 或真实覆盖去向没有静默丢失。

### 2.2 当前真实依赖

`src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj` 当前直接引用：

- `IIoT.Edge.Presentation.Navigation`
- `IIoT.Edge.Module.Sdk`
- `IIoT.Edge.SharedKernel`
- `IIoT.Edge.UI.Shared`
- `IIoT.Edge.Application`

插件源码还实际使用：

- `Application.Abstractions.*`
- `Application.Features.*`
- `Application.Modules.*`
- `Application.Common.DataPipeline`
- `Domain.Hardware.Aggregates`
- `Presentation.Navigation.PluginSystem`

`src/Modules/IIoT.Edge.Module.Sdk/IIoT.Edge.Module.Sdk.csproj` 当前继续引用 `Application` 和 `SharedKernel`，且 `IsPackable=false`。

### 2.3 当前运行时与打包风险

- `ModulePluginAssemblyResolver` 当前把 `Application`、`Domain`、`Module.Sdk`、`Presentation.Navigation`、`SharedKernel`、`UI.Shared`、Avalonia 和部分 `Microsoft.Extensions.*` 视作宿主共享程序集。
- `PackEdgePlugin.ps1` 当前使用整个 module build output 作为 staging 输入；虽然已有运行数据黑名单，但还没有形成程序集级静态白名单。
- Homogenization `plugin.json` 当前为插件版本 `1.0.1`、`hostApiVersion=1.0.0`，且 `maxHostVersion=99.0.0`；后者不能作为未来宿主兼容证明。

这些事实决定了不能直接移动目录。必须先消灭源码边界泄漏，再建立正式包和真实运行时门禁。

## 3. 不可违反的执行原则

### 3.1 只有一条真实链路

```text
真实源码
→ 正式 SDK pack
→ 正式 restore/build
→ 正式 PackEdgePlugin
→ 真实 plugin.json / DLL / SHA256
→ 正式安装目录布局
→ 宿主正式校验与 AssemblyLoadContext
→ 真实 Homogenization 注册、页面与 Workflow
→ 现有 Deploy-Changed 发布入口
```

允许为不同阶段提供薄编排命令，但这些命令只能调用上图中的同一实现。禁止另写测试版 pack、测试版 manifest parser、测试版插件加载器、测试版安装器、测试版 catalog 或测试版上传路径。

### 3.2 严禁假数据、假状态和假证据

- 兼容与发布验收必须使用本次正式构建产生的 SDK 包、正式插件 zip、真实 manifest 和真实 SHA256。
- 上一正式版本必须下载不可变发布 artifact 并校验 SHA256；禁止从旧 tag 重新编译后冒充“上一正式二进制”。
- UI 验收必须创建插件注册的真实 View/ViewModel，加载真实 XAML、资源、样式、绑定和语言资源；禁止创建假页面或只扫描源码字符串。
- 未连接 PLC、MES 或 Cloud 时，必须展示并断言真实的离线、未配置、不可达或空态；不得注入“在线”“已上传”“有生产记录”等模拟状态。
- 没有真实采集记录或真实本地缓存时，生产数据页保持空态；不得为了截图或测试好看生成记录。
- 发布 metadata、下载地址、包大小、hash、release notes 和 catalog 状态必须来自真实产物与真实发布记录。
- 纯函数/Analyzer 的受控输入和现有分层单元测试可以继续存在，但不得被用来替代包、装载、UI、兼容、发布或现场链路的真实完成证据。
- 真实生产数据和现场凭据同样不得进入普通测试；“真实”指真实产物、生产代码路径和如实状态，不等于复制生产数据。

### 3.3 中性 TestPlugin 与真实插件分工

- 中性 TestPlugin 继续覆盖宿主的通用发现、装载、生命周期、隔离、拒载和负例。
- TestPlugin 不得携带匀浆业务、真实设备点位、MES 字段或生产数据。
- 任何拆仓阶段完成、SDK 发包或宿主兼容结论，必须额外通过真实 Homogenization 包；TestPlugin 不能单独满足完成门。

### 3.4 启动必须非阻断

- 插件缺失、manifest 无效、版本不兼容、页面资源失败、PLC/MES/Cloud 不可达都不得把普通业务问题升级为桌面进程 fatal。
- 宿主应拒载问题插件、保存稳定原因码、显示真实诊断，并继续进入可修配置的 Shell。
- 只有宿主自身程序集、DI 或框架 runtime 无法构造时才允许停止启动。
- 相关改动必须验证真实启动路径或等价生产组合集成测试；只 build 或窄单测不算完成。

### 3.5 不保留双轨兼容层

- 类型迁移到 SDK 后，宿主和插件同时改用新类型，旧声明物理删除。
- 禁止用 facade、type forwarding、同名 wrapper、旧 namespace adapter 或 fallback ALC 长期维持两条路径。
- 如果本次边界变化构成新 API 代际，就明确递增 `hostApiVersion` 并走组合切换，不把旧 Application/Domain 类型继续暴露给新插件。

## 4. 三仓目标架构

### 4.1 仓库职责

| 仓库 | 拥有 | 不得拥有 |
|---|---|---|
| `IIoT.EdgeClient` | Domain、Application、Infrastructure、DataPipeline runtime、PLC transport、Host.Bootstrap、Shell、Launcher、Installer、更新与运行诊断 | 具体工序源码、具体工序测试、SDK 源码副本、插件打包实现副本 |
| `IIoT.Edge.Sdk` | 公共契约 DTO/port、Module SDK 基类、UI.Shared 与 UI 注册契约、Analyzer/AnalyzerTests、API baseline、唯一跨仓兼容验证编排 | Domain 聚合、EF/Dapper、宿主 handler、具体 provider、具体工序、生产发布实现 |
| `IIoT.Edge.Plugins.Private` | Homogenization 及未来私有工序插件、各插件业务/Workflow/Conformance/UI 测试、唯一插件 pack 实现 | 宿主源码、SDK 源码副本、Cloud/Host 发布实现、跨插件共享具体工序业务 |

### 4.2 依赖方向

```text
IIoT.EdgeClient ───────PackageReference──────▶ IIoT.Edge.Sdk packages

IIoT.Edge.Plugins.Private ─PackageReference─▶ IIoT.Edge.Sdk packages

IIoT.Edge.Sdk ─X─▶ IIoT.EdgeClient
IIoT.Edge.Sdk ─X─▶ IIoT.Edge.Plugins.Private
IIoT.EdgeClient ─X─▶ 具体插件源码/程序集编译引用
具体插件 ─X─▶ Host/Application/Domain/Infrastructure/Presentation 实现
```

宿主与具体插件只在运行时通过同一份 SDK 程序集 identity 相遇。

### 4.3 SDK 仓最小包集合

保持一个 SDK 仓，但按运行时职责分成最少必要包，避免所有插件被迫带入完整 Avalonia 或宿主实现：

| 包 | 职责 | 依赖约束 |
|---|---|---|
| `IIoT.Edge.Module.Contracts` | 生命周期、builder、capability、domain-neutral DTO、端口、结果和稳定枚举 | 不依赖宿主、数据库、UI 或具体 provider |
| `IIoT.Edge.Module.Sdk` | 通用任务基类、强类型参数/信号辅助和无工序语义的 SDK 实现 | 只依赖 Contracts 和批准的稳定第三方抽象 |
| `IIoT.Edge.UI.Shared` | 共享控件、资源、语言与插件 UI 注册/导航契约 | 不依赖宿主 Presentation 实现或具体工序 |
| `IIoT.Edge.Module.Analyzers` | 阻断插件越界、SDK 公共面泄漏和禁用包引用 | build-time only，consumer 使用 `PrivateAssets=all` |

包名在 Phase 0 最终清单中对账一次；后续不得为了迁移方便再增加一个总包、兼容包或宿主镜像包。

### 4.4 SDK 允许提供的“插座”

SDK 公共面只允许以下类别：

- 模块 identity、manifest schema、生命周期和 capability 声明。
- `IEdgeProcessModule`、`IEdgeProcessModuleBuilder` 及其窄注册能力。
- 与具体工序无关的 DataPipeline record/port、Cloud/MES 目标枚举和诊断结果。
- PLC 逻辑信号访问、Buffer/任务端口和强类型信号契约；不包含 transport、driver 或地址硬编码。
- 日志、时间、配置读取、参数快照、设备 identity 和任务调度的稳定端口。
- 硬件/IO 模板 DTO 与 contributor port；不暴露 Domain 聚合或 repository。
- 插件 UI 的 ViewId、View/ViewModel 注册、资源字典注册、导航请求和共享控件契约。
- 必须跨边界传递的不可变 DTO、值类型和稳定错误码。

### 4.5 SDK 明确禁止提供的内容

- `Domain` entity、aggregate、domain service、repository 或 `DbContext`。
- Application Command/Query handler、Features、具体 module service 或 `Application.Common` 实现。
- EF/Dapper/SQLite、HTTP client、MES/Cloud provider、PLC driver/transport。
- Host.Bootstrap、Shell、Launcher、Installer、Presentation.Panels/Shell 的实现类型。
- Homogenization 名称、点位、参数、状态机、MES 字段、生产 payload、页面布局或业务默认值。
- 为兼容旧插件而复制的宿主内部类型。

### 4.6 公共 API 递归门禁

SDK Analyzer/API gate 必须递归检查公共面，而不是只看 namespace：

- public/protected 类型、基类和实现接口；
- 方法参数、返回值、属性、事件、字段和 delegate；
- 泛型参数、约束、嵌套类型和数组/集合元素；
- attribute、异常类型、扩展方法和默认值类型；
- DTO 属性闭包中的所有外部程序集 identity。

任一公共面泄漏 Host、Application、Domain、Infrastructure、Presentation 实现或具体工序类型都必须构建失败。

## 5. Domain.Hardware 迁移专项

当前 Homogenization 开发样例直接使用 `Domain.Hardware.Aggregates`，这是全计划语义风险最高的迁移，不得和普通接口搬家混成一个大提交。

目标调用方向：

```text
插件声明不可变 Hardware/IO Template DTO
→ 宿主 Application 校验、授权和编排
→ 宿主 Domain 创建/修改聚合
→ 宿主持久化
```

插件不得再读取或构造 `NetworkDevice` 等 Domain 聚合，也不得取得 repository。SDK DTO 只表达插件需要提交的模板事实，不承载宿主领域规则。

该专项的完成证据必须包括：

- 迁移前后 Homogenization 真实硬件/IO 模板 snapshot 字节或结构化字段一致。
- 字段、顺序、协议、地址、数据类型、默认值、启用状态、备注和资源 key 一致。
- 宿主把 DTO materialize 为 Domain 聚合后的可观察结果一致。
- 原 Homogenization Workflow/Conformance 测试全部原样通过；确因项目移动改变测试 identity 时必须登记一一迁移。
- 插件编译 metadata 和依赖账本中 `IIoT.Edge.Domain` 使用为零。
- 不新增绕过设备分配、功能权限、事务或一次 commit 的写路径。

## 6. 运行时 DLL 所有权与版本规则

### 6.1 宿主唯一提供共享程序集

- 宿主和插件在构建时消费同一 SDK package version。
- 宿主发布物携带 SDK runtime DLL、`IIoT.Edge.UI.Shared` 及批准的共享框架程序集。
- 插件 zip 不携带任何 SDK、UI.Shared、宿主、Application、Domain 或 Presentation DLL。
- 默认 ALC 先装载宿主提供的共享程序集；插件 collectible ALC 只装载插件 entry 与 plugin-owned dependency。
- 请求的共享程序集 identity、版本或 API 代际不匹配时 fail-closed；禁止回退到插件目录里的另一份 SDK DLL。

### 6.2 插件包静态白名单

唯一 `PackEdgePlugin` 实现必须从编译图和显式 ownership 生成包，不再复制整个 build output。包只允许：

```text
plugin.json
IIoT.Edge.Module.<ModuleId>.dll
插件显式登记的 plugin-owned DLL
Config/**
Resources/**
必要的 pdb/符号文件（仅按发布策略）
```

必须拒绝：

- SDK、UI.Shared、Application、Domain、Presentation、Host、Infrastructure DLL；
- Tests、Testing、TestKit、VisualTestData；
- `edge.db`、Cloud/MES queue、日志、recipe、excel、现场配置和任何运行数据；
- 未分类的第三方 DLL；
- manifest 未声明或 entry metadata 没有真实引用的多余文件。

白名单由正式 pack 实现生成；宿主/SDK 测试只检查产物，不再维护第二份复制逻辑。

### 6.3 版本三件套

- NuGet `PackageVersion`：SDK 包的 SemVer 发布版本。
- `AssemblyVersion`：在同一 `hostApiVersion` 代际内保持稳定，防止无 API 变化的 patch 破坏二进制绑定。
- `FileVersion` / `InformationalVersion`：记录每次真实构建版本和 source commit。

`hostApiVersion` 是运行契约代际键，宿主与插件必须精确匹配。任何公共二进制契约变化都必须明确裁决：

- 无公共面变化：可以只升 SDK package patch，`hostApiVersion` 不变。
- 公共面或二进制语义变化：新建 `hostApiVersion` 代际，宿主和插件组合升级。

本次从宿主内部类型迁移到独立 SDK 属于明确的边界代际变化，默认把目标代际设为 `hostApiVersion=2.0.0`；最终版本只允许在 Phase 0 基线冻结时调整一次。不得把 `1.0.0` 原地改义。

`minHostVersion` / `maxHostVersion` 只填写真实验证过的宿主窗口，禁止继续用 `99.0.0` 表示未来兼容。

## 7. 代际内兼容与跨代组合切换

### 7.1 代际内强制兼容

同一 `hostApiVersion` 内，候选宿主必须加载并打开所有仍受支持的正式插件二进制。SDK 或宿主发布前不得只重新编译插件源码；必须同时验证已发布 zip。

### 7.2 跨代不背永久兼容层

当前 `Host API 1.0.0 + Homogenization 1.0.1` 可登记为已知良好旧组合。目标新组合是：

```text
旧组合：Host API 1.0.0 + Homogenization 1.0.1
新组合：Host API 2.0.0 + 迁移后的 Homogenization 正式包
```

跨代验收不是要求新宿主加载旧插件，而是同时证明：

1. 旧宿主与旧插件的不可变正式组合仍可恢复和运行。
2. 新宿主与新插件通过全部真实链路验证。
3. 新宿主遇到旧插件时在装载前稳定拒绝，Shell 仍能启动并显示真实不兼容原因。
4. Launcher 在升级宿主前读取真实 catalog 和本机安装清单，提示必须同时准备兼容插件。
5. 操作员可以选择保持旧组合或切换到已验证新组合；不能被迫进入宿主已升级、插件不可用的半完成状态。

### 7.3 唯一兼容基线文件

SDK 仓维护：

```text
eng/compatibility-baselines/compositions.json
```

它只保存测试 pin，不复制 Cloud catalog，也不提交二进制。至少记录：

- `hostApiVersion`
- 宿主版本、artifact URL、SHA256、source commit、支持状态
- 每个 `moduleId` 的插件版本、artifact URL、SHA256、source commit、支持状态
- 已真实验证的 host/plugin 组合
- 最后验证时间和验证命令版本

Cloud catalog 是发布状态来源；该文件是“哪些不可变正式字节构成兼容回归集”的测试来源。基线条目必须由真实发布结果生成，禁止手填不存在的 URL、hash 或版本。

正式插件发布后，发布脚本输出待登记条目；只有条目进入该文件并通过唯一兼容验证，发布闭环才完成。当前代际所有仍受支持/仍有设备使用的插件都保留，不因发布新版自动删除。删除必须有明确弃用结论和设备盘点证据。

## 8. 唯一兼容验证入口

SDK 仓拟新增唯一编排入口：

```text
eng/Verify-EdgeSdkCompatibility.ps1
```

它只负责编排，不复制生产逻辑。它接受 Host/SDK/Plugins 三个 clean checkout 路径以及本次候选范围，顺序执行：

1. 记录三仓 HEAD、tree、dirty 状态、SDK package version 和候选 `hostApiVersion`。
2. 正式 pack SDK 包到本次隔离 artifact feed。
3. 让宿主和插件从该 feed restore；禁止偷偷回退 `ProjectReference` 或源目录 DLL。
4. 运行 SDK Analyzer/API baseline 和各仓受影响测试。
5. 调用插件仓唯一 `PackEdgePlugin` 生成真实 Homogenization zip、metadata 和 SHA256。
6. 检查插件包白名单、manifest、程序集引用闭包和宿主唯一 DLL 所有权。
7. 通过 Launcher/宿主生产服务把真实 zip 安装到隔离但生产同构的 runtime layout；不手工复制拼装另一种布局。
8. 使用宿主正式 resolver/ALC 发现、校验、注册和装载插件。
9. 枚举真实插件注册的全部 ViewId，逐个创建、挂载、布局、调度、关闭和释放。
10. 运行 Homogenization 对应 Conformance/Workflow 与启动非阻断验证。
11. 下载 `compositions.json` 中适用的正式宿主/插件字节，校验 hash 并执行同代兼容或跨代拒载/组合验证。
12. 生成单份 JSON/Markdown 摘要，绑定三仓 HEAD、所有输入 artifact digest、发现/执行数和失败原因。

任一正式基线不可下载、hash 不一致、页面未打开、资源/绑定 fatal、包中出现宿主 DLL、测试 Skip 或未知依赖都必须失败。禁止因为网络或 artifact 不可用而自动改用源码重编译、缓存旧文件或跳过该项。

这个入口可以被 SDK 发包、三仓 CI 和 `Deploy-Changed` 的发布前门禁调用，但底层 pack、install、load、catalog 与 upload 实现始终只有一份。

## 9. 真实 UI 兼容门

API surface 和资源 key 比较不能证明插件页面能运行。真实 UI 门必须：

1. 用候选宿主装载正式插件 zip，而非引用插件测试项目。
2. 从实际模块注册结果枚举全部 ViewId；禁止维护另一份手写页面列表。
3. 使用真实 Avalonia Headless Application、Dispatcher 和视觉树创建 View/ViewModel。
4. 应用真实资源字典、ControlTheme、Style、Converter、Asset 和语言资源。
5. 至少完成一次 attach、measure/arrange/layout 和 dispatcher drain。
6. 捕获 XAML 类型加载、StaticResource/DynamicResource、样式、资源、绑定和页面生命周期 fatal。
7. 逐个 detach/close/dispose，证明无残留生命周期异常。
8. 输出 `discoveredViews = openedViews = closedViews`，fatal 和未处理绑定错误为零。

无 PLC/MES/Cloud 环境时，页面必须显示真实空态/离线态，测试不得注入在线或生产记录。Windows Release lane 仍要在正式发布前通过真实 Launcher/Shell 打开关键页面；Headless 结果不得冒充 Windows 原生窗口、DPI、输入法或驱动现场验收。

## 10. 分阶段执行计划

每个批次默认只修改一个当时存在的仓库。进入三仓后，如某批需要跨仓写入，必须在该轮由用户明确列出允许写入的仓库/目录。任何批次失败都先停在当前 gate，不用兼容 wrapper 跨过去。

Phase 0–5 的 Git 证据采用 implementation/evidence commit pair，不得在 ledger 中伪造自引用的最终 commit：先提交本 Phase 的实现、generator、schema、测试和文档形成 implementation commit；在该提交工作树 clean 时运行唯一 generator，状态采集只允许排除 canonical ledger 自身；再以仅包含 canonical ledger 的 evidence commit 收口。ledger 记录的是 implementation commit 的 HEAD/tree，commit-pair validator 必须证明当前 HEAD 恰好只多一个 ledger-only evidence commit，该 commit 的唯一 parent 是 recorded implementation HEAD、唯一差异是 canonical ledger，且最终工作树完全 clean；Phase 0 还必须在 E 上继续通过无参数 actual formal 与 post-E canonical/full gates，commit-pair validator 单独通过不构成收口。implementation commit 前或 evidence commit 前的 noncanonical/pending smoke 只用于调试，不得冒充 final canonical gate。Phase 2 另需在迁移实现前先提交独立 canonical snapshot；因此一个 Phase 是可审的有序提交单元，不等于永远只有一个 Git commit，也不得把这里的 Git 提交与业务 UoW 的 `CommitAsync` 混为一谈。

Phase 6 至 Phase 9 是一次受控的仓库迁移窗口：期间不执行新的生产发布，现有现场仍运行迁移前已发布的不可变组合；Phase 9 恢复唯一正式发布入口后才解除发布冻结。若期间出现必须立即发布的现场修复，应中止尚未完成的迁移批次并回到迁移前 clean 基线处理，不长期维护新旧两套发布实现。

### Phase 0：冻结事实与机器账本

批次：`EDGE-SPLIT-000`

工作：

- 按 implementation/evidence commit pair 记录真实 clean implementation HEAD/tree、solution/project graph、测试 inventory、plugin manifest、正式发布 artifact 和 package 内容；`cleanObserved` 必须由 generator 调用 `git status --porcelain` 实测，禁止从输入 JSON 复制。
- 使用 MSBuild + Roslyn compilation 生成 Homogenization 依赖账本，不使用 namespace grep 代替。
- 唯一 generator 固定为 `eng/Generate-EdgePluginContractLedger.ps1`，schema 固定为 `eng/edge-plugin-contract-ledger.schema.json`，canonical ledger 固定为 `eng/baselines/edge-plugin-contract-ledger.json`；Phase 0 冻结 baseline，Phase 1–5 每批都由同一 generator 读取“前一批 canonical ledger”并校验 immediate predecessor batch/digest，从其中继承 Phase 0 exact carry、推进 lifecycle 后原位重生成当前账本，禁止跳批、平行临时脚本或手写 JSON。
- 对每个插件使用的外部 symbol 记录：源文件/行号、symbol、owner assembly、usage kind、目标处置、替代契约和保护测试。
- 同时生成四个独立可重算依赖层：evaluated `ProjectReference`（输入 csproj、完整 item、owner family、总数/禁止数/未知数）、Roslyn forbidden symbols（完整 Compile 输入与 semantic usage）、候选程序集 PE `AssemblyRef`（输入程序集、完整引用、owner family、总数/禁止数/未知数）和插件 zip packaged assemblies（package hash、逐 entry hash/size、程序集归属、总数/禁止数/未知数）。Phase 0–3 的 package 层必须显式为 `not-applicable-before-EDGE-SPLIT-040`，不得把“未评估”伪装成 evaluated zero。
- owner family 固定区分 Application、Domain、SharedKernel、Infrastructure、Presentation、Host、UiShared、SdkContract、Analyzer、PluginOwned、PlatformOrThirdParty 与 Unknown；四层输入明细、层内计数和 summary 四层计数均由 strict schema 与 validator 交叉重算，删字段、加未知字段和伪造 count 的负例必须失败。
- 生成当前公开 ViewId/资源字典/页面 inventory。
- 从真实发布位置取得并首次逐字节验证 `Host API 1.0.0 + Homogenization 1.0.1` artifact、URL、size、SHA256 和插件 archive inventory；将已验证历史构成冻结进 authority input。正常可复现生成读取冻结证据，不依赖内网端点持续在线；只有显式 `-RefreshHistoricalArtifactEvidence` 才重新下载并逐项对账，刷新失败不得改写冻结结论或回退伪 artifact。
- 冻结目标包名、目标 `hostApiVersion=2.0.0` 和三仓工作名。
- formal closure 固定为 `I（本批全部实现、测试、schema、tracked docs） → clean I 上唯一 generator → ledger-only E → E 上 no-arg actual formal`。formal parent 固定 `param()`、固定仓根与 canonical ledger，不接受 ledger/root/command/fixture/skip override，不调用 generator；`E` 必须是 `I` 的唯一直接子提交，唯一变化为 mode `100644` 的 canonical ledger，committed blob 与 worktree bytes 精确相等。E 后不得补写 tracked 文档或代码。
- actual formal 在 authority 前执行两次相等的 clean precondition，ledger 只绑定 clean I 且唯一 excluded path 为 canonical ledger；direct-child coordinator 必须产生 authority/replay `1/1` 的签名临时 machine-fact receipt，正式验证保持 `RequireFormal` 与 fast consumer 的两个 Require switches，随后验证 post-state、non-recursive exact cleanup 和最终收据原字节。该 receipt 不承载 reviewer、waiver、permission、manual approval 或 parallel authority 语义。

完成门：

- 未分类 symbol 为零。
- 未知程序集依赖为零。
- canonical ledger 必须由 clean implementation commit 可复现生成，唯一排除路径为 ledger 自身；evidence commit 只含该 ledger。当前 solution project、Compile source、runner/case、View/resource 与 manifest 数量只作为本次可重算观测值，不得硬编码成 Phase 1–5 的永久常量。
- schema v2 先记录完整 semantic-evidence identity（源路径/位置、精确 symbol、symbol kind、完整 owner assembly identity 和 usage kind），再以 `sourcePath + ownerAssembly + symbol` 作为 carry item 的 canonical 聚合身份；occurrence 是该身份下层语义证据记录数，不是源码行数、标识符文本次数或只统计 invocation 的旧口径。本批审查接受的候选值为 `EDGE-SPLIT-020` 的 `Domain.Hardware + development-sample` 22 个 exact items / 122 个 semantic-evidence occurrences，以及 `EDGE-SPLIT-030` 的私有 Presentation seam 6 个 exact items / 11 个 semantic-evidence occurrences；只有 implementation/evidence pair、E 上 actual formal 与 post-E canonical/full gates 全部完成后才正式冻结，目标批次前必须 retained-exact，到目标批次必须 closed/zero。
- `IIoT.Edge.UI.Shared` 是已批准的稳定 SDK UI surface；schema v2 候选账本中的 34 条 UiShared usages 作为稳定契约使用对账，不归入 `EDGE-SPLIT-030` 待移除 carry，不得因 Phase 3 打包/边界化重新标为 forbidden。
- 当前测试发现、执行、失败、Skip 对账完成。
- 真实旧组合 artifact 有可复验 digest；若当前尚未正式发布，则先完成合法发布基线或明确取消“上一正式版本兼容”结论。
- actual formal 的 strict result 必须通过独立 schema v1，固定报告 I/E、ledger/receipt/public-key digest、authority/replay `1/1`、双 descriptor direct-child binding、两个 Require switches、post-state 与 cleanup；`cleanupComplete=true` 只表示受控临时 run root 已精确清理，不表示删除 final receipt。既有 request/descriptor/receipt/authority-result 四份 schema v1 不因新增 formal-result 而改写。
- static/formal 终态必须由真实入口对账 11 sources、formal mutations `10/10`、formal-result `receiptPath='.artifacts/../x'` path-traversal schema negative `1/1` 与 legacy `87+22+3=112`。synthetic formal fixture 只能保护 schema/签名/ownership，不能替代 E 上无参数 actual formal。

#### Phase 0 当前候选真值（2026-07-21 文档冻结前）

- `actual formal=NOT_RUN`；`I/E=NOT_FORMED`；`Phase 0=NOT_CLOSED`。当前候选所有运行 gate 均为 `NOT_RUN`，不得引用旧 HEAD 或早期 source bytes 的通过记录冒充终态。
- 本批没有新增 `[Fact]`，只是扩展既有 Architecture Fact；最近 1334 discovery 基线尚未对当前候选重新验证。旧四份 authority schema v1 保持原字节，新 formal-result 为独立 v1。
- pre-I qualification 的精确命令与 F0 manifest key 见 `客户端架构治理清单.md` 第 0.1、8.1 节；结果只写固定工作区仓外 F0 checkpoint，不回写 ledger-only E 之后的 tracked docs。

### Phase 1：在当前仓抽取纯 Contracts 与 SDK

批次：`EDGE-SPLIT-010`

工作：

- 创建目标 Contracts/SDK/UI/Analyzer 项目，但暂不建新 Git 仓。
- 按账本逐个把真正跨边界的抽象、DTO 和通用基类迁入目标项目。
- 宿主 Application 改为实现/消费 Contracts，而不是把 Application 自己暴露给插件。
- 清除插件对 `Application.Features`、`Application.Modules` 和 `Application.Common` 实现的直接使用，但只把通过 Phase 0 final evidence 正式冻结的 development-sample carry set 留给 `EDGE-SPLIT-020`；不得新增同批之外的延期项。
- 清除 Module.Sdk 对宿主 Application/SharedKernel 的依赖；必要稳定 primitive 进入 Contracts，不把整个 SharedKernel 变成插件 API。
- 建立公共 API 递归 Analyzer 和 ProjectReference gate。
- 旧声明、旧 namespace 和临时 adapter 在 consumer 同批迁移后物理删除。

完成门：

- Homogenization 除 Phase 0 final evidence 正式冻结的 `EDGE-SPLIT-020` 与 `EDGE-SPLIT-030` 两组精确 carry set 外，对 Application、Domain、Infrastructure、Host、Presentation 实现的 ProjectReference 与 symbol 使用为零；carry set 数量不得增长。
- Module.Sdk 对宿主 Application/Domain/SharedKernel 的 ProjectReference 为零。
- Analyzer 对别名、泛型、基类、attribute、DTO 闭包和跨文件泄漏都有正反 fixture。
- 全仓原 required 测试保持真实绿；数量变化有 before/after 解释，0 Skip。

### Phase 2：Domain.Hardware → DTO 行为等价迁移

批次：`EDGE-SPLIT-020`

工作：

- Phase 1 clean/evidence 收口后、迁移代码前，先把旧实现的 canonical snapshot 作为独立 snapshot commit 冻结，建议唯一文件为 `eng/baselines/homogenization-hardware-development-sample-v1.json`。expected 必须由旧实现/旧 fixture 产生并人工复核，禁止由新 mapper 同源生成 expected/actual。snapshot 同时保存 input facts 与 materialized Domain 两层：1 台默认设备；40 条有序模板，其中 Read 35/Write 5、sort `1..35 + 101..105`、类别 10 交互/18 单点/12 连续；逐项保留 `resourceKey=null` 与 legacy remarks；并绑定同名设备复用、已有映射整套跳过、Reset 分支、UoW/commit 次数与异常结果。新 DTO normalization 与 Domain normalization 必须由独立测试/实现路径产生并分别对 snapshot；Reset/约束/级联部分必须使用真实 SQLite fixture，不能只用内存 repository。
- 独立提取 Hardware/IO Template DTO 和 contributor port。
- 宿主 Application 增加 DTO → Domain 的唯一 mapping/编排入口。
- Homogenization 只声明真实模板事实，不读取/构造 Domain 聚合。
- 对迁移前后真实模板和 materialized Domain 结果生成可复验 snapshot。
- 冻结旧源码/EF 事实：`ResetBeforeImport=true` 时在 `GetHardwareProfile()` 之前删除当前数据库全部 `NetworkDeviceEntity`，并非仅删除 Homogenization；真实 EF 关系会 cascade 删除这些设备的全部 `IoMappingEntity`、`PlcTaskBindingEntity` 与 `DeviceParamEntity`。源码中的“旧匀浆样本”注释不能缩窄真实语义。任何将 reset 改成模块级/同名设备级或漏掉级联结果的行为都属于另行授权的业务变更，本迁移不得顺手实施。
- 冻结正常路径事实：同名设备存在时复用；该设备只要存在任一 IO 映射，就整套模板跳过且不修复用户编辑；新设备写入与 IO 映射写入当前分别开启 UoW 并产生两次 durable commit。
- 目标实现必须在一个显式 UoW/session/transaction 中完成 reset、设备与映射的组合写入；允许零到多次同 transaction 的 non-durable `FlushAsync`，但成功路径最多一次最终 durable `CommitAsync`。Reset 后同事务重建相同 `PlcCode` 可能碰 unique index，允许采用 delete Flush → add/identity Flush → mapping → final Commit 的顺序；设备写入、mapping/mapping-N 写入、任一 Flush 或 final Commit 失败都必须整体 rollback，不得留下旧级联数据只删一部分、设备已提交但映射未提交或新级联数据半成品。若现有 UoW 无法表达，先扩展正式 port/测试，禁止用第二次 durable commit 冒充原子性。

完成门：

- 第 5 节全部行为等价条件满足。
- canonical snapshot 对迁移前后逐字段相等；同名设备复用、任一映射整套跳过、全库 Reset 且先于 hardware profile、SQLite 真实 cascade 清除 IoMappings/PlcTaskBindings/DeviceParams、相同 PlcCode 同 transaction 重建、正常路径一次 session/最多一次最终 durable commit、允许多次 non-durable Flush、每个故障点 rollback/零半成品均有独立行为测试。
- final schema v2 canonical 将冻结 `EDGE-SPLIT-020` 的 22 个 exact items / 122 个 semantic-evidence occurrences；Phase 2 完成后该 carry lifecycle 必须 `closed` 且相关 Application/Domain owner 在 ProjectReference、Roslyn、PE 前三层全部归零。`EDGE-SPLIT-030` 的 6 个私有 Presentation exact items / 11 个 semantic-evidence occurrences 在 Phase 2 只能 retained-exact，不得增删、替换身份或提前伪归零；UiShared 稳定 surface 不属于该 carry。
- 插件 metadata 中 `IIoT.Edge.Domain` 引用为零。
- 没有新 repository、DbContext、事务或权限旁路。
- 相关 Workflow/Conformance、Domain、Application、Persistence 测试全部通过。

### Phase 3：抽取稳定 UI 契约

批次：`EDGE-SPLIT-030`

工作：

- 把当前 `Presentation.Navigation.PluginSystem` 中真正属于插件的 ViewId、View/ViewModel registration、resource registration 和导航请求迁入 SDK/UI 包。
- 宿主 Presentation 只消费 UI registration 结果，不向插件暴露 Panels/Shell 实现。
- 以 `IIoT.Edge.UI.Shared` 已批准的稳定 SDK UI surface 为输入完成打包和边界化；宿主特有资源留宿主，稳定共享 UI 契约进入 SDK UI，具体工序资源留插件。schema v2 的 34 条 UiShared usages 是 approved/stable 对账面，不得重分类为 Phase 3 forbidden carry。
- 建立真实 View 枚举和 Headless runtime 门。

完成门：

- `EDGE-SPLIT-030` 的 6 个私有 Presentation exact items / 11 个 semantic-evidence occurrences 归零，插件对全部宿主私有 `Presentation.*` ProjectReference 和 symbol 使用为零；对 approved `IIoT.Edge.UI.Shared` 稳定契约的合法消费保留并由 package/API/runtime 门对账。
- 插件全部真实 View 可以通过新契约注册、打开和释放。
- 资源 key/API baseline 通过，同时真实 XAML runtime 门通过。
- 原 Shell 导航、语言切换和当前启用插件 DataView 行为不变。

### Phase 4：正式 SDK 包、插件白名单与运行时所有权

批次：`EDGE-SPLIT-040`

工作：

- 让目标 SDK 项目可 pack，生成真实 NuGet 包、符号包和 package metadata。
- Phase 0–5 唯一 package source 固定为本批隔离本地 feed `artifacts/edge-sdk-feed`，不得远端发布；远端私有 NuGet 权威源留到 Phase 6 取得明确授权时只裁决一次，不得同时配置 GitHub Packages 与另一受控源。
- 当前同仓过渡时先 pack 到隔离 feed，再让宿主和 Homogenization 以 PackageReference restore；不得保留同一 consumer 的 ProjectReference/PackageReference 双轨。
- 改造唯一 `PackEdgePlugin` 为程序集/文件白名单。
- 收紧宿主 resolver：共享列表只保留 SDK/UI 和批准的框架程序集，移除 Application、Domain、Presentation 等宿主内部程序集。
- 宿主发布物成为 SDK runtime DLL 唯一提供者，插件包内 SDK DLL 为零。

完成门：

- 宿主和插件均从真实 SDK nupkg 构建，源码引用和本地 DLL hint path 为零。
- 插件 zip 中宿主/SDK DLL 为零，未分类文件为零。
- 使用正式 pack、同构安装布局和正式 resolver 装载真实 Homogenization 成功。
- 重复 SDK DLL、版本漂移、缺依赖和非法宿主引用负例全部 fail-closed。

### Phase 5：唯一跨仓兼容验证闭环

批次：`EDGE-SPLIT-050`

工作：

- 建立 `Verify-EdgeSdkCompatibility.ps1` 与 `compositions.json`。
- 加入候选源码 consumer build、正式二进制回归、真实 UI、启动非阻断和跨代拒载/组合测试。
- 将现有 TestPlugin 通用门与 Homogenization 真实门明确分开汇总。
- 生成机器 JSON/Markdown 证据，绑定输入 artifact digest 和当前 HEAD。

完成门：

- 单命令从 SDK pack 一直跑到真实插件 UI 关闭，全程无第二套实现。
- 同代正式插件全部通过；跨代组合按第 7 节通过。
- 正式基线不可用、hash 不符或页面漏开时稳定失败。
- 该门在 SDK 正式发包和后续生产发布前不可绕过。

### Phase 6：先拆 SDK 仓

批次：`EDGE-SPLIT-060`

工作：

- 在临时 clone 中用可复核的历史提取方式创建 SDK 仓，禁止直接破坏当前工作树历史。
- SDK 仓建立独立 solution、Directory.Build/Packages、NuGet 配置、Analyzer、测试、API baseline 和 pack workflow。
- 发布首个正式 SDK 代际包。
- 当前 EdgeClient 仍保留 Host+Plugin，但全部从正式 SDK 源 restore，并通过 Phase 5。
- 成功后从 EdgeClient 删除 SDK 源码副本、solution 项目、旧测试归属和 build 输入。

完成门：

- SDK 仓 clean checkout 独立 build/test/pack。
- EdgeClient clean checkout 只靠正式 SDK 包 build/test。
- 两仓源码重复为零；不保留 vendored SDK、git submodule 或源码 fallback。
- 远端建仓、push 和 package publish 必须由执行当轮明确授权，本计划不自动授权。

### Phase 7：再拆私有插件仓

批次：`EDGE-SPLIT-070`

工作：

- 用临时 clone 提取 Homogenization 源码、配置、资源、四类真实测试及其真正 plugin-owned TestSupport。
- 插件仓建立独立 solution、SDK restore、真实 pack、Conformance/Workflow/UI 和 artifact 验证。
- `PackEdgePlugin` 的唯一实现迁入插件仓；宿主仓只保留 runtime validation，不保留 pack 副本。
- Host 仓保留中性 TestPlugin 作为通用宿主 fixture；真实 Homogenization fixture 和业务断言全部从 Host 仓删除。
- 更新三仓测试 inventory 和回归迁移账本。

完成门：

- 插件仓 clean checkout 能从正式 SDK 包 build/test/pack Homogenization。
- Host 仓具体工序源码、资源、测试、配置和发布输入为零。
- Host 通用测试不包含 Homogenization 名称或业务数据。
- 三仓联合真实链路通过，且所有移动测试只在新 owner 执行一次。

### Phase 8：宿主清理与独立仓联合验收

批次：`EDGE-SPLIT-080`

工作：

- 删除宿主仓所有已迁空项目、路径、solution entry、build target、package map 和文档旧入口。
- 将宿主项目图/Analyzer 更新为“Host → SDK package；Host 禁止具体插件”的目标矩阵。
- 在三个独立 clean checkout 上重新生成测试 inventory、compatibility/API baseline 和 package digest。
- 验证真实启动、拒载、诊断、当前组合和旧组合恢复。

完成门：

- 三个仓库分别 clean，独立 build/test 成功。
- 跨仓没有相对源码路径、ProjectReference、DLL hint path、共享 bin/obj 或隐式工作区回退。
- 三仓联合验收只交换正式包和 digest。
- 全部旧入口零引用，文档链接无死链。

### Phase 9：改造现有唯一发布入口

批次：`EDGE-SPLIT-090`

这是部署/发布脚本改动批，实施前必须重新读取部署总览、客户端部署和安装更新验收，并取得当轮明确授权。

工作：

- 保持工作区对外唯一入口 `deploy/Deploy-Changed.ps1`。
- 让影响分析识别三个仓的生产基线、候选 HEAD 和依赖闭包。
- Host 改动只发布 Host；Plugin 改动只发布指定 `ModuleId` 插件。
- SDK runtime/API 改动至少影响 Host，并先跑兼容门；只有需要新 API 的插件才发布新插件版本，禁止无条件全量插件重发。
- Analyzer/docs-only SDK 改动不得触发 Host/Plugin 生产发布。
- Plugin 发布内部调用插件仓唯一 pack；Host 仓不得重建插件。
- 继续使用 Cloud Human HTTP API、真实 release notes、catalog/DB/静态 HEAD、互斥锁和 `-ResumeReleaseRoot`；禁止恢复 `scp/rsync` 或直接执行项目内部脚本。

完成门：

- 对外仍只有一个发布入口。
- 影响不明时 fail-closed，不退化全量。
- 包、hash、release metadata、Cloud catalog 和下载字节一致。
- 所有部署 policy/behavior/preflight/Windows 下载验收通过；未执行的生产/Windows/现场项目明确标为未执行。

### Phase 10：Launcher 组合升级保护

批次：`EDGE-SPLIT-100`

工作：

- 升级宿主前，用真实 Cloud catalog 和本机安装清单计算每个已启用插件的兼容性。
- 若目标宿主会使当前插件失效，Launcher 在下载/替换前明确提示目标插件版本和所需组合。
- 插件包尚未下载、hash 未验证或组合不完整时，不允许先替换宿主。
- 允许保持旧组合或选择已验证的新组合；Shell 运行时不替换插件。
- 安装失败使用既有 staging/原子替换恢复已验证组合，不实现进程内旧 API fallback。

完成门：

- `Host 1.x + Plugin 1.0.1` 和目标新组合均有不可变真实证据。
- 不兼容组合在安装前被阻止，在手工放入错误包时由宿主拒载且启动非阻断。
- UI 不显示伪造“可升级”“已兼容”或“已安装”状态。

## 11. 各仓测试与 CI 归属

### 11.1 SDK 仓

- Analyzer/AnalyzerTests、API surface baseline、package metadata、Contracts/SDK/UI 单元与 Headless 测试。
- SDK pack 产物检查。
- 正式发包前调用唯一跨仓兼容验证。

### 11.2 Host 仓

- Domain、Application、Infrastructure、Startup、Shell/Launcher/Installer、Deployment 和中性 TestPlugin Conformance。
- 正式 resolver、安装服务、非阻断诊断和宿主唯一 DLL 所有权。
- 不包含真实工序 Workflow 或 Homogenization 业务断言。

### 11.3 Plugins 仓

- Homogenization 及未来插件的业务 Unit/Application/Workflow/Conformance/UI。
- 插件源码依赖 Analyzer。
- 唯一正式插件 pack、zip 白名单和 artifact metadata 测试。
- 每个插件只测试自己的业务；跨插件通用机制回 SDK/Host，不建立 `*.Shared` 具体业务工程。

### 11.4 CI 不形成多条链

各仓可以有自己的快速 build/test job，但跨仓完成证据只来自 `Verify-EdgeSdkCompatibility.ps1`。CI 不通过 webhook 互相触发一串隐藏流程；SDK 发包和生产发布入口显式调用同一验证命令即可。

PR 不连接生产和现场。真实 PLC/MES/Cloud/Windows 现场属于 Release/Manual/LiveExternal lane，必须另行授权；未执行就明确写未执行，不能用 mock 或 Headless 冒充。

## 12. 现在必须做与明确推迟

### 12.1 本次拆仓必须完成

1. 机器依赖账本和契约清单。
2. Contracts/SDK/UI/Analyzer 边界抽取。
3. Domain.Hardware → DTO 行为等价迁移。
4. 正式 SDK 包消费、插件静态打包白名单和宿主唯一 DLL 所有权。
5. 真实 Homogenization 包、真实 View、真实启动和正式二进制兼容门。
6. 唯一组合基线 JSON 和唯一兼容验证命令。
7. SDK 仓、私有插件仓、Host 仓按顺序物理分家并清理旧入口。
8. 现有唯一发布入口适配三仓影响映射。
9. 跨代升级前组合保护。

### 12.2 插件/团队规模扩大后再做

- GitHub App、跨仓 webhook 或集中式兼容性服务。
- 多机器并行插件矩阵和质量仪表盘。
- 自动生成组织级审批、CODEOWNERS 或第二 reviewer 流程。
- 同一进程并行装载多个 SDK 代际。
- 为尚不存在的大量第三方插件建立开发者门户或市场治理后台。

推迟的是组织型自动化，不是测试真实性、打包白名单、版本兼容或现场保护。

## 13. 风险、停止条件与恢复

| 风险 | 机械发现 | 处理 |
|---|---|---|
| 契约继续泄漏宿主类型 | Roslyn 公共面递归门、project graph | 停在当前批，缩窄/重塑契约，不加 wrapper |
| Domain → DTO 改变业务模板 | before/after snapshot、Workflow/Domain/Persistence | 独立回退该批，重新裁决 DTO，不继续拆仓 |
| XAML/API 看似兼容但页面崩溃 | 真实 View runtime 门 | 阻断 SDK/Host 发包，修 SDK UI 或插件 |
| 插件包重复带 SDK DLL | zip 白名单与 PE reference closure | pack 失败，禁止靠 ALC 优先级掩盖 |
| 上一正式 artifact 不可得 | URL/SHA 下载校验 | 兼容结论失败，先修正式 artifact 留存 |
| 三仓部分迁移导致双源码 | 路径、namespace、package/project graph 零重复扫描 | 未清零不得关批，不保留临时 vendoring |
| 跨代现场半升级 | Launcher 组合预检、staging/原子替换 | 保持旧组合，未备齐新插件不先升 Host |
| 发布链被复制 | deployment policy 与入口扫描 | 删除重复入口，继续收口 `Deploy-Changed` |

恢复原则：

- 物理拆仓前按小批次回退，不创建 legacy 层。
- 已发布 artifact 保持不可变；失败时回到上一已验证 Host/Plugin 组合。
- SDK 包发布失败不覆盖已发布同版本，必须升新 SemVer。
- 插件/宿主不兼容只做拒载、诊断和组合切换，不偷偷加载另一份 DLL。

## 14. 批次验证命令

Phase 0 当前使用以下已存在入口分段执行；本节所有命令当前均为 `NOT_RUN`，不能只选择其中一段宣布完成。

### 14.1 Dirty candidate qualification

```bash
pwsh -NoLogo -NoProfile -NonInteractive -File scripts/tests/Test-EdgePluginContractStaticGuard.ps1 -RepositoryRoot .
pwsh -NoLogo -NoProfile -File scripts/tests/Test-EdgePluginContractLedgerPrimitives.ps1 -RepositoryRoot .
pwsh -NoLogo -NoProfile -NonInteractive -File scripts/tests/Test-EdgePluginContractAuthorityProtocol.ps1 -RepositoryRoot . -LedgerPath .artifacts/phase0-ledger-dev-3.json
pwsh -NoLogo -NoProfile -NonInteractive -File scripts/tests/Invoke-EdgePluginContractDevelopmentValidation.ps1 -RepositoryRoot . -Configuration Release -CurrentBatch EDGE-SPLIT-000 -AuthorityTimeoutSeconds 900
dotnet build src/Tests/IIoT.Edge.Architecture.Tests/IIoT.Edge.Architecture.Tests.csproj -c Release --no-restore -t:Rebuild --disable-build-servers --nologo -noAutoResponse /p:UseSharedCompilation=false /nodeReuse:false
dotnet test src/Tests/IIoT.Edge.Architecture.Tests/IIoT.Edge.Architecture.Tests.csproj -c Release --no-build --no-restore --disable-build-servers --nologo --filter "FullyQualifiedName=IIoT.Edge.Architecture.Tests.EdgePluginContractLedgerTests.CanonicalLedgerValidator_ShouldRejectSemanticBypassFixtures"
```

顺序固定为 StaticGuard → Primitives → AuthorityProtocol synthetic → Development → forced rebuild/freshness → exact xUnit；synthetic 不得替代 actual formal。DLL freshness 与 F0 checkpoint 字段按 `客户端架构治理清单.md` 第 8.1 节执行。

### 14.2 Clean `I` 上唯一 generator

```bash
pwsh -NoLogo -NoProfile -NonInteractive -File eng/Generate-EdgePluginContractLedger.ps1 -PluginProject src/Modules/IIoT.Edge.Module.Homogenization/IIoT.Edge.Module.Homogenization.csproj -OutputPath eng/baselines/edge-plugin-contract-ledger.json -Configuration Release -CurrentBatch EDGE-SPLIT-000
```

全部实现、测试、schema 和 tracked docs 必须先进入 clean 本地 `I`；generator 后只允许 canonical ledger 成为差异，当前仍禁止 push。

### 14.3 Ledger-only `E` 与 actual formal

```bash
pwsh -NoLogo -NoProfile -NonInteractive -File scripts/tests/Test-EdgePluginContractLedger.ps1 -RepositoryRoot . -LedgerPath eng/baselines/edge-plugin-contract-ledger.json -CommitPairGateOnly
pwsh -NoLogo -NoProfile -NonInteractive -File scripts/tests/Invoke-EdgePluginContractFormalValidation.ps1
pwsh -NoLogo -NoProfile -NonInteractive -File scripts/tests/Test-EdgePluginContractLedger.ps1 -RepositoryRoot . -LedgerPath eng/baselines/edge-plugin-contract-ledger.json
```

第二条是 E 上 actual formal，必须保持无参数；第三条是 actual formal 成功后的 canonical validator。随后必须继续执行 `客户端架构治理清单.md` 第 8.4 节的 post-E full gates，才可评估 Phase 0 是否关闭。

### 14.4 后续 Phase 命令

以下唯一跨仓兼容入口属于后续 Phase，在对应实现创建前只保留为计划契约，不得写成当前已存在或已通过：

```powershell

pwsh ./eng/Verify-EdgeSdkCompatibility.ps1 `
  -HostRepository <IIoT.EdgeClient> `
  -SdkRepository <IIoT.Edge.Sdk> `
  -PluginsRepository <IIoT.Edge.Plugins.Private> `
  -Configuration Release `
  -TargetRuntime win-x64
```

Phase 0 不运行该未来入口，也不以它替代当前 I/E/formal/post-E 链。

## 15. 最终完成定义

只有以下条件全部满足，才能宣布三仓拆分完成：

- 三个仓库均可从独立 clean checkout build/test，所需 SDK 只来自唯一正式包源。
- Homogenization 的 Host/Application/Domain/Infrastructure/Presentation ProjectReference 和 symbol 使用全部为零。
- SDK 公共面没有宿主实现或具体工序类型，Analyzer/API gate 正反例齐全。
- Host 不含真实工序源码/测试/资源，Plugins 不含 Host/SDK 源码副本，SDK 不反向依赖两端。
- 插件 zip 使用静态白名单，SDK/Host DLL、测试、现场数据和未分类文件为零。
- 宿主唯一提供 SDK runtime DLL，正式 resolver/ALC 无插件目录 SDK fallback。
- 当前真实 Homogenization 包全部 View 可打开/关闭，Workflow/Conformance 通过，未连接外部系统时状态真实。
- 同代所有仍受支持正式插件二进制通过；跨代旧组合、新组合、拒载和 Launcher 组合预检通过。
- `compositions.json` 的 URL、SHA、版本和 source commit 均来自真实不可变发布产物。
- 三仓测试 inventory 完成 before/after 对账，required `failed=0`、`skipped=0`，没有 dummy 或双跑旧入口。
- 生产对外仍只有 `deploy/Deploy-Changed.ps1`；Host/Plugin/SDK 影响闭包准确，未知影响 fail-closed。
- 部署、Windows 实机、真实 PLC/MES/Cloud 或生产操作未执行时明确记录，未被 Headless、mock 或文档冒充完成。
- 三仓对应规则、架构契约、部署文档和滚动复盘已更新，旧入口/死链/旧项目名零命中。

## 16. Phase 0 候选的命名与版本事项

以下四项已通过 Phase 0 审查并进入 schema v2 候选实现；当前仍未完成 `I → clean I 上 canonical 生成 → ledger-only E → E 上 commit-pair gate → E 上无参数 actual formal → post-E canonical/full gates` 证据链，因此不得提前称为 Phase 0 已正式冻结或收口。只有 actual formal 的 strict result 与 post-E full gates 同时通过后，Phase 0–5 才可消费该冻结输入且不得漂移：

1. 三个目标仓名采用 `IIoT.EdgeClient`、`IIoT.Edge.Sdk`、`IIoT.Edge.Plugins.Private`。
2. Phase 0–5 只用 `artifacts/edge-sdk-feed` 隔离本地源且禁止远端发布；Phase 6 再取得授权裁决唯一远端私有源。
3. 新契约代际固定为 `hostApiVersion=2.0.0`。
4. 旧组合候选固定为正式 Host `1.0.29 / API 1.0.0` 与 Homogenization `1.0.1`：catalog、manifest、Setup 与插件 zip 的 URL、bytes、SHA256、source commit 和旧包内容须由 final canonical ledger 复验。

E 上无参数 actual formal 与 post-E canonical/full gates 全部完成后，Phase 0–5 才能按序消费该账本；不得用 final validator、synthetic、旧 gate 或文档结论单独闭合证据链，也不允许跳过或扩大 carry set。Phase 6 之后仍需用户另行授权。
