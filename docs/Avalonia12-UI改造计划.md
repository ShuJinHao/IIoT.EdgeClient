# IIoT.EdgeClient Avalonia UI 改造计划

## 当前状态

- 本计划只覆盖 `IIoT.EdgeClient`，不联动 `IIoT.CloudPlatform`、`AICopilot`。
- 目标是把 WPF UI 技术栈迁移到 Avalonia，业务运行、PLC、Cloud/MES、SQLite 补偿、模块边界保持不变。
- 原 `IIoT.EdgeClient` 只保留计划和依赖例外文档，不承载 Avalonia 迁移代码。
- Avalonia 迁移代码隔离在旁路副本 `..\IIoT.EdgeClient.AvaloniaMigration`。
- 计划已发现一个必须先决策的依赖冲突：`Avalonia.Desktop 12.0.0` 到 `12.0.3` 当前都会通过 `Avalonia.Skia` 拉入 `SkiaSharp *.preview.*` 传递依赖。
- 如果严格执行“完整依赖图不能有 preview/prerelease”，当前不能使用 Avalonia 12，只能退到 `Avalonia.Desktop 11.3.15` 或等待 Avalonia 12 后续稳定依赖。
- 已选择方向 A：允许 Avalonia 12/SukiUI 当前稳定包带出的 `SkiaSharp` preview 传递依赖，继续 Avalonia 12 路线。
- 例外记录见 `docs/NuGet预览传递依赖例外记录.md`。

## 依赖验证结果

### SukiUI

- `SukiUI 6.1.0` 和 `SukiUI.Dock 6.1.0` restore 后会解析出：
  - `SkiaSharp/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Win32/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Linux/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.macOS/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.WebAssembly/3.119.4-preview.1.1`
- 结论：SukiUI 顶层包是稳定版；用户已批准 `SkiaSharp` preview 传递依赖例外，因此 PoC 可以采用 SukiUI。若后续解析出非 `SkiaSharp` preview/prerelease 包，立即暂停 SukiUI 并回退到 Fluent/Material 路线。

### Avalonia 12

- `Avalonia.Desktop 12.0.3` restore 后会解析出：
  - `SkiaSharp/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Win32/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Linux/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.macOS/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.WebAssembly/3.119.4-preview.1.1`
- `Avalonia.Desktop 12.0.0`、`12.0.1`、`12.0.2` 同样会解析出 preview `SkiaSharp`。
- `Avalonia.Desktop 11.3.15` 依赖图干净，没有 preview 包。

## 决策点

已选择方向 A。以下内容保留为决策记录：

- 方向 A：继续 Avalonia 12 最新稳定版，明确允许 Avalonia/SukiUI 当前带出的 `SkiaSharp *.preview.*` 传递依赖。
- 方向 B：严格禁止任何 preview/prerelease 依赖，先使用 `Avalonia 11.3.15`，等 Avalonia 12 依赖图稳定后再升级。

方向 A 已获得用户确认，只能在旁路副本中继续写 Avalonia 12 + SukiUI PoC 代码。

### 二次审核补充判断

- 方向 A 已新增依赖例外记录，明确例外只覆盖 Avalonia/SukiUI 当前稳定包带出的 `SkiaSharp *.preview.*` 传递依赖，不扩展到其他 preview 包。
- 如果选择方向 B，已验证以下 Avalonia 11 组合可以 restore 且不含 preview/prerelease 依赖：
  - `Avalonia.Desktop 11.3.15`
  - `Avalonia.Controls.DataGrid 11.3.7`
  - `Material.Avalonia 3.10.2`
  - `Material.Avalonia.DataGrid 3.10.2`
  - `Dock.Avalonia 11.3.12.1`
  - `Dock.Model.Mvvm 11.3.12.1`
  - `DialogHost.Avalonia 0.11.1`
  - `Material.Icons.Avalonia 2.4.3`
  - `CommunityToolkit.Mvvm 8.4.2`

