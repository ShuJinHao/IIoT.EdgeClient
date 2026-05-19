# Phase 6 Launcher Avalonia Migration

## 范围

本阶段只迁移 `src/Edge/IIoT.Edge.Launcher` 原项目：

- Launcher 项目从 WPF 切换为 Avalonia，并关闭 `<UseWPF>true</UseWPF>`。
- `App`、`MainWindow`、`ChangePasswordWindow` 改为 `.axaml`。
- 旧 WPF `.xaml`、`Themes/LauncherTheme.xaml`、`materialDesign:` 和 `PackIcon` 在 Launcher 范围内清零。
- `LauncherMainViewModel` 改继承 `BaseNotifyPropertyChanged`，保留登录、改密、启动 Shell 的业务调用语义。

本阶段未修改账号服务、工序 profile 服务、密码哈希服务、Shell 子进程启动服务，也未修改 repo 内的账号或工序 json。

## 关键保持点

- `AddLauncherServices(string baseDirectory)` 签名保持不变，Avalonia App 启动时继续传入 `AppDomain.CurrentDomain.BaseDirectory`。
- `LauncherAccountCatalogInitializer.EnsureCatalogExists()` 仍在主窗口显示前执行。
- 密码输入继续由 code-behind 读取控件值后传给 `LoginAsync` / `ChangePasswordAsync`，没有在 ViewModel 上新增公开密码属性。
- `ShellLaunchService.Launch` 未改动，Shell 子进程启动方式和 `Shell__MachineProfile` 环境变量传递保持不变。
- `PasswordHash` 实际值未写入本文档。

## 验证结果

已执行：

```powershell
dotnet restore "src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj"
dotnet build "src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj" --no-restore /m:1 /p:UseSharedCompilation=false
```

结果：

- restore 通过。
- build 通过，0 error。
- build 有 2 个 Avalonia XAML loader 警告：`MainWindow.axaml` 与 `ChangePasswordWindow.axaml` 没有公开无参构造器。当前窗口由 DI / code-behind 显式构造，不影响本阶段运行路径；后续如需要设计器加载，可另行补无参设计期构造器。

已执行静态检查：

```powershell
rg "System\.Windows\.Application|System\.Windows\.Controls|System\.Windows\.Threading|System\.Windows\.Shell|using System\.Windows;|MessageBox|materialDesign:|PackIcon|HintAssist|ButtonAssist" "src\Edge\IIoT.Edge.Launcher"
Get-ChildItem "src\Edge\IIoT.Edge.Launcher" -Recurse -Filter *.xaml
Test-Path "src\Edge\IIoT.Edge.Launcher\ViewModels\ObservableObject.cs"
Select-String -Path "src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj" -Pattern "UseWPF.*true"
Select-String -Path "src\Edge\IIoT.Edge.Launcher\IIoT.Edge.Launcher.csproj" -Pattern "UseWPF.*false"
rg "Avalonia\.Controls\.DataGrid|CommunityToolkit\.Mvvm|ReactiveUI|Dock\.Avalonia|Material\.Avalonia|Material\.Icons\.Avalonia|DialogHost\.Avalonia|LucideAvalonia|MaterialDesignThemes" "src\Edge\IIoT.Edge.Launcher"
```

结果：

- WPF / MaterialDesign grep 无输出。
- Launcher 范围内 `.xaml` 无输出。
- `ObservableObject.cs` 为 `False`。
- `UseWPF.*true` 无输出，`UseWPF.*false` 命中 1 处。
- 禁用包 grep 无输出。

## 未执行项

未在真实窗口中执行三档截图验收：

- 1900x1200
- 1600x1000
- 1366x768

合主前仍需人工启动 Launcher，覆盖登录失败、登录成功、工序卡片、修改密码窗口和 Shell 子进程启动场景。
