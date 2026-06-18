# Edge 客户端宿主插件分发契约

本文档定义 EdgeClient 从“按工序整包”升级为“通用宿主 + 外部插件目录 + 云端插件 catalog”的阶段契约。它约束后续 Edge 与 Cloud 的实现，避免下载中心、版本盘点和插件更新建立在错误的整包模型上。

## 1. 目标模型

Edge 现场安装由两层组成：

- 通用宿主：Launcher、Shell、共享运行时、Velopack 自更新能力。
- 工序插件：按机器 profile 选择安装，可同时安装多个工序，独立声明版本和宿主兼容范围。

开发机和现场机都遵守同一布局：宿主目录保持干净，插件只放在与宿主并列的 `plugins/`。现场机通过 `launcher.profiles.json`、`Shell__MachineProfile`、`Modules:PluginRoots` 和 `Modules:Enabled` 控制可见工序与启用范围。

## 2. 不变量

- `ClientCode` 与 `BootstrapSecret` 绝不进入公共安装包、插件包、catalog 或下载中心静态文件。
- 插件不得安装到 Velopack 管理的程序目录；宿主更新或回滚不得删除外部插件目录。
- 插件下载、安装、版本上报和 catalog 获取失败不得阻断 Launcher/Shell 启动。
- 更新链路不得混入 Cloud/MES 上传、bootstrap、权限或生产数据补偿链路。
- 插件包是远程代码载体。生产前必须启用 catalog 或包签名校验；MVP 阶段至少保留签名字段并执行 SHA256 完整性校验。

## 3. 目录布局

安装目录固定为单宿主布局：

```text
install-root/
  launcher/
    launcher.profiles.json
  host/
    IIoT.Edge.Shell(.exe/.dll)
    appsettings.json
    appsettings.machine.<MachineProfile>.json
  plugins/
    <ModuleId>/
      plugin.json
      <module assemblies>
      install.json
  data/
```

`host/` 只能包含宿主及宿主配置，严禁出现 `Modules/` 或任何工序插件。Shell 默认从 `host/../plugins` 发现插件，也可以通过 `Modules:PluginRoots` 显式配置插件根路径。`Modules:Enabled` 为空时不自动加载全部插件，必须提示诊断问题。

`launcher.profiles.json` 中所有工序的 `ExecutablePath` 必须指向同一份宿主：

```json
{
  "ProfileId": "HomogenizationLine",
  "DisplayName": "匀浆",
  "MachineProfile": "HomogenizationLine",
  "ExecutablePath": "../host/IIoT.Edge.Shell"
}
```

安装素材清单使用 `schemaVersion=2`：

```json
{
  "schemaVersion": 2,
  "launcherDirectory": "launcher",
  "hostDirectory": "host",
  "pluginsRoot": "plugins",
  "modules": [
    {
      "moduleId": "Homogenization",
      "pluginDirectory": "Homogenization"
    }
  ]
}
```

旧的 `layout.zip`、`runtimeDirectory`、`runtime/Modules`、每工序一份宿主目录模型全部废弃，不得在新代码、脚本或文档中继续作为生产契约。

## 4. Catalog v2 契约

云端客户端版本 catalog 使用 v2 JSON，返回“宿主组件 + 插件组件”的版本仓库视图。客户端基于 `versions[]` 和本机已安装版本计算当前版本、目标版本、可回退版本和兼容状态；云端不得再返回 v1 派生字段或扁平列表。

根结构：

```json
{
  "catalogSchemaVersion": 2,
  "channel": "stable",
  "targetRuntime": "win-x64",
  "host": {
    "componentKind": "Host",
    "displayName": "Edge Host",
    "versions": []
  },
  "plugins": [],
  "generatedAtUtc": "2026-06-17T00:00:00Z",
  "hostUpdateSource": "https://example.com/edge-updates/velopack/stable/"
}
```

宿主版本条目：

```json
{
  "version": "1.0.0",
  "hostApiVersion": "1.0.0",
  "targetRuntime": "win-x64",
  "targetFramework": "net10.0",
  "downloadUrl": "https://example.com/edge-updates/host/1.0.0/package.nupkg",
  "sha256": "",
  "packageSize": 0,
  "status": "Published",
  "releaseNotes": "",
  "signature": "",
  "publisher": "IIoT"
}
```

插件组件与版本条目：