## UI 技术路线

### 方向 A：Avalonia 12

- 主框架：`Avalonia` / `Avalonia.Desktop` 最新稳定版。
- 主题：PoC 优先采用 `SukiUI`，前提是顶层包稳定且完整依赖图中只有已批准的 `SkiaSharp` preview 传递依赖。
- 保守回退路线：`Avalonia.Themes.Fluent` + 自定义工业暗色主题，或 Material.Avalonia 稳定线。
- Dock：`Dock.Avalonia` / `Dock.Model.Mvvm`。
- DataGrid：`Avalonia.Controls.DataGrid`。
- 图标：优先使用稳定依赖图中的图标方案；如果 `Material.Icons.Avalonia` 带出 preview 依赖，则移除。
- MVVM：`CommunityToolkit.Mvvm`。

### 方向 B：Avalonia 11.3.15

- 主框架：`Avalonia.Desktop 11.3.15`。
- 主题：可用 Avalonia Fluent + 自定义工业暗色主题，或使用 `Material.Avalonia 3.10.2` 作为 Avalonia 11 兼容 Material 方案。
- Dock、DataGrid、Dialog、Icons 使用上方已验证的 Avalonia 11 兼容版本。
- 后续再单独评估升级 Avalonia 12。

## 本地项目事实

- 排除 `bin/obj` 后，当前 EdgeClient 有 38 个 XAML 文件。
- 12 个 XAML 使用 `DataGrid`。
- 8 个 XAML 使用 `DataGridTemplateColumn`。
- WPF API 已进入 ViewModel、服务、本地化、资源、窗口代码。
- 因此不能承诺 ViewModel 完全不动，也不能把迁移理解为 XAML 命名空间替换。

## 改造原则

- 只改 `IIoT.EdgeClient`。
- 生产 WPF 主线仓库不放 Avalonia 迁移代码；迁移实现放在旁路副本。
- 不修改 Cloud API、MES API、数据库结构、配置 JSON 字段、业务规则文档。
- 不改变 PLC 读写策略、Cloud/MES 上传与补偿规则、模块业务运行链路。
- 不把 WPF 和 Avalonia 做成长期双实现兼容层。
- 不把插件 UI、工序业务数据、运行时流程塞回 Shell。
- 现有中文 UI 默认文案保持中文；英文资源只有成对维护时才添加。

## Phase 0 - 审计与基线

- 输出 WPF 依赖清单。
- 输出 XAML 风险清单。
- 跑通并记录当前 WPF 测试基线，包括 `IIoT.Edge.Shell.Tests`、`IIoT.Edge.NonUiRegressionTests` 和模块契约测试。
- 确认本次只影响 EdgeClient。
- 记录依赖门槛验证结果。

## Phase 1 - PoC 依赖门槛

- 创建最小 Avalonia 项目。
- restore 后检查 `project.assets.json`。
- 确认是否存在 preview/prerelease 包。
- 如存在 preview 依赖，必须先决策是否允许继续。
- 记录实际采用的 NuGet 版本。

## Phase 2 - Avalonia PoC

PoC 只验证 UI 技术可行性，不连接真实 PLC，不触发真实 Cloud/MES 上传。

PoC 必须包含：

- `MainWindow` 骨架。
- Dock 布局：主页面区域、设备面板、日志面板。
- 弹窗：登录或确认弹窗。
- 无边框窗口、最大化、拖拽。
- `MonitorView` 代表区：卡片、状态色、DataGrid `AutoGenerateColumns`。
- `IOViewPage` 代表区：`DataGridTemplateColumn`、按钮、图标、列头本地化。
- `EquipmentView` 最小迁移：验证 Dock 面板、`EquipmentViewModel`、`Application.Current`/颜色状态等 WPF 渗透点。
- 动态中英文切换：普通文本和 DataGrid 列头都要刷新。
- Windows 工控机触摸、高 DPI、多屏手测。

