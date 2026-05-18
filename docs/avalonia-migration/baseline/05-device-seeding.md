# 05. 设备 bootstrap 与播种链路基线

本文把两个容易混淆的链路分开记录：

- 云端设备 bootstrap：用 `CloudApi:ClientCode` 和 `BootstrapSecret` 获取设备会话、上传 token 和设备身份。
- 开发样例设备播种：在本地数据库中创建 Homogenization PLC 设备和 IO 映射，供本地运行诊断和模拟使用。

## 云端设备 bootstrap 配置

云端接口配置由 `CloudApiConfig` 表达，字段包括：

- `BaseUrl`
- `TimeoutSecs`
- `ClientCode`
- `BootstrapSecret`
- `Paths`

对应 `CloudApiConfig.cs:3-17`。

`CloudApiEndpointProvider` 负责读取配置：

- `GetClientCode()` 要求 `CloudApi:ClientCode` 非空。对应 `CloudApiEndpointProvider.cs:34-40`。
- `GetBootstrapSecret()` 要求 `CloudApi:BootstrapSecret` 非空。对应 `CloudApiEndpointProvider.cs:43-50`。
- API path 必须非空、必须是相对 API 路径、必须以 `/` 开头。对应 `CloudApiEndpointProvider.cs:52-103`。

`appsettings.json` 中默认 `CloudApi:ClientCode` 为 `IIoT-Edge-Default`，`BootstrapSecret` 为空；`appsettings.machine.HomogenizationLine.json` 将 `ClientCode` 设置为 `HomogenizationLine`，并启用 Homogenization 模块及其设备播种配置。

## bootstrap 和 refresh 请求

`CloudDeviceBootstrapClient` 有两条请求路径：

- `BootstrapAsync`：读取 `ClientCode`、`BootstrapSecret`、`DeviceInstance` path，发起 `GET {path}?clientCode=...`，并在请求头写入 `BootstrapSecret`。对应 `CloudDeviceBootstrapClient.cs:29-62`。
- `RefreshAsync`：使用当前会话或配置中的 `ClientCode`，向 refresh path 发起 `POST`，请求头携带 refresh token。对应 `CloudDeviceBootstrapClient.cs:64-99`。

响应解析会生成 `DeviceSession`：

- `DeviceId` 使用云端响应中的设备 ID。
- `DeviceName` 使用云端响应名称。
- `ClientCode` 使用响应值或本地原始 ClientCode。
- `ProcessId`、上传 access token、refresh token 和过期时间来自响应体和响应头。

对应 `CloudDeviceBootstrapClient.cs:101-133`、`DeviceSession.cs:3-24`。

## DeviceService 状态机

`DeviceService` 是 Shell 内设备会话和上传门控的核心服务：

- 初始状态为 `Offline`，上传门控为 `Unknown/DeviceUnidentified`。对应 `DeviceService.cs:30-42`。
- `StartAsync` 启动心跳任务。对应 `DeviceService.cs:65-80`。
- 心跳循环启动后立即执行一次识别或刷新，然后按在线或离线间隔循环。对应 `DeviceService.cs:151-174`。
- 如果云端上传被运行配置关闭，服务会标记 `CloudUploadDisabled`，不发起 bootstrap。对应 `DeviceService.cs:176-202`。
- 有可刷新的当前会话时优先 refresh；refresh 失败后进入 bootstrap。对应 `DeviceService.cs:240-260`。
- bootstrap 成功后进入 `GoOnline`，保存会话缓存，门控置为 `Ready/None`，发布设备识别和网络状态事件。对应 `DeviceService.cs:262-286`、`DeviceService.cs:335-374`。
- bootstrap 或 token 校验失败时进入 `GoOffline`，上传门控置为 Blocked，并尝试按 ClientCode 加载本地缓存设备。对应 `DeviceService.cs:318-333`、`DeviceService.cs:376-430`。

上传门控由 `DeviceUploadGatePolicy` 和 `CloudUploadGate` 共同表达：

- refresh 需要 refresh token 存在且未过期。对应 `DeviceUploadGatePolicy.cs:5-13`。
- token 不可用时产生 `DeviceUnidentified`、`MissingUploadToken` 或 `ExpiredUploadToken`。对应 `DeviceUploadGatePolicy.cs:15-31`。
- bootstrap 失败会映射为 HTTP、超时、网络、响应无效等阻断原因。对应 `DeviceUploadGatePolicy.cs:33-54`。
- `CloudUploadGate` 在上传关闭、设备未就绪或 token 不可用时返回 blocked 快照。对应 `CloudUploadGate.cs:23-40`。

## 会话缓存

设备会话缓存写入运行路径下的 `device_cache.json`。对应 `ShellRuntimePathResolver.cs:39`、`DeviceSessionFileCacheStore.cs`。

缓存读取规则：

- 文件不存在时返回空。
- 文件存在但内容无效时返回失败结果。
- `DeviceId` 必须非空。
- `ClientCode` 必须和当前配置匹配；旧格式缓存可按当前 ClientCode 升级保存。

对应 `DeviceSessionFileCacheStore.cs` 和 `DeviceSessionCacheCoordinator.cs`。

## 开发样例设备播种

启动初始化会调用 `DevelopmentSampleInitializer.EnsureConfigurationSamplesAsync`。对应 `AppStartupInitializer.cs:29-39`、`DevelopmentSampleInitializer.cs:22-29`。

运行状态恢复阶段会调用 `DevelopmentSampleInitializer.EnsureRuntimeSamplesAsync`。对应 `AppRuntimeStateCoordinator.cs:34-40`、`DevelopmentSampleInitializer.cs:31-38`。

Homogenization 模块注册了开发样例贡献者。对应 `IIoT.Edge.Module.Homogenization/DependencyInjection.cs:89`。

`HomogenizationDevelopmentSampleContributor` 的当前行为：

- 只播种样例 PLC 设备；IO 点位来自硬件模板。对应 `HomogenizationDevelopmentSampleContributor.cs:13-16`。
- 默认设备为 `PLC-Homogenization-01`，协议 `Mc`，地址 `127.0.0.1:6000`，启用状态 true。对应 `HomogenizationDevelopmentSampleContributor.cs:19-31`。
- 配置绑定到 `Modules:Homogenization:DeviceSeed`。对应 `HomogenizationDevelopmentSampleContributor.cs:50-62`。
- 可选 `ResetBeforeImport` 会删除既有 Homogenization 设备和映射。对应 `HomogenizationDevelopmentSampleContributor.cs:98-130`。
- 正常播种时会创建 `NetworkDeviceEntity`，绑定 `ModuleId=Homogenization` 和设备模型，再根据硬件 Profile 创建 IO 映射。对应 `HomogenizationDevelopmentSampleContributor.cs:133-215`。

开发样例开关分两层：

- 全局 `DevelopmentSamples.Enabled`
- 模块内 `Modules:Homogenization:DeviceSeed:Enabled`

默认 `appsettings.json` 关闭开发样例；`appsettings.Development.json` 启用开发样例和 Homogenization 模块；`appsettings.machine.HomogenizationLine.json` 启用 Homogenization 模块设备播种。

## 迁移保持点

- 设备 bootstrap、上传 token、上传门控不能和 Launcher 本地登录合并。
- 本地样例设备播种不能替代云端设备身份。
- 启动诊断应继续在 PLC 绑定和后台任务启动前验证设备与模块绑定。
- Avalonia UI 只读取并展示设备、网络和上传门控状态，不应改写 bootstrap 或播种业务链路。
