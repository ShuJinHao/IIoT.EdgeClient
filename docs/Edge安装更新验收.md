# Edge 安装更新验收

本文档是 EdgeClient 安装、更新和 Windows 分发安全策略的唯一客户端侧验收入口。上传部署总口径见 `../../docs/上传部署总览.md`；云端生成安装包的字段写入规则由 CloudPlatform 单独验收，本文件只约束 EdgeClient 本仓库能验证的内容。工作区日常唯一对外入口是根目录 `deploy/Deploy-Changed.ps1`；`deploy/Invoke-WorkspaceDeploy.ps1` 只作为宿主/插件内部执行器和显式恢复入口。本文件保留项目级验收细节。

> 当前状态（2026-07-10）：隔离提交 `37ec98b` 的部署行为、失败恢复与发布契约回归已通过；本轮没有执行真实 Cloud `stable` 上传/catalog/DB/静态 HEAD，也没有执行 Windows runtime/installer/Velopack/targetRuntime 实机验收，因此本清单不能作为生产已验收证明。

EdgeClient 验收分为开发阶段和实际部署阶段：macOS 是主力开发环境，必须承担真实 Launcher/Shell 启动、登录后 UI、更新进度和隔离安装验证；Windows 是现场部署目标，必须承担 Release runtime、安装器、快捷方式、Velopack 更新和 `targetRuntime` 实机验收。两层证据必须分别记录，互不替代；开发阶段通过不代表已经进入发布或部署。

## 0. 标准发布链路

EdgeClient 不发布 Docker 镜像，不推 Harbor，也不从 GitHub hosted runner 直连内网服务器。当前链路分为日常 smoke、正式 GitHub 打包和本机快发：

- `push main`：只跑 smoke 编译和测试，不生成安装包和 Velopack 发布包。
- `workflow_dispatch` 或 `edge-v*` / `v*` tag：完整 GitHub 打包并发布到内网静态目录，渠道固定为 `stable`。
- 本机宿主快发：操作者或 AI 从工作区根运行 `deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgeHost`，统一入口内部调度 `LocalPublishAndDeploy.ps1 -Transport http`，本机编译、打包、生成 installer artifact 后通过 Cloud Human API 上传 release bundle；这是运维快发路径，不属于 GitHub CI/CD job。生产 `stable` 不允许走 `rsync/scp`。
- 本机插件快发：只改工序插件时从工作区根运行 `deploy/Invoke-WorkspaceDeploy.ps1 -Target EdgePlugin -ModuleId <真实ModuleId>`；内部脚本只上传独立插件 zip 并登记插件 release，不生成宿主版本。
- 本机快发和正式发布上传前，发布凭据必须优先使用 Edge Release API key 换短期发布 JWT；Human refresh token 只作为临时应急 fallback，不得作为稳定发布凭据。
- 更新内容必须显式填写：本机快发传 `-ReleaseNotes` 或 `-ReleaseNotesPath`；`workflow_dispatch` 填 `release_notes`；tag 发布必须使用带正文的 annotated tag。
- 本机正式发布必须由工作区统一入口生成内部调度标记；项目实现脚本拒绝直接执行。EdgeHost/EdgePlugin 共用本地互斥锁，且构建前必须确认工作树 clean、HEAD 已推送到 upstream。
- catalog、HTTP 上传和静态 HEAD 验证必须执行连接、总时限与低速停滞门禁，并把受限长度的 `4xx/5xx` 正文写入错误摘要。失败产物和 `edge-deployment-attempt.json` 必须保留，后续通过统一入口 `-ResumeReleaseRoot` 只做校验、重传或已发布对账；不得反复从头全量构建或跳过 release notes、DB 登记、审计和保留策略。
- 生产服务器只允许 `stable` 渠道，不保留 `ci`、`dev`、`test` 或其他测试渠道目录。

正式 GitHub 打包链路固定为：

```text
workflow_dispatch / edge-v* tag / v* tag
-> GitHub hosted Windows runner 构建 runtime、installer artifact、Velopack releases
-> 上传 GitHub Actions artifacts
-> 内网 Linux self-hosted runner 下载 artifacts
-> 组装 release bundle 并调用 Cloud Human 发布 API
-> Cloud 下载中心读取 installers 目录，Launcher 从 velopack 目录拉更新
```

`.github/workflows/edge-pack-modules.yml` 必须保持两个职责分离：

