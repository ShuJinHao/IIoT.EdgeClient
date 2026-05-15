# NuGet 预览传递依赖例外记录

## 例外范围

- 项目：`IIoT.EdgeClient.AvaloniaMigration`
- 决策日期：2026-05-13
- 例外类型：仅允许 Avalonia 12 稳定包通过 `Avalonia.Skia` 传递带入的 `SkiaSharp 3.119.x-preview.1.1` 及对应 `SkiaSharp.NativeAssets.* 3.119.x-preview.1.1`。
- 原 `IIoT.EdgeClient` WPF 主线仓库不承载 Avalonia 包引用。
- 不允许把本例外扩展到其他 preview、prerelease、alpha、beta、rc、nightly 包。
- 不允许直接新增顶层 preview 包引用。

## 例外原因

- 用户已明确选择 Avalonia 12 最新稳定版路线，并接受 SkiaSharp preview 作为传递依赖临时例外。
- 当前 NuGet 验证显示，`Avalonia.Desktop 12.0.3` 会通过 `Avalonia.Skia` 传递依赖 `SkiaSharp 3.119.4-preview.1.1`。
- 如果完全禁止传递 preview 依赖，当前只能退回 Avalonia 11 线；用户已明确选择继续 Avalonia 12。
- 第六批验证中发现 `SukiUI 6.1.0` 会引入 Avalonia 11 系列传递包，并且 `SukiWindow` 在 Avalonia 12 Headless 运行时触发 `WindowStateProperty` 缺失，因此 SukiUI 已从本迁移主线移除，改用 Avalonia Fluent + Dock Fluent。

## 已验证依赖

`AvaloniaShell` 和 `Launcher.Avalonia` 当前仅出现以下 preview/prerelease 依赖：

- `SkiaSharp/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.Win32/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.Linux/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.macOS/3.119.4-preview.1.1`
- `SkiaSharp.NativeAssets.WebAssembly/3.119.4-preview.1.1`

## 影响范围

- 仅影响 Avalonia 12 UI 渲染依赖。
- 不影响 Cloud/MES API。
- 不影响 PLC 读写策略。
- 不影响 SQLite 补偿链路。
- 不影响业务运行规则和模块运行时。

## 收回条件

- 当 Avalonia 12 或后续稳定版本不再传递依赖 `SkiaSharp *.preview.*` 时，必须移除此例外。
- 当 SkiaSharp 3.x 发布正式稳定版且 Avalonia 稳定包已对齐时，必须升级并重新执行依赖图检查。
- 后续每次升级 Avalonia、Dock、Material Icons 或其他 Avalonia 相关包时，都必须重新检查 `project.assets.json` 和 `dotnet list package --include-transitive`。

## 验证命令

```powershell
dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --include-transitive
dotnet list src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj package --include-transitive
dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive
dotnet list src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj package --vulnerable --include-transitive
```

检查结果：preview/prerelease 仅限本文列出的 SkiaSharp 系列，漏洞扫描无未处理告警。
