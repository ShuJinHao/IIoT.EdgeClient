# Edge 安装更新验收

本文档是 EdgeClient 安装、更新和 Windows 分发安全策略的唯一客户端侧验收入口。云端生成安装包的字段写入规则由 CloudPlatform 单独验收，本文件只约束 EdgeClient 本仓库能验证的内容。

## 0. 标准发布链路

EdgeClient 不发布 Docker 镜像，不推 Harbor，也不从 GitHub runner 直连内网服务器。标准链路固定为：

```text
git push / workflow_dispatch
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
  installers/<channel>/<version>/
    installer-artifact.json
    IIoT.Edge.Setup.exe
    launcher/
    host/
    plugins/
    velopack/
  velopack/<channel>/
    releases.<channel>.json
    assets.<channel>.json
    *.nupkg
    *-Setup.exe
    *-Portable.zip
```

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
./scripts/PackEdgeClientVelopack.ps1 -Version 0.0.0-ci -Channel ci -OutputRoot publish/edge-velopack -CleanOutput
./scripts/TestEdgeVelopackPackage.ps1 -OutputRoot publish/edge-velopack -Channel ci -Version 0.0.0-ci
./scripts/TestEdgeClientInstallerArtifact.ps1 -ArtifactRoot publish/edge-installer-artifacts/ci/0.0.0-ci -ExpectedChannel ci -ExpectedVersion 0.0.0-ci
./scripts/TestEdgePackageVulnerabilities.ps1
```

CI 发布验收：

- `edge-pack-modules.yml` 在 push main 或 `workflow_dispatch` 时必须生成 `edge-runtime-package`、`edge-installer-artifact`、`edge-velopack-releases` 三个 artifacts。
- `edge-installer-artifact` 必须通过 `TestEdgeClientInstallerArtifact.ps1`，并包含 `installer-artifact.json`、安装器 exe、宿主、Launcher、插件和 Velopack setup。
- `edge-velopack-releases` 必须通过 `TestEdgeVelopackPackage.ps1`，并包含 `releases.<channel>.json`、`assets.<channel>.json`、full nupkg、setup exe 和 portable zip。
- `publish-edge-updates` 发布后必须能在 `/srv/iiot/edge-updates/installers/<channel>/<version>/installer-artifact.json` 和 `/srv/iiot/edge-updates/velopack/<channel>/releases.<channel>.json` 找到对应产物。

Windows 实机安装验收：

```powershell
./scripts/InvokeEdgeInstallerWindowsAcceptance.ps1 `
  -InstallerPath <cloud-downloaded-installer.exe> `
  -InstallRoot <install-root> `
  -ExpectedUpdateSource <base-url>/edge-updates/velopack/<channel>/ `
  -ExpectedChannel <channel> `
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
