# Edge 安装更新验收

本文档是 EdgeClient 安装、更新和 Windows 分发安全策略的唯一客户端侧验收入口。云端生成安装包的字段写入规则由 CloudPlatform 单独验收，本文件只约束 EdgeClient 本仓库能验证的内容。

## 0. 标准发布链路

EdgeClient 不发布 Docker 镜像，不推 Harbor，也不从 GitHub hosted runner 直连内网服务器。当前链路分为日常 smoke、正式 GitHub 打包和本机快发：

- `push main`：只跑 smoke 编译和测试，不生成安装包和 Velopack 发布包。
- `workflow_dispatch` 或 `edge-v*` / `v*` tag：完整 GitHub 打包并发布到内网静态目录，渠道固定为 `stable`。
- 本机快发：操作者本机运行 `scripts/LocalPublishAndDeploy.ps1`，本机编译、打包、生成 installer artifact 后通过 rsync/scp 发布到服务器；这是运维快发路径，不属于 GitHub CI/CD job。
- 生产服务器只允许 `stable` 渠道，不保留 `ci`、`dev`、`test` 或其他测试渠道目录。

正式 GitHub 打包链路固定为：

```text
workflow_dispatch / edge-v* tag / v* tag
-> GitHub hosted Windows runner 构建 runtime、installer artifact、Velopack releases
-> 上传 GitHub Actions artifacts
-> 内网 Linux self-hosted runner 下载 artifacts
-> 本地发布到 /srv/iiot/edge-updates
-> Cloud 下载中心读取 installers 目录，Launcher 从 velopack 目录拉更新
```

`.github/workflows/edge-pack-modules.yml` 必须保持两个职责分离：

- `package-runtime`：只能跑在 `windows-latest`，负责 .NET/Avalonia/Velopack/installer 构建和验证。
- `publish-edge-updates`：只能跑在 `[self-hosted, iiot-linux-prod]`，负责把 artifact 复制到内网静态目录；该 job 不允许 `scp`、`ssh`、Docker、Harbor 或 GHCR。

内网 runner 必须使用非 root 专用用户运行。默认发布目录为 `/srv/iiot/edge-updates`，如需改目录只允许通过 GitHub Actions repository variable `EDGE_UPDATES_DIR` 覆盖。目录结构固定为：

```text
/srv/iiot/edge-updates/
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
```

本机快发命令示例：

```powershell
pwsh ./scripts/LocalPublishAndDeploy.ps1 `
  -Version 1.2.0 `
  -Channel stable `
  -DeployHost 10.98.90.154 `
  -DeployUser root `
  -EdgeUpdatesDir /srv/iiot/edge-updates
```

快发只负责让文件落盘。Cloud 负责在 catalog 请求时扫描 `/app/edge-updates/installers/stable/<version>/installer-artifact.json` 并与数据库 release 记录合并；数据库同 key 记录优先，可用 Draft/Archived 抑制文件版本。

## 1. 固定契约

- 安装器只接受当前 Velopack payload 和固定发布布局，不保留旧解压安装路径、exe 旁边 bootstrap、旧 `runtimeDirectory` 或 `layout.zip` 兼容。
- 首装配置只落到 `data/IIoT/EdgeClient/launcher/`，Launcher 不读取 exe 旁边的旧绑定文件。
- Velopack 管理的 `current/` 目录不得包含可变数据、插件目录、真实账号、真实更新源或 bootstrap 文件。
- 外部插件目录固定为安装根下的 `plugins/<ModuleId>/`。
- 卸载默认只清程序文件和快捷方式，`data/IIoT/EdgeClient/`、`data/IIoT/EdgeData/` 的配置、日志、缓存、SQLite、配方和诊断文件保留。

## 2. 必跑验证

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
- `edge-pack-modules.yml` 在 `workflow_dispatch` 或 `edge-v*` / `v*` tag 时必须生成 `edge-runtime-package`、`edge-installer-artifact`、`edge-velopack-releases` 三个 artifacts。
- `workflow_dispatch` 必须显式输入生产版本号；tag 触发时版本来自 tag。
- `PublishEdgeRuntime.ps1 -Version` 必须同步写入 runtime 的 `AssemblyVersion` / `FileVersion`，否则 `TestEdgeVelopackPackage.ps1` 会拒绝包版本和 Launcher 程序集版本不一致。
- CI 允许对 `PackEdgeClientVelopack.ps1` 使用 `-SkipVeloAppCheck:$true`，原因是 Launcher 通过 `EdgeUpdateVelopackStartup.Run()` 包装 `VelopackApp.Build().Run()`，Velopack CLI 静态扫描无法识别该包装；真实包仍必须通过 `TestEdgeVelopackPackage.ps1`。
- `edge-installer-artifact` 必须通过 `TestEdgeClientInstallerArtifact.ps1`，并包含 `installer-artifact.json`、安装器 exe、宿主、Launcher、插件和 Velopack setup。
- `edge-velopack-releases` 必须通过 `TestEdgeVelopackPackage.ps1`，并包含 `releases.stable.json`、`assets.stable.json`、full nupkg、setup exe 和 portable zip。
- `publish-edge-updates` 发布后必须能在 `/srv/iiot/edge-updates/installers/stable/<version>/installer-artifact.json` 和 `/srv/iiot/edge-updates/velopack/stable/releases.stable.json` 找到对应产物。

Windows 实机安装验收：

```powershell
./scripts/InvokeEdgeInstallerWindowsAcceptance.ps1 `
  -InstallerPath <cloud-downloaded-installer.exe> `
  -InstallRoot <install-root> `
  -ExpectedUpdateSource <base-url>/edge-updates/velopack/stable/ `
  -ExpectedChannel stable `
  -ExpectedTargetRuntime <target-runtime>
```

该脚本会验证安装布局、首装绑定导入、`launcher.update.json` 标准落点与 camelCase 字段、开始菜单快捷方式目标、静默安装默认不创建桌面快捷方式和 Launcher 启动。

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