- `package-runtime`：只能跑在 `windows-latest`，负责 .NET/Avalonia/Velopack/installer 构建和验证。
- `publish-edge-updates`：只能跑在 `[self-hosted, iiot-linux-prod]`，负责把 artifact 组装成 release bundle 并调用 Cloud Human API；该 job 不允许 `scp`、`ssh`、Docker、Harbor 或 GHCR。

内网 runner 必须使用非 root 专用用户运行。发布目录使用 `${EDGE_UPDATES_DIR}`，真实生产路径以 Cloud `.env` 为准；当前生产现场口径是 `/data/iiot-platform/edge-client/edge-updates`。历史 `/srv/iiot/edge-updates` 只允许作为旧 `scp/rsync` 或非生产 fallback 说明，不再是标准 HTTP 发布路径。如需改目录只允许通过 GitHub Actions repository variable `EDGE_UPDATES_DIR` 或 Cloud 生产 `.env` 覆盖。客户端文档不得固化真实服务器 IP。目录结构固定为：

```text
${EDGE_UPDATES_DIR}/
  installers/stable/<version>/
    installer-artifact.json
    IIoT.Edge.Setup.exe
    launcher/
    host/
    plugins/
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

工作区标准入口会调度本机快发实现脚本；操作者和 AI 只执行根入口：

```powershell
pwsh ./deploy/Invoke-WorkspaceDeploy.ps1 `
  -Target EdgeHost `
  -ReleaseNotesPath ./release-notes.md
