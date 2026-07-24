# Edge 安装更新验收

> 读取边界：只有本次实际发布 Host、Installer、Velopack 或具体插件时，才读取对应验收小节；Windows 实机、历史候选和其它产物章节不默认读取。

本文档是 EdgeClient 安装、更新和 Windows 分发安全策略的唯一客户端侧验收入口。上传部署总口径见 `../../docs/上传部署总览.md`。标准入口依次是根目录 `deploy/Validate-Candidate.ps1`、`deploy/Prepare-Release.ps1`、`deploy/Deploy-Changed.ps1 -PreparedReleaseId <id>`；紧急生产入口是 `deploy/Deploy-ProductionNow.ps1 -Target Edge`。本文件保留项目级验收细节。

> 本文只定义验收契约，不代表任何一次发布已经完成。真实 Cloud `stable` 上传、catalog/DB/静态 HEAD、Windows 下载与实机结果以对应部署 receipt/checkpoint 为准。

EdgeClient 验收分为开发阶段和实际部署阶段：macOS 是主力开发环境，必须承担真实 Launcher/Shell 启动、登录后 UI、更新进度和隔离安装验证；Windows 是现场部署目标，必须承担 Release runtime、安装器、快捷方式、Velopack 更新和 `targetRuntime` 实机验收。两层证据必须分别记录，互不替代；开发阶段通过不代表已经进入发布或部署。

## 0. 标准发布链路

EdgeClient 不发布 Docker 镜像，不推 Harbor，也不从 GitHub hosted runner 直连内网服务器。当前链路分为默认受影响验证、可选离线 artifact 构建和本机正式发布：

- `push main` / PR：只跑 Architecture/Security 与 selector 选出的受影响 Business/DeploymentContract，不生成安装包和 Velopack 发布包；全量、coverage、mutation、duplication 不自动运行。
- 显式 `workflow_dispatch`：只在 hosted Windows runner 构建并上传离线 Actions artifacts，到此停止；不连接 Cloud、不写生产、不构成发布证据。
- 标准候选验证：`Validate-Candidate.ps1` 在开发阶段运行受影响验证并生成 exact-SHA 绿色证据。
- 标准产物准备：`Prepare-Release.ps1` 调度 `LocalPublishAndDeploy.ps1 -PrepareOnly` 与 `PublishEdgePluginRelease.ps1 -PrepareOnly`，本机编译/pack 并封存 Host/AP/CP 真实字节，不写生产。
- 标准正式投放：`Deploy-Changed.ps1 -PreparedReleaseId <id>` 只上传预制包并验证 catalog/hash/download，不运行测试、编译或 pack。
- 紧急生产：`Deploy-ProductionNow.ps1 -Target Edge` 不运行测试/CI/绿色门禁；先封存三仓工作区，只 pack 一次，失败归档本轮新版本，永不取得绿色资格。
- 本机快发和正式发布上传前，发布凭据必须从 macOS Keychain canonical Edge Release API key 换短期发布 JWT；Human refresh token、旧 session、Markdown 和旧 env 不得作为标准 fallback。
- 生产更新内容必须由根入口显式传 `-EdgeReleaseNotes` 或 `-EdgeReleaseNotesPath`；离线 artifact workflow 的 `release_notes` 只进入离线产物。
- 本机正式发布必须由工作区统一入口生成内部调度标记；EdgeHost/EdgePlugin 共用协调锁。标准流程要求三仓 clean/main/pushed；ProductionNow 允许脏工作区但必须先封存不可变快照。
- 三端从零部署只走 `deploy/Deploy-FromZero.ps1 -PreparedReleaseId <id>`；Host/AP/CP 在 Cloud 清空前已完成 pack，清空后只吊销/重签派生 Release API key、上传和验收，不得重建。不得自动创建设备、注册 `ClientCode` 或轮换设备 bootstrap secret。
- catalog、HTTP 上传和静态 HEAD 验证必须执行连接、总时限与低速停滞门禁，并把受限长度的 `4xx/5xx` 正文写入错误摘要。失败产物和 `edge-deployment-attempt.json` 必须保留；标准流程只允许用同一 `PreparedReleaseId` 重传或对账，ProductionNow失败只允许归档，不得反复全量构建或跳过release notes、DB登记、审计和保留策略。
- 生产服务器只允许 `stable` 渠道，不保留 `ci`、`dev`、`test` 或其他测试渠道目录。