```json
{
  "componentKind": "Plugin",
  "moduleId": "Homogenization",
  "displayName": "匀浆",
  "description": "",
  "iconKind": "Cog",
  "accentColor": "#0F766E",
  "versions": [
    {
      "version": "1.0.0",
      "hostApiVersion": "1.0.0",
      "minHostVersion": "1.0.0",
      "maxHostVersion": "1.0.99",
      "targetRuntime": "win-x64",
      "targetFramework": "net10.0",
      "downloadUrl": "https://example.com/edge-updates/plugins/Homogenization/1.0.0/package.zip",
      "sha256": "",
      "packageSize": 0,
      "dependencies": [],
      "status": "Published",
      "releaseNotes": "",
      "signature": "",
      "publisher": "IIoT"
    }
  ]
}
```

版本状态只允许：

```text
Draft | Published | Deprecated | Archived
```

- `Published`：进入 catalog，可安装、更新和回退。
- `Deprecated`：进入 catalog，可下载，但 UI 必须显示降级/不推荐语义，默认不作为推荐目标。
- `Archived`：不进入 Edge catalog，包文件允许被异步清理。
- `Draft`：只允许 Human 管理端查看，不进入 Edge catalog。

发布保留上限由云端统一配置 `EdgeRelease:MaxVersionsPerComponent`，默认 5。发布新版本后，云端对同一组件、同一 channel、同一 targetRuntime 执行保留策略：超过上限的老版本如果没有设备上报使用则归档；仍有设备使用则降级为 `Deprecated`，等待管理员处理。

客户端 Application 层输出给 UI 的版本计划结构必须表达多版本，不允许 UI 自己猜：

```text
EdgeComponentVersionPlan
  ComponentKind: Host | Plugin
  ModuleId
  DisplayName
  CurrentVersion
  Versions: EdgeVersionOption[]

EdgeVersionOption
  Version
  Status: NotInstalled | Current | Newer | Older | InstalledNewer | Incompatible | Deprecated
  CanApply
  CompatibilityIssue
```

所有安装、更新和回退都必须显式选择版本：

```text
CheckReleaseCatalogAsync(target)
ApplyPluginVersionAsync(target, moduleId, version)
ApplyHostVersionAsync(target, version)
ApplyVersionCompositionAsync(target, selection)
```

旧的单版本接口、单版本计划和 v1 扁平 catalog 模型全部废弃，不得保留 wrapper。

### 4.1 云端安装包载荷约束

Cloud 生成的 Windows 安装包是一个单文件安装器：安装器 stub 后追加 payload zip 和 trailer。payload zip 内统一携带本次安装所需的宿主、插件和绑定配置，不允许把配置文件散落成独立下载项。

payload 必须包含：

```text
launcher/
  iiot-binding.json
  iiot-enabled-plugins.json
  launcher.update.json
host/
plugins/<ModuleId>/
  iiot-plugin-binding.json
```

安装器只负责解包、调用 Velopack Setup、复制引导文件和启动 Launcher。`ClientCode`、`BootstrapSecret` 等绑定信息只存在于受控 payload 的绑定文件中，不写 UI、不写日志、不进入公共 catalog。

## 5. 兼容语义

宿主与插件保留两道门，但语义必须分清：

- `hostApiVersion`：硬契约，必须精确匹配。只有宿主和插件之间的运行 API/ABI 契约变化才递增。
- `minHostVersion` / `maxHostVersion`：运行兼容窗口，用于约束宿主功能版本或 bugfix 范围。

禁止用 `maxHostVersion=99.0.0` 假装已经验证未来所有宿主版本。开发阶段可以暂时保留宽松范围，但发布 catalog 时必须显式标注该范围是测试范围，不得作为生产兼容证明。

宿主回滚到旧 API 后，不兼容插件必须被拒载，并在 Launcher 或诊断页显示“因宿主版本不兼容已禁用”，不能静默丢失。

## 6. 设备版本上报契约

设备版本上报是非阻断链路。建议字段：

```json
{
  "deviceId": "00000000-0000-0000-0000-000000000000",
  "clientCode": "EDGE-0001",
  "machineProfile": "HomogenizationLine",
  "channel": "stable",
  "hostVersion": "0.1.0",
  "hostApiVersion": "1.0.0",
  "installedPlugins": [
    {
      "moduleId": "Homogenization",
      "version": "1.0.0",
      "hostApiVersion": "1.0.0"
    }
  ],
  "enabledPlugins": ["Homogenization"],
  "reportedAtUtc": "2026-06-08T00:00:00Z"
}
```

