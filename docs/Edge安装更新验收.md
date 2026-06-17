# Edge 安装更新验收

本文档是 EdgeClient 安装、更新和 Windows 分发安全策略的唯一客户端侧验收入口。云端生成安装包的字段写入规则由 CloudPlatform 单独验收，本文件只约束 EdgeClient 本仓库能验证的内容。

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
./scripts/TestEdgeClientInstallerArtifact.ps1 -ArtifactRoot publish/edge-installer-artifacts/ci/0.0.0-ci -ExpectedChannel ci -ExpectedVersion 0.0.0-ci
./scripts/TestEdgePackageVulnerabilities.ps1
```

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