可选 GitHub 离线 artifact 链路固定为：

```text
workflow_dispatch
-> GitHub hosted Windows runner 构建 runtime、installer artifact、Velopack releases
-> 上传 GitHub Actions artifacts
-> 停止；不得下载到 self-hosted runner 或调用 Cloud 发布 API
```

`.github/workflows/edge-pack-modules.yml` 只能保留 hosted Windows 的 `package-runtime` artifact 生成；禁止 `self-hosted`、`publish-edge-updates`、Cloud 人员账号密码、短期 JWT、服务器目录写入或其它生产发布 job。生产目录使用 `${EDGE_UPDATES_DIR}`，真实值只来自 Cloud 生产配置；当前现场口径是 `/data/iiot-platform/edge-client/edge-updates`。历史 `/srv/iiot/edge-updates` 不是标准路径。目录结构固定为：

```text
${EDGE_UPDATES_DIR}/
  installers/stable/<version>/
    installer-artifact.json
    IIoT.Edge.Setup.exe
    launcher/
    host/
    velopack/
  velopack/stable/
    releases.stable.json
    assets.stable.json
    *.nupkg
    *-Setup.exe
    *-Portable.zip
  plugins/stable/<ModuleId>/<version>/
    IIoT.EdgePlugin.<ModuleId>-<version>-<runtime>.zip
```

工作区标准投放只消费 Prepare 输出；操作者和 AI 只执行根入口：

```powershell
pwsh ./deploy/Deploy-Changed.ps1 `
  -Targets Edge `
  -PreparedReleaseId <prepared-release-id>
```

未传 `-Version` 时，HTTP 发布会读取 Cloud Human catalog 最新 stable 版本并自动递增 patch；需要固定版本时才显式传 `-Version`。本机快发的完整操作入口见 `docs/客户端部署.md`。

HTTP 快发会先让文件安全落盘，再由 Cloud 服务端从 manifest 派生 DB release 行、写审计、按 SemVer 执行最新 3 个 stable 版本保留策略，并返回部署摘要。Cloud catalog、下载中心和首装版本集合只读取 Cloud release 记录；文件系统只验证记录中已登记 artifact 的存在性、完整性和可下载性，不得扫描残留目录补出版本。

插件独立发布必须满足：

- `PublishEdgePluginRelease.ps1` 必须显式传 `-ReleaseNotes` 或 `-ReleaseNotesPath`。
- `PublishEdgePluginRelease.ps1` 必须显式接收 canonical `-PluginRepositoryRoot`，只调用 Private Plugins 的 `eng/PackEdgePlugin.ps1`；Host 仓不得保留第二份插件 pack 或 package validator。
- 根入口必须显式传 `-ModuleId`；缺失时禁止用示例/default 模块继续发布。
- 发布前查 Cloud catalog；相同 `(moduleId, channel, version, targetRuntime)` 已存在时必须失败。
- 插件 schema v2 metadata 与 wrapper 的 `sourceCommit` 必须等于 Private Plugins clean/pushed HEAD，不能写 Host commit。
- Cloud `plugin-packages` 接口落盘到 `${EDGE_UPDATES_DIR}/plugins/stable/<ModuleId>/<version>/`，DB 插件 release 的 `downloadUrl` 必须指向真实 zip。
- Launcher 只安装插件时，宿主版本不得递增。

## 1. 固定契约

- 安装器只接受当前 Velopack payload 和固定发布布局，不保留旧解压安装路径、exe 旁边 bootstrap、旧 `runtimeDirectory` 或 `layout.zip` 兼容。
- 首装配置只落到 `data/IIoT/EdgeClient/launcher/`，Launcher 不读取 exe 旁边的旧绑定文件。
- Velopack 管理的 `current/` 目录不得包含可变数据、插件目录、真实账号、真实更新源或 bootstrap 文件。
- 外部插件目录固定为安装根下的 `plugins/<ModuleId>/`。
- Host 安装素材和 Velopack 包不得包含业务插件；业务插件只在外部目录由独立 catalog/package 安装。
- 卸载默认只清程序文件和快捷方式，`data/IIoT/EdgeClient/`、`data/IIoT/EdgeData/` 的配置、日志、缓存、SQLite、配方和诊断文件保留。

