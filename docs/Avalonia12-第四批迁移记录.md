# Avalonia 12 第四批迁移记录

## 记录信息

- 日期：2026-05-13
- 仓库：`IIoT.EdgeClient.AvaloniaMigration`
- 范围：仅旁路迁移副本，不回写原 `IIoT.EdgeClient`
- 目标：迁移 Recipe 和 Param 两个标准页面，验证真实 Application 契约接入、参数编辑、DataGridTemplateColumn、动态列头、权限控制和本地应急配方入口

## 本批完成内容

- 新增 `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia/Features/Formula/RecipeView`
  - `RecipeViewModel` 接入 `IRecipeViewCrudService` 和 `IRecipeService`
  - 保留配方来源切换、云端同步入口、本地应急参数保存和删除入口
  - 本地编辑校验只在 UI 层处理名称、上下限数字和范围关系，不改业务服务规则
  - `RecipeViewPage` 使用 Avalonia `DataGrid` 和 `DataGridTemplateColumn` 展示配方参数与删除动作
- 新增 `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia/Features/Config/ParamView`
  - `ParamViewModel` 接入 `IParamViewCrudService` 和 `IClientPermissionService`
  - 按 MES、Cloud、插件业务三类参数分组展示
  - 保存权限来自 `IClientPermissionService.CanEditParams`，权限变化通过 `IAvaloniaDispatcherService` 回到 UI 线程
  - 参数显示名和描述使用 Avalonia 语言资源服务映射，不使用 WPF `ResourceDictionary`
  - `ParamViewPage` 使用 Avalonia `TabControl`、`DataGrid` 和模板列编辑参数值
- 调整 Navigation Avalonia 注册
  - `StandardAvaloniaModuleViewIds` 新增 `RecipeView` 和 `ParamView`
  - `NavigationAvaloniaPresentationRegistration` 注册 Recipe/Param 的 route、menu、dock pane
  - `NavigationAvaloniaResources` 补齐 Recipe/Param 菜单、标题、按钮、页签、列头、提示和中英文资源
  - `IIoT.Edge.Presentation.Navigation.Avalonia` 新增对 `IIoT.Edge.Application` 的引用，用于消费真实 Application 层契约
- 扩展 Headless 测试
  - Recipe route/page 可创建，fake `IRecipeViewCrudService` 可验证切源、保存、删除路径
  - Param route/page 可创建，fake `IParamViewCrudService` 可验证加载、保存和权限变化
  - 保留并清理上一批测试里的编码损坏断言，避免测试反向依赖乱码文本

## 边界说明

- 本批未连接真实 PLC。
- 本批未触发真实 Cloud/MES 上传。
- 本批未启动完整后台数据管线。
- 本批未修改 Cloud/MES API、数据库结构、配置 JSON 字段、PLC 读写策略、补偿规则或业务规则文档。
- 本批未迁移匀浆插件 UI、Launcher、现场硬件联调页。
- Avalonia 项目未引用 `IIoT.Edge.UI.Shared`、WPF Presentation 项目、`UseWPF` 或 `System.Windows`。

## 依赖结论

- 顶层 Avalonia/SukiUI/Dock/DialogHost/Material Icons 包仍为稳定版。
- 漏洞检查未发现易受攻击包。
- prerelease 传递依赖仍仅出现已批准例外的 `SkiaSharp` 系列：
  - `SkiaSharp/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Linux/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.macOS/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.WebAssembly/3.119.4-preview.1.1`
  - `SkiaSharp.NativeAssets.Win32/3.119.4-preview.1.1`

## 验证结果

```powershell
dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj
dotnet build src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj
dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj
dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj
dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj
dotnet test src/Tests/IIoT.Edge.Shell.Tests/IIoT.Edge.Shell.Tests.csproj
dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --include-transitive
dotnet list src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj package --vulnerable --include-transitive
rg --pcre2 -n "UseWPF|System\.Windows|IIoT\.Edge\.UI\.Shared|IIoT\.Edge\.Presentation\.(Navigation|Shell|Panels)(?!\.Avalonia)" src/Edge/IIoT.Edge.AvaloniaShell src/Edge/IIoT.Edge.Host.Bootstrap.Avalonia src/Shared/IIoT.Edge.UI.Avalonia src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia src/Tests/IIoT.Edge.AvaloniaShell.Tests
```

- AvaloniaShell build：通过，0 警告，0 错误
- WPF Shell build：通过，0 警告，0 错误
- AvaloniaShell Headless 测试：通过 11
- Module Contract Tests：通过 28
- NonUiRegressionTests：通过 367
- Shell Tests：通过 71，保留既有 fake 事件未使用警告
- 漏洞检查：未发现易受攻击包
- prerelease 检查：仅发现已批准的 `SkiaSharp` preview 传递依赖
- 边界扫描：未发现 `UseWPF`、`System.Windows`、`IIoT.Edge.UI.Shared` 或 WPF Presentation 项目引用

## 剩余风险

- Recipe 和 Param 已接入 Application 层契约，但仍通过 Headless/fake 服务验证，不代表完成现场数据联调。
- Param 当前用文本编辑器承载所有参数值，Bool/Int/Decimal 的更细粒度编辑器可在后续 UI 体验批次补齐。
- Recipe 的云端同步入口仍遵循现有 `IRecipeViewCrudService` 行为，本批没有放宽 Cloud 契约或上传策略。
- 资源动态刷新已覆盖菜单、Dock 标题和 DataGrid 列头；页面内部已加载文本如参数描述需要后续结合语言切换事件继续增强。

## 后续批次建议

- 第五批迁移匀浆插件 UI，保持模块 key、ProcessType、任务 key 字符串值不变。
- 第五批开始逐步把 HardwareConfig/IOView 的 fake 数据替换为只读真实配置查询，但仍不连接现场 PLC。
- Launcher 继续独立决策，不并入 Shell 页面迁移批次。