## Phase 3 - Avalonia UI 基础层

- 主题资源。
- 页面承载控件，例如 Avalonia 版 `PageActionShell`。
- 导航承载。
- 弹窗封装。
- Dispatcher 适配。
- `AppLanguageService` 重接：用 Avalonia 资源系统替代 WPF `Application.Current.Resources` 和 `ResourceDictionary` 合并链。
- Avalonia DataGrid 列头动态本地化：替代当前 `DataGridColumnLocalization`，避免依赖全局静态扫描窗口刷新列头。
- Headless UI 测试基础。

## Phase 4 - Shell 迁移

- 迁移 Shell 主窗体。
- 迁移 Header、Footer、Login、SysMenu。
- 迁移 Equipment 和 Log 面板。
- 迁移 Dock 布局保存/恢复。
- 不修改运行时业务链路。

## Phase 5 - 功能页迁移

- 按风险从简单页到复杂页迁移。
- DataGrid 重页必须单页验收。
- 匀浆插件 UI 仍保持插件边界。
- WPF `Page` 类型契约迁移为 Avalonia `Control` 类型契约。
- 模块 key、菜单 key、ProcessType、任务 key 字符串值保持不变。

## Phase 6 - Launcher 单独决策

- `IIoT.Edge.Launcher` XAML 较大，默认不纳入首轮生产切换。
- PoC 和主 Shell 通过后，再决定是否同步迁移 Launcher。

## Phase 7 - 切换与清理

- 移除已替代的 WPF 包。
- 移除已替代的 `UseWPF`。
- 移除 WPF 专用共享 UI。
- 检查所有相关 csproj，确认 `MaterialDesignThemes`、`Dirkster.AvalonDock*` 等 WPF 专用包不再残留。
- 更新构建和发布脚本。
- 更新阶段完成记录。
- 保留 WPF 回退分支，但不在主线长期维护双实现。

## 测试计划

- `dotnet restore` 后检查完整依赖图。
- `dotnet build` PoC。
- Headless 测试覆盖本地化、Dialog、Dispatcher、DataGrid 列头资源绑定、导航注册解析。
- 现有 Edge 非 UI 回归测试继续运行。
- Shell 注册测试继续运行。
- 模块契约测试继续运行。
- Windows 工控机手测窗口行为、触摸、高 DPI、多屏、启动速度、内存。

## 阶段阻断条件

- 出现未批准的 preview/prerelease 依赖。
- 出现未处理的 `NU190x` 或漏洞告警。
- PoC 引用了现有 WPF UI 项目。
- `DataGridTemplateColumn` 代表场景无法稳定实现。
- 动态语言切换必须依赖全局静态扫描窗口。
- 改动触碰 Cloud/MES/数据库/PLC 业务链路。

## 当前已决事项

- 已允许 Avalonia 12/SukiUI 当前稳定包带出的 preview `SkiaSharp` 传递依赖。
- Avalonia 11.3.15 作为依赖红线无法接受时的回退路线保留，不作为当前主线。
- SukiUI 可进入 PoC；若后续出现未批准 preview/prerelease 依赖，立即回退到 Fluent/Material 路线。

## 参考来源

- [Avalonia releases](https://github.com/AvaloniaUI/Avalonia/releases)
- [Avalonia 12 breaking changes](https://docs.avaloniaui.net/docs/avalonia12-breaking-changes)
- [Avalonia DataGrid docs](https://docs.avaloniaui.net/docs/reference/controls/datagrid/)
- [Avalonia resources docs](https://docs.avaloniaui.net/docs/guides/styles-and-resources/resources)
- [SukiUI NuGet](https://www.nuget.org/packages/SukiUI/)
- [Material.Avalonia NuGet](https://www.nuget.org/packages/Material.Avalonia)
- [Dock.Avalonia NuGet](https://www.nuget.org/packages/Dock.Avalonia)