## 2. 按阶段触发的验收

macOS 开发阶段运行态验收：

- 使用当前源码构建的 Launcher/Shell，不用静态截图或 ViewModel stub 冒充真实运行。
- Launcher 需要登录后验证的页面必须提供合法已登录环境；允许在隔离 `IIOT_EDGE_PROGRAM_DATA_ROOT` 中通过真实首次初始化 UI 创建临时本地账号，但不得读取、复制或覆盖开发者真实账号文件。
- 更新进度验收必须使用隔离 runtime 和仅监听 loopback 的临时 catalog/package 源触发一次真实下载与安装，确认更新前版本、进度条可见、完成后状态和安装版本；不得修改正式 `publish/Debug`，不得连接生产发布源。
- macOS 验收结束后必须关闭进程并删除临时账号、临时运行目录和临时更新源；结果写入本次任务或发布证据。只有长期规则变化、历史回归、生产事故或部署机制变化才写滚动复盘。
- 本阶段不得因为验证通过自动触发上传、发布、`stable` 或 Windows 部署。

```powershell
dotnet test src/Tests/IIoT.Edge.Installer.UnitTests/IIoT.Edge.Installer.UnitTests.csproj -c Release --no-build --no-restore --nologo
dotnet test src/Tests/IIoT.Edge.Installer.UiTests/IIoT.Edge.Installer.UiTests.csproj -c Release --no-build --no-restore --nologo
./scripts/TestEdgeRuntimePublish.ps1 -Configuration Release -RuntimeIdentifier win-x64 -SelfContained
./scripts/PackEdgeClientVelopack.ps1 -Version 1.0.0 -Channel stable -RuntimeIdentifier win-x64 -SelfContained -OutputRoot publish/edge-velopack -CleanOutput -SkipVeloAppCheck:$true
./scripts/TestEdgeVelopackPackage.ps1 -OutputRoot publish/edge-velopack -Channel stable -Version 1.0.0
./scripts/TestEdgeClientInstallerArtifact.ps1 -ArtifactRoot publish/edge-installer-artifacts/stable/1.0.0 -ExpectedChannel stable -ExpectedVersion 1.0.0
./scripts/TestEdgePackageVulnerabilities.ps1
```

CI 发布验收：