上报失败只允许写日志、更新诊断或进入独立非阻断重试，不得阻断启动、PLC/MES/Cloud 生产链路。

## 7. 签名策略

SHA256 只能证明下载文件未损坏，不能证明来源可信。生产前必须至少采用一种来源真实性校验：

- catalog 签名：云端发布 `catalog.json` 与签名，客户端内置公钥验签，再按 catalog 中 SHA256 校验包。
- 包签名：插件包或 DLL 进行签名，客户端安装前验证签名和发布者。

MVP 阶段可以先实现 TLS + SHA256 + 签名字段占位，但必须保留拒绝未签名生产包的路线。

## 8. Phase 1 边界

Phase 1 只完成 Edge 地基：

- 宿主版本来自真实程序集版本，`hostApiVersion` 保留为独立契约值。
- Shell 从配置化 `Modules:PluginRoots` 发现插件，默认 `../plugins`。
- Shell 加载所配置插件根中的 `*.module.json` 默认配置；后配置的插件根可以覆盖前配置的插件根，应用配置和外部机器配置仍然优先。
- 发布脚本输出 `launcher/ + host/ + plugins/ + data/`，并生成 `installer-artifact.json` v2。
- 增加测试覆盖单 host 布局、host 无 `Modules/`、配置化插件路径、真实宿主版本格式。

Cloud 下载中心、插件选择安装、设备盘点和版本上报属于后续阶段。

### 8.1 CI artifact 发布契约

EdgeClient 的交付物是 Windows 安装器、安装素材和 Velopack 更新包，不是 Docker 镜像。CI/CD 不允许推 Harbor、GHCR，也不允许从 GitHub hosted runner 通过 SSH/SCP 直连内网服务器。

`push main` 只跑 smoke 编译和测试，不生成安装包。完整 GitHub 打包只在 `workflow_dispatch` 或 `edge-v*` / `v*` tag 时执行。日常快发可由操作者本机运行 `scripts/LocalPublishAndDeploy.ps1`，本机完成编译、Velopack 打包和 installer artifact 生成后，通过 rsync/scp 发布到 `/srv/iiot/edge-updates`；该路径不属于 GitHub CI/CD job。生产服务器只允许 `stable` 渠道，发布脚本必须拒绝并清理非 `stable` 渠道目录。

正式 GitHub 发布分两段：

```text
windows-latest
-> PublishEdgeRuntime.ps1
-> PackEdgeClientVelopack.ps1
-> PublishEdgeClientInstallerArtifact.ps1
-> upload edge-runtime-package / edge-installer-artifact / edge-velopack-releases

[self-hosted, iiot-linux-prod]
-> download GitHub Actions artifacts
-> copy installers/stable/<version> and velopack/stable to /srv/iiot/edge-updates
```

内网 Linux runner 只做文件分发，不重新构建 EdgeClient。原因是 Avalonia 桌面应用、Windows installer 和 Velopack Windows 包必须在 Windows runner 上构建和验证。Linux runner 必须用非 root 专用用户运行，并只授予 Docker 之外的最小文件权限：读取 Actions 工作目录、写入 `/srv/iiot/edge-updates`。

版本号由打包入口显式指定。`workflow_dispatch` 必须输入生产版本号，tag 触发时版本来自 `edge-v*` 或 `v*` tag，本机快发由操作者显式传 `-Version`；`PublishEdgeRuntime.ps1 -Version` 会同步设置 Launcher/Shell runtime 的 `AssemblyVersion`、`FileVersion` 和 `InformationalVersion`，Velopack 包验收以该版本为准。不要把 runtime 程序集版本固定回 `1.0.0.0` 后再发布 Velopack 包。

发布目录固定为：

```text
edge-updates/
  installers/stable/<version>/installer-artifact.json
  installers/stable/<version>/IIoT.Edge.Setup.exe
  installers/stable/<version>/launcher/
  installers/stable/<version>/host/
  installers/stable/<version>/plugins/
  installers/stable/<version>/velopack/
  velopack/stable/releases.stable.json
  velopack/stable/assets.stable.json
```