```

未传 `-Version` 时，HTTP 发布会读取 Cloud Human catalog 最新 stable 版本并自动递增 patch；需要固定版本时才显式传 `-Version`。本机快发的完整操作入口见 `docs/客户端部署.md`。

HTTP 快发会先让文件安全落盘，再由 Cloud 服务端从 manifest 派生 DB release 行、写审计、按 SemVer 执行最新 3 个 stable 版本保留策略，并返回部署摘要。GitHub 正式发布路径同样通过 Cloud Human 发布 API 上传 bundle，由服务端清理 `installers/stable`、`velopack/stable` 和独立插件 zip 中超出保留策略的文件。Cloud catalog、下载中心和首装版本集合只读取 Cloud release 记录；文件系统只验证记录中已登记 artifact 的存在性、完整性和可下载性，不得扫描残留目录补出版本。

插件独立发布必须满足：

- `PublishEdgePluginRelease.ps1` 必须显式传 `-ReleaseNotes` 或 `-ReleaseNotesPath`。
- 根入口必须显式传 `-ModuleId`；缺失时禁止用示例/default 模块继续发布。
- 发布前查 Cloud catalog；相同 `(moduleId, channel, version, targetRuntime)` 已存在时必须失败。
- Cloud `plugin-packages` 接口落盘到 `${EDGE_UPDATES_DIR}/plugins/stable/<ModuleId>/<version>/`，DB 插件 release 的 `downloadUrl` 必须指向真实 zip。
- Launcher 只安装插件时，宿主版本不得递增。

## 1. 固定契约

- 安装器只接受当前 Velopack payload 和固定发布布局，不保留旧解压安装路径、exe 旁边 bootstrap、旧 `runtimeDirectory` 或 `layout.zip` 兼容。
- 首装配置只落到 `data/IIoT/EdgeClient/launcher/`，Launcher 不读取 exe 旁边的旧绑定文件。
- Velopack 管理的 `current/` 目录不得包含可变数据、插件目录、真实账号、真实更新源或 bootstrap 文件。
- 外部插件目录固定为安装根下的 `plugins/<ModuleId>/`。
- 卸载默认只清程序文件和快捷方式，`data/IIoT/EdgeClient/`、`data/IIoT/EdgeData/` 的配置、日志、缓存、SQLite、配方和诊断文件保留。

## 2. 必跑验证

macOS 开发阶段运行态验收：

- 使用当前源码构建的 Launcher/Shell，不用静态截图或 ViewModel stub 冒充真实运行。
- Launcher 需要登录后验证的页面必须提供合法已登录环境；允许在隔离 `IIOT_EDGE_PROGRAM_DATA_ROOT` 中通过真实首次初始化 UI 创建临时本地账号，但不得读取、复制或覆盖开发者真实账号文件。
- 更新进度验收必须使用隔离 runtime 和仅监听 loopback 的临时 catalog/package 源触发一次真实下载与安装，确认更新前版本、进度条可见、完成后状态和安装版本；不得修改正式 `publish/Debug`，不得连接生产发布源。
- macOS 验收结束后必须关闭进程并删除临时账号、临时运行目录和临时更新源；结果记入滚动复盘。
- 本阶段不得因为验证通过自动触发上传、发布、`stable` 或 Windows 部署。

```powershell
dotnet test src/Tests/IIoT.Edge.Installer.Tests/IIoT.Edge.Installer.Tests.csproj -p:BuildInParallel=false --disable-build-servers
./scripts/TestEdgeRuntimePublish.ps1 -Configuration Release
./scripts/PackEdgeClientVelopack.ps1 -Version 1.0.0 -Channel stable -OutputRoot publish/edge-velopack -CleanOutput -SkipVeloAppCheck:$true
./scripts/TestEdgeVelopackPackage.ps1 -OutputRoot publish/edge-velopack -Channel stable -Version 1.0.0
./scripts/TestEdgeClientInstallerArtifact.ps1 -ArtifactRoot publish/edge-installer-artifacts/stable/1.0.0 -ExpectedChannel stable -ExpectedVersion 1.0.0
./scripts/TestEdgePackageVulnerabilities.ps1
```

CI 发布验收：

- `push main` 不跑完整打包；只允许 smoke 编译和测试。
- `workflow_dispatch` 或 tag 触发正式打包前，必须先运行 `scripts/TestEdgeDeploymentPreflight.ps1 -Mode GitHubHost`，确认版本号、release notes、workflow 包名、`publish-edge-updates`、manifest normalize 和 Cloud API 入口。
- `edge-pack-modules.yml` 在 `workflow_dispatch` 或 `edge-v*` / `v*` tag 时必须生成 `edge-runtime-package`、`edge-installer-artifact`、`edge-velopack-releases` 三个 artifacts。
- `workflow_dispatch` 必须显式输入生产版本号；tag 触发时版本来自 tag。
- `workflow_dispatch` 必须显式输入 `release_notes`；tag 发布必须是 annotated tag 且正文非空；本机快发必须显式传 `-ReleaseNotes` 或 `-ReleaseNotesPath`。
- 本机快发未显式传 `-Version` 时必须自动生成下一个 stable patch 版本，并在 `installer-artifact.json` 写入 `sourceCommit`、`previousVersion`、`previousSourceCommit`、`releaseNotes` 和 `generatedAtUtc`。
- `PublishEdgeRuntime.ps1 -Version` 必须同步写入 runtime 的 `AssemblyVersion` / `FileVersion`，否则 `TestEdgeVelopackPackage.ps1` 会拒绝包版本和 Launcher 程序集版本不一致。
- CI 允许对 `PackEdgeClientVelopack.ps1` 使用 `-SkipVeloAppCheck:$true`，原因是 Launcher 通过 `EdgeUpdateVelopackStartup.Run()` 包装 `VelopackApp.Build().Run()`，Velopack CLI 静态扫描无法识别该包装；真实包仍必须通过 `TestEdgeVelopackPackage.ps1`。
- `edge-installer-artifact` 必须通过 `TestEdgeClientInstallerArtifact.ps1`，并包含 `installer-artifact.json`、安装器 exe、宿主、Launcher、插件和 Velopack setup。
- `edge-velopack-releases` 必须通过 `TestEdgeVelopackPackage.ps1`，并包含 `releases.stable.json`、`assets.stable.json`、full nupkg、setup exe 和 portable zip。
- `publish-edge-updates` 发布后必须通过 Cloud API 返回的 `verificationUrls` 做 HEAD 验证；服务器上必须能在 `${EDGE_UPDATES_DIR}/installers/stable/<version>/installer-artifact.json` 和 `${EDGE_UPDATES_DIR}/velopack/stable/releases.stable.json` 找到对应产物。
- 发布失败后优先重跑失败 job 或复用已生成 artifacts；不得未经定位反复全量 CI。Cloud 返回 `400 hash/size` 不一致时，先在下载后的 artifact 目录对比文件列表、manifest 和 Cloud 校验算法，再决定是否修改脚本。
- 独立插件发布后必须能在 `${EDGE_UPDATES_DIR}/plugins/stable/<ModuleId>/<version>/` 找到插件 zip，且 Cloud catalog 中对应插件 release 的 `downloadUrl` 可 HEAD 成功。

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

Windows 实机验收必须在发布或现场部署前执行；macOS 开发阶段通过、GitHub Windows runner 构建通过或安装包静态检查通过，都不能替代该步骤。未实际进入 Windows 部署阶段时，复盘必须明确写“Windows 实机部署验收未执行”，不得写成全平台验收完成。

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