- `push main` 不跑完整打包；只允许 smoke 编译和测试。
- `edge-pack-modules.yml` 在显式 `workflow_dispatch` 时只生成 `edge-runtime-package`、`edge-installer-artifact`、`edge-velopack-releases` 三个离线 artifacts；不得发布生产。
- `workflow_dispatch` 必须显式输入生产版本号。
- `workflow_dispatch` 必须显式输入 artifact notes；日常根入口必须显式传生产 `-EdgeReleaseNotes` 或 `-EdgeReleaseNotesPath`。
- 本机快发未显式传 `-Version` 时必须自动生成下一个 stable patch 版本，并在 `installer-artifact.json` 写入 `sourceCommit`、`previousVersion`、`previousSourceCommit`、`releaseNotes` 和 `generatedAtUtc`。
- `PublishEdgeRuntime.ps1 -Version` 必须同步写入 runtime 的 `AssemblyVersion` / `FileVersion`，否则 `TestEdgeVelopackPackage.ps1` 会拒绝包版本和 Launcher 程序集版本不一致。
- CI 允许对 `PackEdgeClientVelopack.ps1` 使用 `-SkipVeloAppCheck:$true`，原因是 Launcher 通过 `EdgeUpdateVelopackStartup.Run()` 包装 `VelopackApp.Build().Run()`，Velopack CLI 静态扫描无法识别该包装；真实包仍必须通过 `TestEdgeVelopackPackage.ps1`。
- `edge-installer-artifact` 必须通过 `TestEdgeClientInstallerArtifact.ps1`，并包含 `installer-artifact.json`、安装器 exe、宿主、Launcher 和 Velopack setup；不得包含具体工序插件。
- `edge-velopack-releases` 必须通过 `TestEdgeVelopackPackage.ps1`，并包含 `releases.stable.json`、`assets.stable.json`、full nupkg、setup exe 和 portable zip。
- 正式 `win-x64` 的 installer payload 与 Velopack full nupkg 必须同时携带 Launcher/Host 的 `coreclr.dll`、`hostfxr.dll`、`hostpolicy.dll` 和 `System.Private.CoreLib.dll`；缺少任一文件即判定为 framework-dependent 错包，禁止上传和发布，不能以现场在线安装 .NET 作为通过条件。
- 正式发布后的 `verificationUrls` HEAD、服务器目录和 Cloud catalog 验证只由根级部署 receipt 负责；Actions artifact 成功不得冒充生产发布成功。
- 发布失败后优先从根入口复用已生成 artifact 或恢复失败阶段；不得未经定位反复全量 CI。Cloud 返回 `400 hash/size` 不一致时，先对比 artifact 文件列表、manifest 和 Cloud 校验算法，再决定是否修改脚本。
- 独立插件发布后必须能在 `${EDGE_UPDATES_DIR}/plugins/stable/<ModuleId>/<version>/` 找到插件 zip，且 Cloud catalog 中对应插件 release 的 `downloadUrl` 可 HEAD 成功。
- SDK runtime/API、Host runtime 或具体插件受影响时，发布前必须由 SDK 仓唯一兼容门绑定三仓候选 HEAD，验证四个 SDK 包、插件唯一 zip、旧生产不可变组合、候选接受和跨代旧插件拒载；Analyzer/docs-only SDK 改动不得触发 Host/Plugin 发布。
- Host API 代际变化时，首个产品版本必须显式等于新 `hostApiVersion`；不得把 API 2.0.0 宿主按旧 1.x catalog 自动递增为 patch。

Windows 实际部署阶段实机验收：

```powershell
./scripts/InvokeEdgeInstallerWindowsAcceptance.ps1 `
  -InstallerPath <cloud-downloaded-installer.exe> `
  -InstallRoot <install-root> `
  -ExpectedUpdateSource <base-url>/edge-updates/velopack/stable/ `
  -ExpectedChannel stable `
  -ExpectedTargetRuntime <target-runtime>
```

该脚本会验证安装布局、首装绑定导入、`launcher.update.json` 标准落点与 camelCase 字段、开始菜单快捷方式目标、静默安装默认不创建桌面快捷方式和 Launcher 启动。

Windows 实机验收只在本次确实进入 Windows 发布或现场部署阶段时执行；macOS 开发阶段通过、GitHub Windows runner 构建通过或安装包静态检查通过，都不能替代该步骤。未执行时在本次验收结果中明确标为 `NOT-RUN`，不得写成全平台验收完成，也不因此强制新增滚动复盘。

存在插件 activation 时，Windows 实机验收还必须逐一验证安装包实际包含的工序：插件贡献的 Launcher profile 必须与 Host 基础 profile 解析到同一份 `current/host/IIoT.Edge.Shell.exe`，对应外部 machine profile 必须可读，实际启动后进程使用正确 `Shell__MachineProfile` 并保持运行。只验证 Launcher 进程、两张卡片可见、插件 ZIP 存在或机器配置写盘都不能证明工序可启动。

## 3. Defender 策略

安装器不得默认添加 Defender 排除项。外网客户环境优先走 Authenticode 签名；企业内网如经运维审批需要临时排除项，只能手动运行：

```powershell
./scripts/AddEdgeDefenderExclusion.ps1 -InstallRoot <install-root> -ConfirmApply
```

如果现场使用 `IIOT_EDGE_PROGRAM_DATA_ROOT` 指向独立可变数据根，并且运维明确要求一起排除：

```powershell
./scripts/AddEdgeDefenderExclusion.ps1 -InstallRoot <install-root> -IncludeAppDataRoot -ConfirmApply
```

该脚本要求管理员 PowerShell，不由安装器自动调用。
