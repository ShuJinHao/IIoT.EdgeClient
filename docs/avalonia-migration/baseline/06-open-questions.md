# 06. 迁移前明确记录项

本文记录 Phase 0 需要固定下来的开放事项和结论。所有条目均基于当前源码和一次 WPF Shell 运行日志。

## PresentationViewModelBase 已存在

`PresentationViewModelBase` 已存在，位置为：

`src/Shared/IIoT.Edge.UI.Shared/PluginSystem/PresentationViewModelBase.cs`

该类型定义为 `public abstract class PresentationViewModelBase : ViewModelBase`，提供：

- `IsBusy`
- `ErrorMessage`
- `StatusMessage`
- `HasError`
- `HasStatus`
- `ClearFeedback`
- `SetError`
- `SetStatus`
- `ReplaceItems`
- `SyncItemsByKey`
- `RunViewTaskAsync`
- `RunViewTaskInBackground`

对应 `PresentationViewModelBase.cs:5-159`。

当前使用点包括 SysLog、Navigation 基类、Equipment、Homogenization Navigation ViewModel。Avalonia 迁移不需要重新发明同名基类；需要先判断该共享基类能否继续作为 ViewModel 行为层，避免重复抽象。

## 图标方案对比

当前 WPF 图标事实：

- 大量 XAML 使用 `materialDesign:PackIcon`。
- `SysMenuViewModel` 使用 `MaterialDesignThemes.Wpf` 的图标类型。
- `src/Shared/IIoT.Edge.UI.Shared/IIoT.Edge.UI.Shared.csproj` 将 `Assets\fonts\iconfont.ttf` 作为资源包含。
- 本次扫描未发现 `iconfont.ttf` 对应的 WPF glyph 映射或直接 glyph 使用；当前可见 UI 图标主要来自 MaterialDesignThemes PackIcon。

两个 Avalonia 方向：

| 方案 | 优点 | 风险 |
| --- | --- | --- |
| `Material.Icons.Avalonia` | 与当前 Material Design 图标语义接近，PR #44 已引入过 `Material.Icons.Avalonia` 3.0.2，可降低 PackIcon 名称迁移成本。 | 新增依赖，需要确认包版本、控件样式和授权；若只迁移少量图标，依赖面可能偏大。 |
| 现有 `iconfont.ttf` 转 SVG/Path | 可减少运行时图标包依赖，资源可纳入 Avalonia `PathIcon` 或 Drawing 资源。 | 当前仓库未找到 glyph 名称映射，转换前需要确认字体码点和语义；若从字体反推图标，迁移成本和误配风险更高。 |

Phase 0 结论：不在本阶段新增图标依赖。后续 Phase 如选择 `Material.Icons.Avalonia`，应只引入该图标包并保留清晰映射表；如选择字体转 SVG，必须先建立 `iconfont.ttf` 的码点到业务图标语义的映射。

## 空 Modules 实测结果

实测运行目录：

`C:\Users\jinha\Desktop\产线系统架构升级\1\publish\Debug\shell`

实测时 `Modules` 目录不存在。结果：

- 发现模块数：`0`
- 配置启用模块数：`0`
- 激活模块数：`0`
- 启动诊断问题码：`PLUGIN_ROOT_MISSING`、`PLUGIN_NONE_ENABLED`
- 启动被拦截的位置：EF Core migration、Dapper 表初始化、开发样例配置初始化之后；PLC 绑定和后台服务启动之前。

日志文件：

`C:\Users\jinha\Desktop\产线系统架构升级\1\publish\Debug\shell\data\profiles\Default\diagnostics\logs\2026-05-18.log`

该结果应作为 Avalonia Shell 的空模块行为基线：空模块不能静默进入主界面。

## AppLanguageService 建议落点

当前 WPF 实现位于：

`src/Presentation/IIoT.Edge.Presentation.Shell/Localization/AppLanguageService.cs`

接口位于：

`src/Shared/IIoT.Edge.UI.Shared/Localization/IAppLanguageService.cs`

当前实现行为：

- 默认语言为 `zh-CN`。
- 支持 `zh-CN` 和 `en-US`。
- 默认持久化到 `%LocalAppData%\IIoT.Edge\language.json`。
- 切换语言时设置线程 Culture，替换 WPF ResourceDictionary，并刷新 DataGrid 列标题。
- 资源加载范围包括 Shell、Presentation.Shell、Presentation.Navigation、Presentation.Panels 和已加载的 `IIoT.Edge.Module.*` 程序集。

对应 `AppLanguageService.cs:17-38`、`AppLanguageService.cs:43-99`、`AppLanguageService.cs:101-165`、`AppLanguageService.cs:181-223`。

建议 Avalonia 落点：

`src/Shared/IIoT.Edge.UI.Avalonia/Localization/AppLanguageService.cs`

理由：

- `IAppLanguageService` 的语言枚举、当前语言、切换事件和 `GetString` 语义可以保持框架无关。
- WPF ResourceDictionary 和 pack URI 逻辑必须替换为 Avalonia 资源加载逻辑。
- 落在共享 Avalonia UI 项目可供 Launcher、Shell、Presentation 和模块 UI 复用，避免把语言切换绑定到单一 Shell Presentation 项目。

## PR #44 额外依赖对比结论

对比来源：

- 当前 WPF 基线：`IIoT.EdgeClient/Directory.Packages.props`
- PR #44 参考仓库：`IIoT.EdgeClient.AvaloniaMigration/Directory.Packages.props`

PR #44 相比当前 WPF 基线新增了这些包：

- `Avalonia` 12.0.3
- `Avalonia.Controls.DataGrid` 12.0.0
- `Avalonia.Desktop` 12.0.3
- `Avalonia.Fonts.Inter` 12.0.3
- `Avalonia.Headless.XUnit` 12.0.3
- `Avalonia.Themes.Fluent` 12.0.3
- `CommunityToolkit.Mvvm` 8.4.2
- `DialogHost.Avalonia` 0.12.2
- `Dock.Avalonia` 12.0.0.2
- `Dock.Avalonia.Themes.Fluent` 12.0.0.2
- `Dock.Model.Mvvm` 12.0.0.2
- `LucideAvalonia` 1.6.2
- `Material.Avalonia` 3.16.1
- `Material.Avalonia.DataGrid` 3.16.1
- `Material.Icons.Avalonia` 3.0.2
- `Microsoft.Extensions.Configuration` 10.0.5
- `xunit.v3` 3.2.2

Phase 0 结论：

- Avalonia 主体包、Desktop、Fluent theme、DataGrid 是 Avalonia 迁移的核心候选，但本阶段不新增。
- `Avalonia.Headless.XUnit` 只应在后续明确需要 Avalonia UI 自动化测试时引入。
- `Material.Icons.Avalonia` 是图标方案候选，不应随 UI 迁移自动带入。
- `CommunityToolkit.Mvvm` 与现有 `ViewModelBase`、`PresentationViewModelBase` 有职责重叠，不能在未重构 ViewModel 基线前引入。
- Dock、DialogHost、Material.Avalonia、LucideAvalonia、xunit.v3 都不是 Phase 0 或基础迁移的必需依赖；若后续需要，应分别给出使用点和替代方案比较。
- `Microsoft.Extensions.Configuration` 已有 Abstractions、Binder、Json 等拆分包；是否需要总包应由实际项目引用决定，不能仅因 PR #44 存在而继承。

本阶段不 cherry-pick PR #44 代码，不继承其依赖集合。