Cloud 端通过只读挂载扫描 `edge-updates/installers/stable/<version>/installer-artifact.json`，并把文件版本合并到公开下载目录、Edge catalog 和 Human catalog。数据库 release 记录仍是状态管理来源：同 key 数据库记录优先，Draft/Archived 可抑制已经落盘的文件版本。

## 9. Phase 2 Cloud 落地

Phase 2 在云端建立 catalog、下载中心基础页和设备版本盘点能力。Cloud 负责“管版本、发 catalog、收上报、看差异”，不负责本阶段的 Launcher 插件安装。

### 9.1 数据表

Cloud 新增四类表，不改 `devices` 主表字段：

- `edge_client_host_releases`：通用宿主发布记录。
- `edge_client_plugin_releases`：工序插件发布记录。
- `edge_device_client_version_snapshots`：设备最近一次客户端版本上报快照，按 `device_id` 唯一。
- `edge_device_client_plugin_versions`：最近一次上报中的插件明细。

发布记录状态统一为：

```text
Draft | Published | Deprecated | Archived
```

### 9.2 Human API

后台管理 API：

```text
GET  /api/v1/human/client-releases/catalog?channel=stable&targetRuntime=win-x64&onlyPublished=false
POST /api/v1/human/client-releases/host-releases
POST /api/v1/human/client-releases/plugin-releases
GET  /api/v1/human/client-releases/device-inventory?channel=stable&targetRuntime=win-x64&keyword=
```

读接口使用 `Device.Read` 权限。写接口使用 `Device.Update` 并要求管理员，避免在 Phase 2 引入新的权限种子和角色迁移。

`POST host-releases` 允许录入或更新同一 `(channel, version, targetRuntime)` 的宿主发布记录。`POST plugin-releases` 允许录入或更新同一 `(moduleId, channel, version, targetRuntime)` 的插件发布记录。MVP 不上传文件，只录入下载地址、SHA256、包大小、签名字段、发布者和 release notes。

### 9.3 Edge API

Edge 已 bootstrap 后使用设备 token 调用：

```text
GET  /api/v1/edge/client-releases/device/{deviceId}/catalog?channel=stable&targetRuntime=win-x64
POST /api/v1/edge/client-releases/version-reports
```

这两个接口都走 `RequireEdgeDeviceToken` 和现有 `DeviceBindingBehavior`。也就是说，请求中的 `DeviceId` 必须和 token 中的 `device_id` 一致。

版本上报会额外校验 `DeviceId + ClientCode` 是否匹配云端设备身份，防止把 `ClientCode` 当成归档主键或跨设备混用。

### 9.4 Catalog v2 返回

```json
{
  "catalogSchemaVersion": 2,
  "channel": "stable",
  "targetRuntime": "win-x64",
  "host": {
    "componentKind": "Host",
    "displayName": "Edge Host",
    "versions": []
  },
  "plugins": [],
  "generatedAtUtc": "2026-06-08T00:00:00Z"
}
```

Edge catalog 返回 `Published` 和 `Deprecated` 发布记录，按组件分组并受 `EdgeRelease:MaxVersionsPerComponent` 保留策略限制。Human catalog 可通过 `includeArchived` 查看归档记录，但 Edge catalog 永远不返回 `Archived`。

### 9.5 设备版本盘点

设备盘点页基于最近一次版本上报和当前 catalog 计算：

- 宿主当前版本 vs 最新宿主版本。
- 插件当前版本 vs 最新插件版本。
- `hostApiVersion` 不匹配。
- 宿主版本不在插件 `[minHostVersion, maxHostVersion]` 窗口。
- 未上报、无发布、可更新、已最新、不兼容等状态。

版本上报不代表设备在线，不写设备在线状态，不参与 Cloud/MES 上传链路。

### 9.6 Phase 2 未完成项

以下内容仍属后续阶段：

- Launcher 拉取 catalog 并选择插件。
- 插件下载、staging、校验、安装到布局级 `plugins/<ModuleId>/`。
- 插件更新失败回滚和宿主回滚后的 UX。
- catalog 或包签名的生产级验签。
- Windows 首装实机下载和更新验收。

## 10. Phase 3 Edge 闭环

Phase 3 打通 Launcher 到 Cloud catalog 的客户端闭环。Cloud 仍沿用 Phase 2 的 Edge API；本阶段客户端不新增 Cloud 业务写链路。

