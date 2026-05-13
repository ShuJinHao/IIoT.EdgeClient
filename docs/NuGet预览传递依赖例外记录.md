# NuGet 预览传递依赖例外记录

## 例外范围

- 项目：`IIoT.EdgeClient`
- 决策时间：2026-05-13
- 例外类型：仅允许旁路副本 `..\IIoT.EdgeClient.AvaloniaMigration` 的 Avalonia 12 UI Shell 和后续 Avalonia 12 UI 主线中，由 Avalonia/SukiUI 当前稳定包传递带出的 `SkiaSharp 3.119.x-preview.1.1` 及对应 `SkiaSharp.NativeAssets.* 3.119.x-preview.1.1`。
- 原 `IIoT.EdgeClient` WPF 主线仓库不承载 Avalonia 包引用。
- 不允许把本例外扩展到其他 preview、prerelease、alpha、beta、rc、nightly 包。
- 不允许直接新增顶层 preview 包引用。

## 例外原因

- 用户已确认选用 Avalonia 12 最新稳定版。
- 当前 NuGet 验证显示，`Avalonia.Desktop 12.0.0` 到 `12.0.3` 的稳定包都会通过 `Avalonia.Skia` 传递依赖 `SkiaSharp *.preview.*`。
- `SukiUI 6.1.0` 和 `SukiUI.Dock 6.1.0` 作为稳定包，也会解析到同类 `SkiaSharp *.preview.*` 依赖。
- 若完全禁止传递 preview 依赖，当前只能退回 Avalonia 11.3.15；用户已明确选择 Avalonia 12，并接受后续 SkiaSharp 稳定后再更新。

## 已验证依赖

- `Avalonia.Desktop 12.0.3` 会解析到：
  - `SkiaSharp/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Win32/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Linux/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.macOS/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.WebAssembly/3.119.4-preview.1.1`
- `SukiUI 6.1.0` 和 `SukiUI.Dock 6.1.0` 会解析到同类 `SkiaSharp` preview 依赖。

## 影响范围

- 仅影响 Avalonia 12 UI 渲染依赖。
- 不影响 Cloud/MES API。
- 不影响 PLC 读写策略。
- 不影响 SQLite 补偿链路。
- 不影响业务运行规则和模块运行时。

## 收回条件

- 当 Avalonia 12 或后续稳定版本不再传递依赖 `SkiaSharp *.preview.*` 时，必须移除此例外。
- 当 SkiaSharp 3.x 发布正式稳定版且 Avalonia/SukiUI 稳定包已对齐时，必须升级并重新执行依赖图检查。
- 后续每次升级 Avalonia、SukiUI、Dock 或 Material Icons 时，都必须重新检查 `project.assets.json` 中是否仍存在 preview/prerelease 包。

## 验证命令

```powershell
dotnet restore IIoT.EdgeClient.AvaloniaMigration/src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj
```

检查 `IIoT.EdgeClient.AvaloniaMigration/src/Edge/IIoT.Edge.AvaloniaShell/obj/project.assets.json`，只允许出现本文列出的 `SkiaSharp` preview 传递依赖。