### 10.1 Launcher 配置来源

Launcher 读取以下配置后才会启用云端插件 catalog：

- `CloudApi:BaseUrl`
- `CloudApi:ClientCode`
- `CloudApi:BootstrapSecret`
- `CloudApi:Paths:DeviceInstance`
- `CloudApi:Paths:ClientReleaseCatalogTemplate`
- `CloudApi:Paths:ClientVersionReport`
- `launcher.update.json` 中的 `channel`、`targetRuntime`

`ClientReleaseCatalogTemplate` 必须包含 `{deviceId}`，默认值为：

```text
/api/v1/edge/client-releases/device/{deviceId}/catalog
```

版本上报默认路径为：

```text
/api/v1/edge/client-releases/version-reports
```

配置缺失、Cloud 不通、bootstrap 失败、catalog 拉取失败或版本上报失败均为非阻断，只影响插件更新面板状态，不阻断 Launcher 或 Shell 启动。

### 10.2 首次安装/选择插件流程

Windows 首次安装由 Cloud 生成绑定安装包，payload 内包含 `launcher/`、一份 `host/`、所选 `plugins/<ModuleId>/` 和本次生成的绑定 JSON。操作员打开 Launcher 后：

1. Launcher 使用 `ClientCode + BootstrapSecret` bootstrap 当前设备，获取设备 token 和 `deviceId`。
2. Launcher 使用设备 token 拉取 catalog，并按 `channel`、`targetRuntime` 读取可用宿主和插件版本。
3. Launcher 扫描布局级 `plugins/`，计算当前插件版本、云端最新版本和兼容状态。
4. 操作员选择安装或更新某个工序插件。
5. Launcher 连带解析 `dependencies[]`，下载目标插件和依赖插件包。
6. 每个插件包先写入 staging，校验包 SHA256、zip 路径安全、`plugin.json` 与 catalog 一致、入口程序集存在、`hostApiVersion` 精确匹配、宿主版本落在 `[minHostVersion, maxHostVersion]`。
7. 校验通过后替换布局级目录 `plugins/<ModuleId>/`，并保留安装摘要 `install.json`。
8. Launcher 写外部机器配置 `Modules:Enabled`，启用已安装插件。
9. Launcher 上报宿主版本、`HostApiVersion`、已安装插件版本和启用插件列表。

Shell 正在运行时不得替换插件。Launcher 必须提示关闭 Shell 后再安装或更新插件。

### 10.3 手动检查更新流程

后续启动或手动点击“插件更新”时，Launcher 重新拉取 catalog 并显示两级差异：

- 宿主：当前宿主版本、Cloud 最新宿主版本。
- 插件：当前版本、Cloud 最新版本、包大小、安装/更新/已最新/不兼容状态。

插件更新仍走 staging 校验和原子替换流程。宿主更新继续使用既有 Velopack 更新链路；Phase 3 只把 Cloud catalog 中的最新宿主版本纳入 Launcher 的两级版本展示。

### 10.4 版本上报内容

Phase 3 客户端实际上报：

```json
{
  "deviceId": "00000000-0000-0000-0000-000000000000",
  "clientCode": "EDGE-0001",
  "machineProfile": "HomogenizationLine",
  "channel": "stable",
  "hostVersion": "0.1.0",
  "hostApiVersion": "1.0.0",
  "installedPlugins": [
    {
      "moduleId": "Homogenization",
      "processType": "Homogenization",
      "version": "1.0.0",
      "hostApiVersion": "1.0.0"
    }
  ],
  "enabledPlugins": ["Homogenization"],
  "reportedAtUtc": "2026-06-08T00:00:00Z"
}
```

Launcher 登录后和 Shell 启动成功后可以触发后台上报，但必须捕获所有异常。上报失败不得影响登录、启动、PLC/MES/Cloud 生产链路。

### 10.5 Phase 3 边界

本阶段明确不做：

- 正式 catalog 签名或包签名强制校验。
- 复杂自动回滚策略。
- MES、PLC、配方、工艺参数、生产上传或业务链路改动。
- Cloud 发布记录管理页面的再设计。

仍需后续阶段补齐：

- 生产级签名验签和公钥轮换策略。
- 宿主回滚导致插件不兼容时的更完整 UX。
- Windows 真实云端首装、插件下载、插件更新和宿主更新联合验收。
