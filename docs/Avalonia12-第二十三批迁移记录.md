# Avalonia 12 第二十三批迁移记录

## 迁移副本收敛记录

### 目标

本批把迁移副本收敛为 Avalonia-only 工作区，避免后续查看、开发和审核时继续混看两套 UI。

### 调整结果

- 移除旧桌面壳、旧启动器、旧 Presentation、旧共享 UI、旧模块壳和旧桌面壳测试。
- 解决方案只保留 Avalonia Shell、Avalonia Launcher、Avalonia Bootstrap、UI.Avalonia、业务 Core、Runtime、Infrastructure、Application、SharedKernel、模块 Core/Avalonia 插件和仍有价值的测试项目。
- 迁移副本中的模块插件只保留 Core 与 Avalonia 插件壳，插件页面继续按模块方式注册。
- Launcher Avalonia 使用自己的 profile 图片资源，不再从旧启动器目录取资源。
- 测试改为验证 Avalonia Bootstrap、Avalonia 视图注册和 Avalonia 模块插件契约。
- 清理旧 UI 专用包版本与审核、现场证据、默认入口评审相关脚本和文档。
- 修正 Bootstrap Core 的历史命名空间，避免被误判为旧桌面壳代码。

### 验证

- `dotnet build IIoT.EdgeClient.slnx -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Module.ContractTests/IIoT.Edge.Module.ContractTests.csproj -m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj -m:1 /p:UseSharedCompilation=false`

### 边界

- 未修改 CloudPlatform、AICopilot、Cloud/MES API、数据库结构、PLC 块规划策略或业务规则文档。
- 原始 WPF 仓库仍作为对照和回退来源；迁移副本不再保留旧 UI 项目。

## 高端 UI 第一阶段改造记录

### 完成内容

- 将 Avalonia Shell 默认主题切换为浅色高端工业控制台方向：浅灰画布、大白色 shell、lime 状态点缀、柔色 KPI 卡和单块深色生产工位画布。
- 在 `IIoT.Edge.UI.Avalonia` 的 XAML 主题中集中建立 `Edge.*` 设计令牌和复用样式类，保留原 `Ind.*` 资源键以兼容已迁移页面。
- 重构 Shell 外壳、顶部栏、左侧 icon rail、页脚、登录弹窗和系统确认弹窗的视觉结构，菜单来源仍由 `IAvaloniaViewRegistry` 和现有 Dock 布局驱动。
- 重做 `MonitorViewPage` 为生产总览样板页，新增汇总值仅由现有 `Devices` 行数据计算，不新增业务查询，不改变数据写入。
- 重做设备运行面板和系统日志面板的展示层，使右侧状态面板与生产总览页同源。
- 新增和补齐中英文 XAML 资源键，用户可见文本继续走资源字典。

### 架构边界

- 未修改 `src/Modules/**` 插件模块代码，未改插件注册、插件菜单描述、模块加载或运行时任务。
- 未修改 DDD、Domain、PLC、MES、Cloud、缓存、数据库、上传、重试和死信业务链路。
- UI 颜色、圆角、阴影、卡片、按钮、表格等均通过 XAML 资源和样式加载，未在业务代码中硬编码视觉策略。
- `MonitorViewModel` 仅增加 UI 展示用汇总属性，数据仍来自原 `IMonitorViewService.GetSnapshotsAsync()`。

### 验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore`

### 剩余风险

- 第一阶段尚未逐页重做硬件配置、参数配置、配方、I/O、诊断等深层页面，只通过共享浅色设计令牌改善基础观感。
- 第一阶段未启动真实现场运行链路做人工视觉截图验收，后续需要在实际窗口尺寸和现场分辨率下检查文字拥挤、表格列宽和弹窗遮挡。
- 左侧导航当前按参考图采用 icon rail，现场操作员是否需要“图标 + 文字”的展开模式，需要在后续结合现场反馈确认。

### 下一阶段进入条件

- 第一阶段视觉方向经人工截图确认后，再进入各业务页面逐页改造。
- 若要调整插件页面或模块自带页面，必须先单独确认插件模块边界和允许修改的文件清单。

## 高端 UI 第二阶段改造记录

### 完成内容

- 扩展 `IndustrialTheme.axaml` 共享主题，新增 `edge-page-panel`、`edge-table-card`、`edge-form-section`、`page-title` 等宿主页面复用样式，继续通过 XAML 资源承载颜色、圆角、阴影、卡片和表格外观。
- 将生产类宿主页面 `CapacityViewPage`、`DataViewPage`、`DiagnosticsPage` 改为同源 Premium Desktop Console 视觉：浅灰页面面板、白色内容卡、克制 lime 状态点缀、稳定表格和分组信息块。
- 将设备与硬件类宿主页面 `IOViewPage`、`HardwareConfigPage`、`NetworkDevicePage`、`SerialDevicePage`、`IoMappingPage` 改为更清晰的现场工作台布局，保留原绑定、原命令和原数据流。
- 将配置类宿主页面 `RecipeViewPage`、`ParamViewPage`、`PlcTaskBindingPage` 改为统一的卡片化表单和表格结构，避免传统 WPF 工具软件质感。
- `DiagnosticsPage` 的新增页签标题改为中英文资源键，补齐 `zh-CN.xaml` 与 `en-US.xaml` 成对条目。

### 架构边界

- 未修改 `src/Modules/**` 插件模块，未调整插件页面、插件菜单注册、模块注册类型或插件加载方式。
- 未修改 Application、Domain、Runtime、Infrastructure 合同，未新增业务接口，未新增 PLC、MES、Cloud、缓存、数据库、上传、重试或死信调用。
- 本阶段未新增业务查询、写入、清理或派生业务服务；页面继续使用现有 ViewModel 的既有数据、命令和集合。
- 未引入新的 Avalonia 控件包，继续沿用 Avalonia 12、Dock.Avalonia、DialogHost.Avalonia、Material.Icons.Avalonia 和现有资源系统。
- 右侧面板仍沿用既有 `EquipmentView` / `LogView` 风格和 Dock 加载方式，未新增插件面板机制。
- 当前仓库存在既有脏改动，本阶段只处理宿主 Avalonia UI 和共享主题资源，不回退、不整理无关改动。

### 验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore`：通过，0 warning，0 error。
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore`：通过，59 passed，0 failed。
- 页面层未新增内联十六进制颜色；色值集中在 `IndustrialTheme.axaml` 设计令牌中。
- 中英文诊断页签资源键已成对补齐。

### 剩余风险

- 本阶段完成的是 XAML 结构与样式层改造，尚未启动真实客户端窗口做人工截图验收；后续仍需在现场常用分辨率下检查列宽、长文本、弹窗遮挡和滚动体验。
- 插件模块页面仍保持原状；如果后续要让插件页面也同源升级，需要先列出插件文件清单并单独确认。
- 当前左侧导航仍以 icon rail 为主，是否增加现场操作员可读的展开中文标签模式，建议在截图验收后再定。

### 下一阶段进入条件

- 在 Avalonia 客户端真实窗口中完成一轮人工截图验收。
- 明确是否允许进入插件模块页面改造；未获确认前继续禁止修改 `src/Modules/**`。
- 若截图验收发现某些页面信息密度不足，优先通过 XAML 样式和布局微调处理，不向业务层增加查询或写入逻辑。

## 高端 UI 纠偏版第三批改造记录

### 完成内容

- 修正主客户端外壳：`MainWindow` 改为无原生标题栏窗口，左侧 rail 顶部承载品牌 logo，中部继续使用 registry 驱动的主导航，底部保留低频入口。
- 修正顶部 Header：去掉旧式 logo 占位，补齐本地模式 chip、产线 chip、搜索文本双向绑定、通知角标绑定；通知数量为 0 时隐藏角标。
- Header 支持空白区域拖动窗口，双击空白区域调用现有最大化/还原命令；窗口控制仍由 Header 内按钮承担。
- 通过 Shell 本地样式隐藏 Dock 文档页签，未修改 `IAvaloniaViewRegistry`、Dock 注册协议、菜单来源或模块加载方式。
- 将 Launcher 改为同源浅色 Premium Desktop Console：浅灰背景、大白圆角 shell、左侧品牌状态区、右侧登录与工序 profile 区。
- 修复 Launcher View 中的用户可见中文，新增 Launcher 中英文 XAML 资源字典，并在 Launcher 启动时注册并应用现有 Avalonia 语言服务。
- 补充 Launcher 资源卫生测试，验证中英文资源键成对、View/资源不含常见乱码片段、DI 可解析语言服务且能读取中文登录文案。

### 架构边界

- 未修改 `src/Modules/**` 插件模块页面、注册、权限、资源和业务逻辑。
- 未修改 Application、Domain、Runtime、Infrastructure 合同；未新增业务接口。
- 未修改 PLC、MES、Cloud、缓存、数据库、上传、重试、死信链路。
- 未修改 `IIoT.Edge.Host.Bootstrap` 启动链路；当前该目录仍有既有脏改动，本轮未对其做新增编辑。
- Launcher 只改 UI、主题、资源加载和 UI 测试；未改本地账号校验算法、profile 文件格式、shell 启动流程。
- 当前仓库存在大量既有脏改动，本轮只做允许范围内的定点叠加，不做 reset、checkout、整文件回退或全量格式化。

### 验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore`：通过，0 warning，0 error。
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore`：通过，0 warning，0 error。
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore`：通过，59 passed，0 failed。
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore`：通过，24 passed，0 failed。
- `rg "鏈|璐|瀵|鐧|淇|鎼" src/Edge/IIoT.Edge.Launcher.Avalonia/Views src/Edge/IIoT.Edge.Launcher.Avalonia/Resources`：无命中。
- `rg "#[0-9A-Fa-f]{6,8}"` 针对本轮宿主与 Launcher XAML 检查时，命中仅位于 `LauncherTheme.axaml` 主题资源文件。

### 剩余风险

- 尚未启动真实桌面窗口做 1366x768、1440x900、1920x1080 人工截图验收；需要后续用真实窗口确认无标题栏露出、Header 不挤、Dock 页签隐藏效果和 Launcher 视觉一致性。
- Dock 页签隐藏当前采用 Shell 本地样式覆盖；如果真实窗口发现影响右侧 tool pane 或 Dock 交互，应停止继续扩展并单独评估 Dock 主题策略。
- 禁止区域当前仍显示既有脏改动，需要后续按对应业务/架构计划单独处理；本轮不解释、不合并、不回退这些改动。

### 下一阶段进入条件

- 先完成真实窗口截图验收，确认主窗口无原生标题栏、无顶部横向旧菜单、左侧 logo + icon rail 和顶部工具区符合参考图。
- 若继续推进内部页面高级感，只允许在宿主页面和共享 XAML 资源内做布局与样式微调。
- 若要统一插件页面，必须先输出 `src/Modules/**` 插件 UI 文件清单和风险说明，单独批准后再实施。

## 高端 UI 视觉返工纠偏记录

### 完成内容

- 重排 Shell 主外壳，把 Header、左侧 rail、Dock 内容区和右侧 tool pane 收敛到同一块大白圆角 shell 内，降低拼接感。
- Header 改为稳定四区布局：菜单和标题、状态 chip 组、搜索框、通知/用户/窗口控制；长文本通过最大宽度和截断处理，避免挤压搜索框。
- 增加 Dock 本地样式覆盖，隐藏文档/工具页签，并压低 Dock 默认边框、chrome、背景残留；未修改 Dock 注册协议和 `IAvaloniaViewRegistry`。
- 右侧 `EquipmentView` 与 `LogView` 改为同源状态面板，卡片、tabs、滚动区域和底部提示统一约束，避免状态 chip 和长描述撑破布局。
- Shell `LoginView` 从普通表单卡改为分层登录弹窗，包含图标、标题说明、输入区、错误提示区和明确主操作。
- Launcher 入口重做为浅灰画布 + 大白 shell：左侧品牌/环境/版本，右侧登录卡或 profile 启动工作区；去掉原先独立顶部标题栏的割裂感。
- Launcher 登录、profile 卡片和改密窗口统一细化，按钮高度、卡片留白、状态提示和窗口控制视觉更接近参考图。
- `LauncherMainViewModel` 仅新增 `HasError` UI-only 显示属性，用于错误提示显隐，不触发业务查询或写入。

### 插件 UI 清单与风险

- 本轮未修改 `src/Modules/**`。
- 已识别截图中央“匀浆出料数据”插件 UI 相关文件：
  - `src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.axaml`
  - `src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.axaml.cs`
  - `src/Modules/IIoT.Edge.Module.Homogenization/Presentation/HomogenizationDataViewModel.cs`
  - `src/Modules/IIoT.Edge.Module.Homogenization/Resources/Languages/zh-CN.xaml`
  - `src/Modules/IIoT.Edge.Module.Homogenization/Resources/Languages/en-US.xaml`
  - `src/Modules/IIoT.Edge.Module.Homogenization/Resources/HomogenizationText.cs`
- 风险：如果插件页面不批准改造，中央旧表格、方形边界和页面级留白只能由宿主容器缓解，无法彻底达到参考图的 Premium Desktop Console 效果。
- 后续如批准插件 UI，只应改插件 XAML 和显示资源，不碰插件运行时、上传、MES、Cloud、PLC、缓存或业务数据链路。

### 架构边界

- 未新增业务接口，未修改 Application、Domain、Runtime、Infrastructure 合同。
- 未修改 PLC、MES、Cloud、缓存、数据库、上传、重试、死信链路。
- 未修改 `IAvaloniaViewRegistry`、Dock 注册协议、权限协议、模块加载协议。
- 未新增 Avalonia UI 控件包；继续使用现有 Avalonia 12、Dock.Avalonia、DialogHost.Avalonia、Material.Icons.Avalonia 和 XAML 资源体系。
- 当前仓库存在既有脏改动，本轮只做允许范围内的定点叠加，不做 reset、checkout、整文件回退或全量格式化。

### 验证

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore`：通过。
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore`：通过。
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore`：通过，59 passed，0 failed。
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore`：通过，24 passed，0 failed。
- 宿主 View 与 Launcher View 内联十六进制颜色检查：无命中；色值仍集中在主题资源文件。
- `rg "鏈|璐|瀵|鐧|淇|鎼" src/Edge/IIoT.Edge.Launcher.Avalonia/Views src/Edge/IIoT.Edge.Launcher.Avalonia/Resources`：无命中。
- 禁止区状态复核显示 `src/Modules/**`、Application、Host.Bootstrap 等仍存在既有脏改动；本轮未对这些禁止区做新增编辑。

### 剩余风险

- 真实窗口截图验收仍需要人工确认，尤其是 1366x768、1440x900、1920x1080 下 Header 是否拥挤、右侧面板是否截断、Launcher 登录态是否足够细腻。
- Dock 默认视觉由样式覆盖压制，如果真实窗口仍出现 Dock 控件残留，需要继续在宿主 Shell 范围内补充样式，不能转向改 Dock 注册或插件架构。
## Launcher 视觉返工 V3 记录

### 完成内容

- 将 Launcher 登录态从“左品牌栏 + 右侧居中登录卡”改为启动控制台布局：右侧工作区包含顶部启动状态栏、左列本地登录主操作、右列本地账号/工序配置/Shell 启动链路状态卡。
- 收窄左侧品牌栏并补充轻量启动流程块，降低左侧文字堆叠和右侧空白感。
- 将 Profile 态改为紧凑工作区：左侧搜索与 profile 列表，右侧说明与启动链路卡，避免大卡片堆空白。
- 调整 `LauncherTheme.axaml`：补充 `launcher-status-card`、`launcher-steps-card`、`launcher-section-title`、`launcher-caption`、`launcher-step-dot` 等样式，输入框和窗口控制按钮继续走主题资源。
- 补齐 Launcher 中英文资源键：启动控制台标题、状态卡、启动流程、Profile 说明等均成对写入 `zh-CN.xaml` 和 `en-US.xaml`。
- 将改密窗口关闭按钮从裸文本字符替换为同源路径图标，减少乱码和廉价感风险。

### 架构边界

- 本轮仍只改 Avalonia Launcher UI、主题、资源和文档记录。
- 未修改 `src/Modules/**` 插件页面、插件注册、权限、资源或业务逻辑。
- 未修改 Application、Domain、Runtime、Infrastructure 合同。
- 未修改 PLC、MES、Cloud、缓存、数据库、上传、重试、死信链路。
- 未修改本地账号认证算法、profile 文件格式或 Shell 启动流程。

### 已执行验证

- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore`：通过，0 warning，0 error。
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore`：通过，24 passed，0 failed。
- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore`：通过，0 warning，0 error。
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore`：通过，59 passed，0 failed。
- `rg "#[0-9A-Fa-f]{6,8}" src/Edge/IIoT.Edge.Launcher.Avalonia/Views src/Edge/IIoT.Edge.Launcher.Avalonia/Resources -n`：无命中。
- `rg "鏈|璐|瀵|鐧|淇|鎼|鈭|鈻|脳" src/Edge/IIoT.Edge.Launcher.Avalonia/Views src/Edge/IIoT.Edge.Launcher.Avalonia/Resources -n`：无命中。

### 剩余风险

- 本记录对应的是代码和静态验证，真实窗口截图仍需人工验收；重点看 Launcher 登录态右侧信息密度、Profile 态列表密度、窗口按钮观感和 1366x768 下是否拥挤。
- Shell 中央插件页面仍未获批修改，截图里的“匀浆出料数据”旧表格质感只能由宿主容器缓解，无法在本轮彻底重做。

## Launcher v2 Step 5 单卡片两步流记录

### 完成内容

- 将 `MainWindow.axaml` 从登录/Profile 双 Panel 显隐结构改为 `TransitioningContentControl + CrossFade` 的单卡片两步流，当前内容统一绑定 `LauncherMainViewModel.CurrentView`。
- 在 `MainWindow.axaml` 中新增 `Window.DataTemplates`，将 `LauncherLoginViewModel` 映射到 `LauncherLoginView`，将 `LauncherProfileViewModel` 映射到 `LauncherProfileView`。
- 新增 `LauncherLoginView.axaml` 和 `LauncherProfileView.axaml`，分别承载登录表单与 Profile 选择工作区；用户可见静态文案继续使用 `Launcher_*` 资源键。
- 精简 `MainWindow.axaml.cs`，移除旧的输入框查找、Panel 显隐切换和登录/Profile 按钮点击处理，只保留窗口拖动、窗口控制和修改密码窗口打开逻辑。
- `LucideAvalonia` 在当前 Avalonia 运行时测试中触发 `MissingMethodException`，本步将新视图图标降级为本地 `Path` 线性图标，避免为了图标破坏 Launcher 运行时或新增依赖。

### 架构边界

- 本步只修改 Launcher 主窗口 View、拆分后的子 View、主窗口 code-behind 和迁移记录。
- 未修改 `ILocalLauncherAuthService`、`ILauncherProfileCatalog`、`IShellLaunchService`、`IPasswordHasher` 接口或业务语义。
- 未修改本地账号认证算法、Profile 文件格式、Shell 启动流程、PLC、MES、Cloud、缓存、上传、重试或死信链路。
- 未修改 `src/Modules/**` 插件页面、插件注册、权限、资源或业务逻辑。
- 未进入 Step 6，`ChangePasswordWindow` 视觉收口仍按后续步骤单独处理。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj --no-restore`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore`：通过，26 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore`：通过，59 passed，0 failed。
- `rg "LoginPanel|ProfilePanel|UserNameInput|PasswordInput|LoginButton_Click|LaunchProfileButton_Click|ChangePasswordButton_Click"` 针对新 `MainWindow` 检查：无命中。
- `rg "[一-龥]"` 针对 `MainWindow.axaml`、`LauncherLoginView.axaml`、`LauncherProfileView.axaml` 检查：无命中。
- 常见乱码片段扫描针对 Launcher Views、ViewModels、Resources 检查：无命中。

### 剩余风险

- 本步完成的是结构拆分和静态验证，真实窗口截图仍需人工验收；重点看登录页 CrossFade 切换、Profile 列表密度、1366x768 下中文是否挤压。
- `LucideAvalonia` 包仍保留在项目中，因为 Step 0 已接入且本步不做依赖回滚；后续若继续使用 Lucide，需要先解决其与当前 Avalonia 运行时的兼容问题。

## Launcher v2 Step 6 修改密码窗口同源精修记录

### 完成内容

- 将 `ChangePasswordWindow.axaml` 改为与 Launcher 主窗口同源的浅灰画布、白色 shell、卡片化表单结构。
- 修改密码窗口改为无系统标题栏，顶部透明区域可拖动，右上角关闭按钮沿用本地 `Path` 线性图标。
- 表单从占位符堆叠改为“标签 + 输入框”结构，覆盖账号、旧密码、新密码、确认新密码四项。
- 底部取消和确认修改按钮改用 `launcher-ghost` / `launcher-primary`，与 Launcher 主流程按钮统一。
- 补齐 `Launcher_ChangePassword_Subtitle`、`Account`、`OldPassword`、`NewPassword`、`ConfirmPassword`、`Submit`、`Cancel` 等中英文资源键。
- 在 `LauncherTheme.axaml` 中补齐 `launcher-card`、`launcher-subtitle`、`launcher-label` 最小共用样式，服务本步弹窗布局。

### 架构边界

- 本步只修改 Launcher 修改密码窗口、Launcher 主题样式、Launcher 中英文资源和阶段记录。
- 未修改 `ILocalLauncherAuthService`、`IPasswordHasher`、`LauncherMainViewModel.ChangePasswordAsync(...)` 的接口或业务语义。
- 未新增 ViewModel、业务服务、DI 注册或密码策略。
- 未修改本地账号认证算法、Profile 文件格式、Shell 启动流程、主客户端 Shell、插件模块、PLC、MES、Cloud、缓存、上传、重试或死信链路。
- 未进入 Step 7。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，26 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。
- `rg "[一-龥]" src\Edge\IIoT.Edge.Launcher.Avalonia\Views\ChangePasswordWindow.axaml`：无命中。
- `Compare-Object` 检查 `zh-CN.xaml` 与 `en-US.xaml` 中 `Launcher_ChangePassword_*` key：无差异。
- 常见乱码片段扫描针对 Launcher Views/Resources 检查：无命中。

### 剩余风险

- 本步未启动真实桌面窗口做截图验收；仍需人工确认修改密码窗口在实际 owner 居中、标题栏拖动、关闭按钮 hover、错误文案显示和 1366x768 下的空间感。
- `LucideAvalonia` 兼容问题仍未处理，本步继续使用本地 `Path` 图标，避免扩大依赖或引入运行时异常。

## Launcher v2 Step 7 DI / Resources 收尾记录

### 完成内容

- 核查并收口 Launcher DI：`LauncherLoginViewModel`、`LauncherProfileViewModel`、`LauncherMainViewModel` 均已注册。
- 调整 `IAvaloniaLanguageService` 注册，让资源加载同时覆盖 Launcher assembly 与 `IIoT.Edge.UI.Avalonia` assembly，并显式使用 `zh-CN` 作为默认文化。
- 新增仓库根 `.editorconfig`，为 `*.axaml` 与 `*.xaml` 固定 `utf-8-bom`，降低后续 XAML 中文乱码退化风险。
- 完成 Launcher 残留扫描：旧 Login/Profile 双 Panel 命名、AXAML inline 中文、资源 key 不一致、常见乱码片段均未检出。

### 架构边界

- 本步只修改 Launcher DI 注册、仓库编码规则和阶段记录。
- 未修改认证、Profile、Shell 启动、PLC、MES、Cloud、缓存、上传、重试、死信、插件模块或主客户端业务。
- 未引入新 NuGet 包，未处理 `LucideAvalonia` 兼容问题。
- 未进入主客户端页面改造。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，26 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。
- `rg "LoginPanel|ProfilePanel|UserNameInput|PasswordInput|LoginButton_Click|LaunchProfileButton_Click"` 针对新 `MainWindow` 检查：无命中。
- `rg "[一-龥]"` 针对 Launcher Views 下 `*.axaml` 检查：无命中。
- `Compare-Object` 检查 `zh-CN.xaml` 与 `en-US.xaml` 中 `Launcher_*` key：无差异。
- 常见乱码片段扫描针对 Launcher Views/Resources 检查：无命中。

### 剩余风险

- Launcher v2 代码层已完成 Step 0-7，但仍未进行真实窗口截图验收。
- 人工验收重点：登录页、Profile CrossFade、修改密码弹窗、标题栏拖动、关闭按钮 hover、1366x768 下中文与卡片间距。
- 人工认可 Launcher 后，再进入主客户端页面改造；未确认前不扩大到主客户端或插件页面。

## Launcher v3 Material 图标与单工序入口返工记录

### 完成内容

- 在 Launcher 项目中接入现有集中版本管理的 `Material.Avalonia` 与 `Material.Icons.Avalonia`，并用 `MaterialIcon` 替换 Launcher 主窗口、登录页、Profile 页、修改密码窗口中的手写控制类图标。
- 去掉 Launcher 主窗口外层厚灰底和大灰框，主 shell 改为直接贴合白色窗口画布，只保留轻量圆角与阴影。
- 扩大 Launcher 拖动热区到 shell 空白区，交互控件区域不会触发拖动；同时支持空白区域双击最大化/还原。
- 右上角最小化、最大化、关闭按钮统一为 40×40 点击区和 Material 图标，减少原先符号按钮的廉价感。
- 新增 `LauncherProfileGroupViewModel`，在 UI 层按 `MachineProfile` 分组展示 profile；同一个 `HomogenizationLine` 只显示一个工序主卡片，默认启动不带 `--start-runtime` 的 UI-only profile。
- 保留两个原始 profile 配置和 `IShellLaunchService` 启动语义，不修改 `launcher.profiles.json`、Profile 文件格式、认证、改密或 Shell 启动服务。

### 边界

- 本轮只修改 Launcher UI、Launcher ViewModel 展示分组、Launcher 项目引用、Launcher 测试和本记录。
- 未修改主客户端 Shell 页面、`src/Modules/**` 插件页面、PLC、MES、Cloud、缓存、上传、重试、死信链路或运行时业务。
- 未处理 `LucideAvalonia` 兼容问题；本轮新增 Material 图标路径，后续 Launcher 不再依赖 Lucide 控件渲染。

### 验证

- `dotnet restore src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj`：通过。
- `dotnet build src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。
- `rg "<Path" src\Edge\IIoT.Edge.Launcher.Avalonia\Views`：无命中。
- `rg "LoginPanel|ProfilePanel|UserNameInput|PasswordInput|LoginButton_Click|LaunchProfileButton_Click|TitleBar_PointerPressed"` 针对 `MainWindow.axaml*`：无命中。
- Launcher Views 中 `*.axaml` 内联中文扫描：无命中。
- `zh-CN.xaml` 与 `en-US.xaml` 的 `Launcher_*` key 集合比对：无差异。

### 剩余风险

- 真实窗口仍需人工截图验收，重点看白色 shell 是否不再出现灰色方块感、右上角窗口按钮观感、空白区拖动、双击最大化、登录卡片节奏和 1366×768 下的拥挤程度。
- Profile 卡片当前保留同一工序下的变体提示；如人工验收仍觉得信息偏多，下一轮可把变体提示收进高级入口。
- 主客户端页面截图中暴露的旧表格、右侧状态栏拥挤和插件页面粗糙问题未在本轮处理，需要单独进入主客户端 Dashboard v2 批次。

## Launcher v3.1 图标修复与浅色层次返工记录

### 完成内容

- 在 Launcher `App.axaml` 中补入 `MaterialIconStyles`，修复 `MaterialIcon` 控件只占位、不绘制图标的问题；窗口按钮、品牌、账号、密码、搜索、刷新、返回、启动、修改密码等图标继续统一走 Material Icons。
- 将 Launcher 主视觉从纯白一片调整为浅暖灰层次：窗口画布、主 shell、左侧品牌区、右侧工作区和功能卡使用不同的浅色面，避免厚灰外框，也避免页面发空。
- 收紧登录页布局，移除 `Auto,*,Auto` 拉伸导致的中部大空洞；登录标题、说明、输入框、状态提示、主按钮、副按钮改为连续垂直节奏。
- 输入框补齐垂直内容居中与中性 focus 样式，避免默认蓝色焦点框和 placeholder 偏上。
- 收紧 Profile 页面外框高度，工序卡与右侧说明卡不再拉满整屏；同一 `MachineProfile` 下仍只显示一个“匀浆工序客户端”主入口。
- Profile 模式 chip 改为可换行显示，避免同一工序下的 UI-only / 运行联调提示挤压主标题或被截断。

### 边界

- 本轮只修改 Launcher 的 `App.axaml`、主题资源、登录视图、Profile 视图和本阶段记录。
- 未修改主客户端 Shell、插件页面、PLC、MES、Cloud、缓存、上传、重试、死信链路或运行时业务。
- 未修改本地账号认证、修改密码、Profile 加载、Profile 配置文件格式或 Shell 启动参数。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.Launcher.Avalonia\IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：单独运行通过，59 passed，0 failed。并行运行时曾命中 Avalonia 全局资源刷新空引用，已通过单独重跑排除本轮 Launcher 改动导致的稳定失败。
- Launcher Views `*.axaml` 内联中文扫描：无命中。
- Launcher Views/Resources/ViewModels 乱码片段扫描：无命中。
- `zh-CN.xaml` 与 `en-US.xaml` 的 `Launcher_*` key 集合比对：无差异。
- Launcher Views 中手写 `<Path>` / `Geometry=` / `Data="M` 控制图标扫描：无命中。

### 剩余风险

- 本轮仍需要人工真实窗口验收图标是否全部可见、浅色层次是否足够、登录卡片是否不再空、Profile 页面是否不挤不空。
- 主客户端 Dashboard、插件表格和右侧状态栏的老旧视觉问题未在本轮处理，需单独进入主客户端 Dashboard v2 批次。

## 主客户端 Dashboard v2 第一批改造记录

### 完成内容

- 在主客户端 `App.axaml` 中加载 `MaterialIconStyles`，保证 Shell、导航和 Monitor 首屏中的 Material 图标在真实运行时可渲染。
- 扩展 `IndustrialTheme.axaml` 的主客户端视觉基线：浅暖灰画布、大白 shell、KPI 卡、工位画布节点、右侧状态卡、mini tab、表格行高和窗口按钮 hover 状态。
- 将 `MonitorViewPage` 重排为 Dashboard v2 第一屏标杆：
  - 顶部 4 张 KPI 卡使用现有真实绑定：今日产量、良率、不良数、设备数。
  - 中部左侧为设备产量趋势列表，继续使用 `Devices` 与 `TotalAll` 快照数据。
  - 中部中间为生产工位画布，展示进料、预处理、均质、检测、包装和 PLC/MES/Cloud 链路语义。
  - 右侧为运行状态 tabs、PLC/MES/Cloud/缓存状态卡和最新告警卡。
  - 底部为设备状态表，保留现有设备、工序、产量、良品、不良、良率、云端、MES 列语义。
- 补齐 Monitor 新增文案的 `zh-CN.xaml` / `en-US.xaml` 双语资源，新增用户可见文字均通过资源键引用。

### 边界

- 本轮只修改主客户端 Shell 资源加载、共享 Avalonia 主题、Monitor 第一屏视图、Navigation 双语资源和本阶段记录。
- 未修改 Launcher、认证、Profile 加载、Shell 启动、PLC、MES、Cloud、缓存、上传、重试、死信链路或模块运行时业务。
- 未修改 `MonitorViewModel` 的业务计算、命令、数据源或后端字段；没有新增节拍、在线率等后端当前不存在的生产字段。
- 未进入其它插件页面、硬件配置页、I/O 页或主客户端后续批次。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。期间曾出现一次 Avalonia 全局资源刷新空引用，单独重跑失败用例通过，随后整组重跑通过。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
- Shell / Navigation / Monitor 相关 AXAML 内联中文扫描：无命中。
- Shell / Navigation 双语资源 key 集合比对：无差异。
- Shell / Navigation / Monitor 相关 AXAML 手写 `Path`、`Geometry=`、`Data="M` 控制图标扫描：无命中。
- Shell / Navigation / Monitor 相关 Views/Resources 乱码片段扫描：无命中。

### 剩余风险

- 本批仍需人工真实窗口验收 1366×768 下的 KPI、工位画布、右侧状态卡和底部表格是否不挤、不空、不溢出。
- 生产工位画布当前只使用现有运行链路语义，不展示后端尚未提供的节拍、在线率或工位实时状态字段；如后续要更真实，需要等业务数据契约单独确认。
- 其它主客户端页面仍可能保留旧控件质感，需在 Dashboard v2 人工认可后分批迁移。

## 主客户端 Dashboard v2 第一批精修记录

### 完成内容

- 将主客户端默认窗口从 `1360x860` 收敛为 `1280x760`，并把 shell 外边距从 `30` 收窄到 `20`，优先保证 1366×768 下可以完整看第一屏。
- 收窄顶部搜索区固定宽度，降低 Header 在中等屏幕下与状态 chip、用户区、窗口按钮互相挤压的风险。
- 微调 `MonitorViewPage` 的三列比例、KPI 行高、底部表格高度、Workbench 节点间距和右侧运行状态栏宽度，减少 1366×768 下的拥挤感。
- 在共享 `IndustrialTheme.axaml` 中补充 compact 卡片样式，并降低 Dashboard 表格表头与行高，让底部设备状态表更适合作为第一屏信息表。
- 修复 `StartupErrorWindow.axaml.cs` 中遗留的启动诊断兜底文案乱码，改为 `Shell_StartupError_*Missing` 资源键，并补齐中英文资源。

### 边界

- 本轮只修改主客户端 Shell/Navigation/Monitor/共享主题和启动错误窗口资源化收口。
- 未修改 Launcher 布局、插件模块页面、PLC、MES、Cloud、缓存、上传、重试、死信、模块运行时或业务服务。
- 未修改 `MonitorViewModel` 的数据来源、命令、业务计算和后端字段。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
- Shell / Navigation / Monitor 相关 AXAML 内联中文扫描：无命中。
- Shell / Navigation / Monitor 相关 AXAML 手写 `Path`、`Geometry=`、`Data="M` 控制图标扫描：无命中。
- Shell / Navigation / Monitor 相关 Views/Resources 常见乱码片段扫描：无命中。
- Shell 中英文资源 key 集合比对：无差异。

### 剩余风险

- 仍需人工真实窗口验收 1366×768、1440×900、1920×1080 三档观感；尤其看中部工位画布、右侧状态卡和底部设备表是否刚好。
- `src/Modules/**` 插件页面仍未进入本批，截图中的匀浆模块页面需要单独批准后再改。

## 主客户端 Shell v2.1 去框与日志前置记录

### 完成内容

- 右侧工具区不再使用 Dock ToolPane 的默认蓝色标题条、粗边框和嵌套框，改为 Shell 自有的浅色圆角工具面板。
- 主 Dock 区只承载主业务文档，右侧工具页通过 Shell 级 `TabControl` 展示，默认优先显示“运行日志”。
- 将“运行日志”固定为左侧 rail 入口，现场排查可一键打开，不再藏在“设备信息”面板内。
- 设备状态面板语义收敛为“设备状态 / 告警 / 审计”等真实运行信息，移除默认展示的“缓存与同步边界”说明卡。
- 主窗口默认尺寸从 `1280x760` 上调到 `1600x1000`，最小尺寸保留 `1180x760`，为 1900×1200 大屏验收留出更合理的工作区比例。
- 扩展共享主题样式，新增无 Dock 框感的主文档区、右侧工具壳、右侧 tabs、日志提示卡等样式。
- 补齐 Panels 中英文资源键：运行日志、设备状态、只读、日志说明和一键日志提示。

### 边界

- 本批只修改主客户端 Shell、Panels 视图、共享主题和资源文案。
- 未修改 `src/Modules/**`，中央“匀浆出料数据”等模块页面仍保持现状，需要单独批准后再逐页改造。
- 未修改 PLC、MES、Cloud、缓存、上传、重试、死信、模块运行时、认证、启动链路和业务服务。
- 未改变 Dock 注册协议和插件加载协议，只调整 Shell 层的工具面板展示方式。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：重试后通过，27 passed，0 failed。
- Shell / Panels / Monitor / Theme 相关 AXAML 内联中文扫描：无命中。
- Panels `zh-CN.xaml` 与 `en-US.xaml` 资源键集合比对：无差异。
- 目标范围内未新增手写 Path 图标，乱码片段扫描无命中。

### 剩余风险

- 需要人工在真实窗口中检查 `1900x1200`、`1600x1000`、`1366x768` 三档视觉效果，尤其确认右侧 tabs 是否仍有默认控件痕迹。
- 中央模块页面仍可能保留旧表格、旧边框和旧排版，这是模块页面自身 UI，未纳入本批范围。
- 如人工验收认可 Shell v2.1 方向，下一批建议单独进入“匀浆模块页面 v2”或“日志中心 v2”。

## 主客户端 Shell v2.1 精修与日志中心 v2 记录

### 完成内容

- 将主客户端默认窗口基准调整为 `1900x1200`，保留 `1180x760` 最小尺寸，优先服务大屏现场工作台，同时保留紧凑屏可用性。
- 右侧工具区从 `TabControl` 默认控件切换为 Shell 自定义轻量 tab 列表，避免默认控件蓝条、硬边和突兀选中态。
- 收窄主内容与右侧工具区间距，右侧工具壳去掉边框和阴影，减少“被框住”的视觉压迫。
- 将运行日志面板升级为日志中心 v2：顶部显示日志总数、警告数、错误数；中部显示日志文件选择、刷新、清空；下方显示最新摘要和日志卡片列表。
- 日志无文件时的启动诊断兜底文案改为资源化文本，避免 ViewModel 内硬编码用户可见中文。
- 新增日志中心相关中英文资源键，并补充共享主题中的日志指标卡、日志项、右侧工具 tab 列表样式。

### 边界

- 本批只修改主客户端 Shell、Panels 视图/ViewModel、共享主题、Panels 资源和阶段记录。
- 未修改 `src/Modules/**`，匀浆模块页面仍保持现状，需单独批准后再进入模块页面 v2。
- 未修改 PLC、MES、Cloud、缓存、上传、重试、死信、模块运行时、认证、启动链路和业务服务。
- 日志中心 v2 只复用现有日志文件、`ILogService.EntryAdded` 和启动诊断快照，不新增后端字段，不 mock 日志。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
- Shell / Panels / Monitor / Theme 相关 AXAML 内联中文扫描：无命中。
- Shell / Panels / Monitor 相关 AXAML 手写 `Path`、`Geometry=`、`Data=` 控制图标扫描：无命中。
- Shell / Panels / Navigation / Theme 相关范围乱码片段扫描：无命中。
- Panels `zh-CN.xaml` 与 `en-US.xaml` 资源键集合比对：无差异。

### 剩余风险

- 需要人工真实窗口验收 `1900x1200`、`1600x1000`、`1366x768` 三档，重点看右侧轻量 tab 是否仍有默认 ListBox 痕迹。
- 日志级别颜色当前只做统计数字区分，日志行内部仍保持统一卡片；如需要更强告警识别，可下一批做日志级别 chip 颜色分层。
- 中央模块页面的旧表格和硬边框不是本批范围，仍需单独进入“匀浆模块页面 v2”。

## 匀浆模块页面 v2 单页标杆记录

### 完成内容

- 将 `HomogenizationDataPage` 从旧的单层 `Border + DataGrid` 改为浅暖灰画布上的主业务数据卡结构。
- 页面顶部增加标题、说明和当前记录状态 chip，底部使用白色圆角表格卡承载最近出料记录。
- 表格保留现有 `Records` 数据源和原有列语义，只调整列宽、滚动和容器层级，不新增生产字段或 mock 数据。
- 增加无记录空态，使用现有运行上下文刷新结果判断是否显示，避免空白页面和裸表格硬框。
- 匀浆模块新增中英文资源键，所有新增用户可见文案仍由模块自己的资源字典承载。
- 共享主题只做右侧工具 tab / 日志列表默认边框的轻量收口，减少默认 ListBox 痕迹。

### 边界

- 本批只修改匀浆模块 Presentation 层、模块资源、共享 Avalonia 主题和本阶段记录。
- 未修改 PLC、MES、Cloud、缓存、上传、重试、死信、模块运行时、认证、启动链路和业务服务。
- 未修改匀浆模块配置、上传映射、运行任务、数据模型和启动流程。
- 未进入其它模块页面或宿主配置页面。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。首次并行运行时遇到 Launcher `obj` 文件锁，顺序重跑通过。
- 匀浆模块 Presentation AXAML 内联中文扫描：无命中。
- 匀浆模块中英文资源 key 集合比对：无差异。
- 匀浆模块 Presentation 与本批共享主题范围内手写 `Path` / `Geometry` / `Data="M"` 控制图标扫描：无命中。
- 匀浆模块与本批共享主题范围内常见乱码片段扫描：无命中。

### 剩余风险

- 仍需人工真实窗口验收 `1900x1200`、`1600x1000`、`1366x768` 三档，确认匀浆页在 Shell 容器中不空、不挤、不出现硬框压迫感。
- 当前空态图形采用轻量文本占位，不新增图标包；如需要更强品牌感，可后续统一接入已稳定的图标资源。
- 其它模块页面仍保持旧式表格或旧式布局，需要在匀浆页标杆认可后逐页迁移。

## 主客户端真实窗口验收与宿主页细腻化收口记录

### 完成内容

- 在共享 `IndustrialTheme.axaml` 中收敛宿主页基础视觉：`edge-page-panel` 默认增加页面留白，`edge-table-card` 增加裁剪，表格表头、行高、单元格内边距和 hover 状态统一到更细腻的白卡表格样式。
- 给宿主页常见 `TabControl / TabItem` 增加浅色 pill 风格，减少诊断、硬件配置等页面里的默认控件痕迹。
- 将日志中心的日志行从普通文本提升为分级卡片：`INFO / DEBUG / WARN / ERROR / FATAL` 根据级别显示不同的轻量状态背景、dot 和 level chip。
- 修复日志文件下拉显示文本，去掉历史乱码格式串，改为稳定的 `文件名 - 更新时间` 展示。
- 日志展示新增 `LogEntryRow` 作为纯展示层包装，只派生级别状态，不改变日志采集、文件读取、`ILogService.EntryAdded` 或运行链路。

### 边界

- 本批只修改主客户端共享主题、日志面板 View/ViewModel 和阶段记录。
- 未修改 PLC、MES、Cloud、缓存、上传、重试、死信、模块运行时、认证、启动链路和业务服务。
- 未修改除已批准匀浆 Presentation 外的其它 `src/Modules/**` 页面。
- 未新增生产字段、mock 数据或后端接口。

### 已执行验证

- `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。

### 剩余风险

- 仍需继续执行 Shell 测试、Launcher 测试和扫描验收。
- 真实窗口仍需人工检查 `1900x1200`、`1600x1000`、`1366x768` 三档，重点看宿主页表格密度、Tab 观感、日志分级颜色是否足够清楚但不过度。

### 验证补充

- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，59 passed，0 failed。
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
- 目标 AXAML inline 中文扫描：无命中。
- Panels/ViewModels/Theme 常见乱码片段扫描：无命中。
- 目标范围手写 Path / Geometry / Data="M" 图标扫描：无命中。
- Panels `zh-CN.xaml` 与 `en-US.xaml` 资源 key 集合比对：无差异。

## 主客户端诊断页与硬件配置页细腻化记录

- 完成内容：
  - 将宿主诊断页从单一大 Tab + 裸表格结构调整为“页头、诊断摘要卡、圆角表格工作区”的排版。
  - 将硬件配置页调整为“页头、设备数量摘要、圆角配置工作区”的结构。
  - 将网络设备、串口设备、I/O 映射三个子页从 DockPanel 裸表格改为工具栏卡 + 圆角表格卡。
  - 在共享主题中补充 `edge-summary-card`、`edge-toolbar-card`、`edge-table-shell`、`edge-side-list` 等可复用样式。
- 边界：
  - 本批只修改 Avalonia 宿主 UI 展示层和共享主题。
  - 未修改 PLC/MES/Cloud、缓存上传、重试、死信、模块运行时、认证、启动链路和配置模型。
  - 未进入其它 `src/Modules/**` 页面。
- 验证命令：
  - `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
  - `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
  - `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
- 测试结果：
  - Shell 构建通过，0 警告，0 错误。
  - `IIoT.Edge.AvaloniaShell.Tests` 通过 59 个测试。
  - `IIoT.Edge.Launcher.Tests` 通过 27 个测试。
  - 目标 AXAML inline 中文扫描、乱码片段扫描、手写 Path 图标扫描均无命中。
- 剩余风险：
  - 仍需要用户在真实窗口中查看诊断页和硬件配置页的 1900×1200、1600×1000、1366×768 观感。
  - 当前只完成宿主诊断和硬件配置页细腻化，其它宿主页还需要按标杆逐页推进。
- 下一阶段进入条件：
  - 用户人工确认诊断页和硬件配置页视觉方向可接受后，再进入产能、数据、配方、参数、PLC 任务绑定等宿主页的下一批迁移。

## Launcher 与 Monitor 首页真实化收口记录

- 完成内容：
  - `launcher.profiles.json` 收口为单个现场入口“匀浆产线”，默认携带 `--start-runtime`；分组主入口排序改为 runtime 优先，避免同一 MachineProfile 下回退到 UI-only。
  - `LogViewModel` 文件日志解析不再用当前时间填充历史日志行；能解析行首时间时展示真实时间，无法解析时展示“未知时间”。
  - Monitor 首页第四个 KPI 改为 PLC 在线数 / 总数；左侧趋势改为今日设备产量分布，进度条按当前设备快照归一化，不再显示 7 天折线含义。
  - Monitor 右侧状态区移除计划/任务/告警/审计空 Tab 和硬编码正常状态，改为 PLC、MES、Cloud、缓存队列真实状态卡；最新告警区改为从运行日志中过滤 ERROR/FATAL。
  - Header/Footer 运行状态点按 `IAvaloniaRuntimeState` 显示成功、警告、错误态；Header 产线名称从 `Shell:MachineProfile` 派生，已补匀浆产线显示资源。
- 边界：
  - 本轮只修改 `IIoT.EdgeClient.AvaloniaMigration`。
  - 未新增 Cloud/API/Runtime/PLC/MES 公共接口，未修改原 WPF EdgeClient、Cloud、AICopilot、插件 Runtime/PLC/MES/Cloud 链路。
  - 截图中的节拍、7 天折线、计划/任务/告警/审计 Tab、清理缓存弹窗因缺少稳定真实来源未照抄。
- 验证：
  - `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，60 passed，0 failed。
  - `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
- 剩余风险：
  - 仍需人工在真实窗口检查 `1900x1200`、`1600x1000`、`1366x768` 三档截图，重点确认无原生标题栏、右侧日志常驻、状态无假正常、中文不溢出。

## 主客户端真实窗口验收与窄屏收口记录

- 完成内容：
  - 增加主窗口关键区域命名，覆盖 Shell 根容器、主工作区、文档区、右侧工具区、设备状态区和日志区，便于 headless 布局验收定位。
  - Shell 默认主文档从注册顺序的首个插件页收口为 Monitor 首页，真实窗口启动后优先进入生产总览，不再先展示匀浆出料数据页。
  - Avalonia headless 测试宿主补齐真实应用使用的 Fluent、Material Icon、DataGrid、Dock、DialogHost 和工业主题样式，避免布局 smoke 测试绕开真实模板。
  - 新增 `1366x768` 主窗口布局 smoke 测试，验证无原生标题栏、默认激活 Monitor、主工作区、文档区、右侧设备状态区和日志常驻区均可创建并落在工作区内。
  - 使用真实 Launcher/Shell 进程采集窗口截图，输出到 `%TEMP%\iiot-avalonia-visual-check\`，默认不进入仓库。
- 边界：
  - 本轮只修改 `IIoT.EdgeClient.AvaloniaMigration` 的 Avalonia Shell 展示层、测试宿主和迁移记录。
  - 未新增 Cloud/API/Runtime/PLC/MES 公共接口，未修改 PLC、MES、Cloud、缓存、上传、重试、死信、模块运行时或业务服务。
  - 未恢复 Launcher UI-only 正式入口，未新增搜索、假告警、假正常状态或清理缓存弹窗。
- 验证：
  - 基线执行 `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - 基线执行 `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - 基线执行 `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，60 passed，0 failed。
  - 基线执行 `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
  - Navigation、Panels、Shell 三组 `zh-CN.xaml` / `en-US.xaml` 资源键配对检查：无缺项。
  - 真实窗口截图：本机虚拟屏幕为 `1440x900`，`1900x1200` 和 `1600x1000` 会被系统裁成约 `1455x915`，只能作为受限截图；`1366x768` 可生成完整物理窗口截图，并确认 Shell 默认进入 Monitor 首页。
- 剩余风险：
  - 受当前桌面分辨率限制，本机无法完整采集 `1900x1200` 和 `1600x1000` 真实窗口截图，仍需在对应尺寸显示器或目标工控机上人工复核。
  - 当前自动化能验证 `1366x768` 逻辑布局不崩溃、关键区域存在且右侧常驻区不越界；中文细节、DPI 缩放和真实现场数据密度仍需人工看图确认。

## 宿主页第二批细腻化记录

- 完成内容：
  - `CapacityViewPage` 调整为“页头 + 筛选工具卡 + 指标卡 + 表格卡”结构，保留 `DeviceNames`、`QueryModes`、`QueryCommand`、`ExportCommand` 和 `Records` 原绑定，仅收口列宽、卡片尺寸、表格容器和窄屏文本裁剪。
  - `DataViewPage` 调整为同一浅色宿主页结构，保留生产数据查询、导出、今日汇总和记录表绑定；注意当前匀浆模块运行时会用 `HomogenizationDataPage` 覆盖标准 `DataView` 路由，本轮未修改该插件页。
  - `RecipeViewPage` 从整页滚动堆叠改为“状态摘要 + 配方参数表格卡 + 本地应急编辑区”，保留同步云端、切换来源、删除参数和保存本地参数命令。
  - `ParamViewPage` 统一为白色表格工作区，`TabControl` 使用现有 `edge-tool-tabs`，MES/Cloud/插件业务参数组仍按原 `ItemsSource` 和 `CanEdit` 权限展示。
  - `PlcTaskBindingPage` 调整为“设备选择工具卡 + 任务绑定表格卡”的工作台布局，设备模块、启停状态和反馈信息均从现有 `SelectedDevice` / `FeedbackMessage` 派生。
- 边界：
  - 本轮只修改 Avalonia 宿主页展示层 XAML 和布局 smoke 测试。
  - 未修改 Application、Domain、Runtime、Infrastructure、Modules 运行链路；未新增 Cloud/API/Runtime/PLC/MES 公共接口。
  - 未新增 UI 库、图标库、动画库、截图库；未新增用户可见资源键。
  - 未修改 `HomogenizationDataPage`。
- 验证：
  - `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，62 passed，0 failed。
  - `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
  - Navigation、Panels、Shell 三组 `zh-CN.xaml` / `en-US.xaml` 资源键配对检查：无缺项。
  - 新增 `Host_pages_layout_smoke_keeps_key_regions_available_at_1366x768`：直接创建本轮五个目标宿主页，验证 1366x768 下根容器、工具区和关键表格可创建且不抛布局异常。
  - 真实窗口截图输出到 `%TEMP%\iiot-avalonia-visual-check\shell-host-pages-1366x768-printwindow.png`。本机屏幕为 `1440x900`，可完整采集 `1366x768`；`1600x1000`、`1900x1200` 仍受物理屏幕限制。
- 剩余风险：
  - 真实 Shell 默认进入 Monitor 首页，标准 `DataView` 路由在匀浆模块下被插件页覆盖；本轮目标 `DataViewPage` 通过 headless 直接创建验证，仍需在非覆盖路由或独立宿主场景人工确认。
  - 当前真实窗口截图只确认应用可启动和 `1366x768` 物理窗口可采集；五个宿主页的逐页真实截图仍建议在可导航到标准宿主页的目标环境中复核。
  - 若后续需要继续压缩 1366 宽度下主 Shell 默认 Monitor 的右侧区域显示，应作为 Shell/Monitor 窄屏单独批次处理，避免混入本轮宿主页展示层收口。

## 宿主页逐页真实窗口验收与路由覆盖风险收口记录

- 完成内容：
  - 在 `IIoT.Edge.AvaloniaShell.Tests` 中新增宿主页直接承载 visual check，用测试/本地验收窗口直接创建 `CapacityViewPage`、`DataViewPage`、`RecipeViewPage`、`ParamViewPage`、`PlcTaskBindingPage`。
  - 将上一轮宿主页布局 smoke 的页面清单抽为共享验收用例，继续验证 1366x768 下根容器、工具栏、表格或关键区域存在。
  - 新增截图输出：五个标准宿主页在 1366x768 Headless 真窗口下逐页渲染，并保存到 `%TEMP%\iiot-avalonia-visual-check\host-page-direct-1366x768-*.png`。
  - 验收路径明确区分正式 Shell 默认 Monitor 截图和标准宿主页直接承载截图；本轮未改变正式 Shell 路由，也未改变匀浆模块 `DataView` 被 `HomogenizationDataPage` 覆盖的规则。
- 边界：
  - 本轮只修改 AvaloniaShell 测试和迁移记录。
  - 未修改 Application、Domain、Runtime、Infrastructure、Modules 运行链路；未新增 Cloud/API/Runtime/PLC/MES 公共接口。
  - 未新增 UI 库、图标库、截图依赖或跨层服务；继续复用现有 Avalonia.Headless 测试依赖。
  - 未修改 `HomogenizationDataPage`，标准 `DataViewPage` 只通过测试/本地验收 helper 直接承载确认。
- 验证：
  - 基线执行 `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - 基线执行 `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - 最终执行 `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，63 passed，0 failed。
  - 基线执行 `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
  - Navigation、Panels、Shell 三组 `zh-CN.xaml` / `en-US.xaml` 资源键配对检查：无缺项。
  - 新增 `Host_pages_visual_check_writes_direct_1366x768_screenshots`：通过，五张截图均已写入临时目录，文件尺寸均为 1366x768 且非空。
- 截图结果：
  - `%TEMP%\iiot-avalonia-visual-check\host-page-direct-1366x768-capacity.png`
  - `%TEMP%\iiot-avalonia-visual-check\host-page-direct-1366x768-data.png`
  - `%TEMP%\iiot-avalonia-visual-check\host-page-direct-1366x768-recipe.png`
  - `%TEMP%\iiot-avalonia-visual-check\host-page-direct-1366x768-param.png`
  - `%TEMP%\iiot-avalonia-visual-check\host-page-direct-1366x768-plc-task-binding.png`
- 剩余风险：
  - 本机物理屏幕仍为 1440x900，`1600x1000` 和 `1900x1200` 无法完整做真实窗口人工截图，只能记录为受限结论。
  - Headless 逐页截图能确认标准宿主页可直接承载、关键区域可创建和输出非空画面；中文细节、DPI 缩放和真实现场数据密度仍需在目标屏幕或工控机上人工复核。
  - 匀浆模块正式导航仍会覆盖标准 `DataViewPage`，后续若要在正式 Shell 中验收标准 `DataViewPage`，需要使用非覆盖模块或继续走本轮这种验收 helper，不应误把插件页截图当宿主页结果。

## Avalonia 迁移阶段整批收口验收记录

- 完成内容：
  - 对当前未提交 diff 做范围审核，确认改动集中在 Avalonia Shell、Avalonia Launcher、Presentation、Shared Avalonia Theme、测试和本迁移记录。
  - 迁移记录最终口径统一为本节：前文各节保留为阶段历史；当前评审以本节的验证结果、截图结果和剩余风险为准。
  - 明确三类截图口径：正式 Shell 默认进入 Monitor 首页；五个标准宿主页通过测试/本地验收 helper 直接承载截图；匀浆插件 `HomogenizationDataPage` 覆盖标准 `DataViewPage`，不能把插件页误作标准宿主页验收结果。
  - 检查 `%TEMP%\iiot-avalonia-visual-check\`，确认 Launcher、Shell/Monitor、Shell PrintWindow、五个标准宿主页的 1366x768 截图均存在、尺寸正确且非空。
- 边界：
  - 本轮只做整批收口验收和记录整理。
  - 未修改旧 WPF EdgeClient、Cloud、AICopilot、Application、Domain、Runtime、Infrastructure、PLC/MES/Cloud 链路、缓存、上传、重试、死信或模块运行时。
  - 未新增 Cloud/API/Runtime/PLC/MES 公共接口；未新增 UI 库、图标库、截图依赖或跨层服务。
  - 未修改 `HomogenizationDataPage`，未改变正式 Shell 路由和匀浆插件覆盖规则。
- diff 范围审核：
  - 允许范围内：`docs/`、`src/Edge/IIoT.Edge.AvaloniaShell/`、`src/Edge/IIoT.Edge.Launcher.Avalonia/`、`src/Presentation/`、`src/Shared/IIoT.Edge.UI.Avalonia/`、`src/Tests/`。
  - 未发现越界路径：无 `IIoT.CloudPlatform`、旧 `IIoT.EdgeClient`、`AICopilot`、`src/Application`、`src/Runtime`、PLC/MES/Cloud 运行链路文件进入本批 diff。
- 截图结果：
  - `launcher-1366x768.png`：1366x768，非空。
  - `shell-runtime-1366x768-monitor.png`：1366x768，非空。
  - `shell-host-pages-1366x768-printwindow.png`：1366x768，非空。
  - `host-page-direct-1366x768-capacity.png`：1366x768，非空。
  - `host-page-direct-1366x768-data.png`：1366x768，非空。
  - `host-page-direct-1366x768-recipe.png`：1366x768，非空。
  - `host-page-direct-1366x768-param.png`：1366x768，非空。
  - `host-page-direct-1366x768-plc-task-binding.png`：1366x768，非空。
  - `launcher-1600x1000.png`、`launcher-1900x1200.png`、`shell-runtime-1600x1000.png`、`shell-runtime-1900x1200.png` 在当前 1440x900 屏幕上实际输出约 1455x915，只能作为受限截图记录，不能标记为完整通过。
- 最终验证：
  - `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，63 passed，0 failed。
  - `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
  - Navigation、Panels、Shell 三组 `zh-CN.xaml` / `en-US.xaml` 资源键配对检查：无缺项。
- 剩余风险：
  - 本机物理屏幕限制仍未解除，`1600x1000` 和 `1900x1200` 必须在目标显示器或工控机上做完整真实窗口人工验收。
  - Headless 和 PrintWindow 能确认窗口可创建、截图非空、关键区域存在；中文细节、DPI 缩放、真实现场数据密度和长文本仍需人工看图确认。
  - 后续若要在正式 Shell 中验证标准 `DataViewPage`，需要使用非匀浆覆盖路由或继续使用本批直接承载 helper，不能用匀浆插件页替代。

## zip 计划 UTF-8 落地与阶段索引记录

- 完成内容：
  - 将 `C:\Users\jinha\Downloads\avalonia_ui_refactor_plan.zip` 内 11 份 md 按 UTF-8 文件名恢复并落地到 `docs/avalonia-ui-refactor-plan/`。
  - 新增 `docs/avalonia-ui-refactor-plan/00_INDEX.md`，逐项标注 Phase 0 到 Phase 7、SKILL、Codex 提示词模板的当前状态。
  - 索引明确当前 Avalonia UI 改造不是“7 批全部完工”，而是 Launcher、Monitor、Shell、日志、匀浆页、五个宿主页和验收 helper 已进入可评审状态。
  - Phase 7 主题与设计系统固化在该文档落地批次中仍作为下一代码批次；后续执行结果见下方 Phase 7 记录。
- 边界：
  - 本批只修改文档和迁移记录，不修改 `src/`。
  - 未修改 Launcher、Shell、Presentation、Shared Avalonia Theme、Application、Domain、Runtime、Infrastructure、Modules、PLC/MES/Cloud 链路、缓存、上传、重试、死信或模块运行时。
  - 未新增 Cloud/API/Runtime/PLC/MES 公共接口；未新增 UI 库、图标库、截图依赖或跨层服务。
  - zip 原文只做忠实落地；该批当时未重写 SKILL 与 Codex 提示词模板，后续同步结果见“全局 Skill 同步与 Codex 模板收口记录”。
- 文件结果：
  - `00_UI总体改造总计划.md`
  - `01_Phase0_冻结范围与事实核查.md`
  - `02_Phase1_Launcher真实入口收口.md`
  - `03_Phase2_Shell信息骨架重建.md`
  - `04_Phase3_日志与设备状态真实化.md`
  - `05_Phase4_去硬框与视觉圆滑化.md`
  - `06_Phase5_匀浆模块页v2.md`
  - `07_Phase6_宿主页逐页细腻化.md`
  - `08_Phase7_主题与设计系统固化.md`
  - `09_SKILL_Avalonia工业上位机前端规则.md`
  - `10_Codex执行总提示词模板.md`
  - `00_INDEX.md`
- 验证：
  - `docs/avalonia-ui-refactor-plan/` 下 12 份 md 存在，包含 11 份 zip 原计划和 1 份阶段索引。
  - 文档编码扫描：未发现本轮关注的常见乱码片段。
  - 索引内容检查：已列出 Phase 0 到 Phase 7、SKILL、Codex 模板。
  - diff 范围检查：本批新增/修改仅涉及 `docs/avalonia-ui-refactor-plan/` 和本迁移记录；当前工作树仍保留前序 UI diff，未被本批回滚。
  - `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，63 passed，0 failed。
- 剩余风险：
  - Phase 7 主题与设计系统固化已在后续独立批次执行，当前剩余是评审和目标屏幕人工验收。
  - 1600x1000 与 1900x1200 完整真实窗口人工验收仍依赖目标屏幕或工控机。
  - SKILL 与 Codex 提示词模板已在后续全局同步批次中按当前索引和真实数据边界修订，避免重复执行已完成阶段。

## Avalonia Phase 7 主题与设计系统固化记录

- 完成内容：
  - 在 `IndustrialTheme.axaml` 中以 `Edge.*` 作为主客户端 canonical token，补齐 neutral、muted、running、stopped、failed、development 等状态 token，并保留现有 `Ind.*` 兼容 token。
  - 补齐 `Edge.Shadow.*` 和卡片、KPI、状态卡、日志、chip、空态、表单、弹窗等通用 class 的边界样式，页面后续优先复用这些 class。
  - 在 `AppTypography.axaml` 中补充 Micro、Metric、Display 字号 token 和少量文本 class，避免页面继续散落字体口径。
  - 在 Launcher 主题中补齐 `Launcher.Status.*` 语义 token 和状态卡变体，让 Launcher 与主客户端状态色含义对齐。
  - 新增 `docs/Avalonia-Industrial-Design-System.md` 与 `docs/Avalonia-UI-验收清单.md`，明确 token 规则、状态语义、页面模板、禁止事项、三档验收表和真实数据底线。
  - 更新 `docs/avalonia-ui-refactor-plan/00_INDEX.md`，将 Phase 7 标记为“本批已执行，待评审”。
  - 扩展 AvaloniaShell 资源卫生测试，覆盖关键 token/class、两份设计系统文档和索引状态。
- 边界：
  - 未修改 Application、Domain、Runtime、Infrastructure、Modules、PLC/MES/Cloud、缓存、上传、重试、死信链路。
  - 未新增 Cloud/API/Runtime/PLC/MES 公共接口。
  - 未新增 UI 库、图标库、截图依赖或跨层服务。
  - 未修改 `HomogenizationDataPage` 或宿主页业务绑定；本批只做主题、文档和测试固化。
  - Phase 7 当时未修改全局 `.codex/skills`；后续全局 skill 同步结果见下方记录。
- 验证：
  - `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，65 passed，0 failed。
  - `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
  - `git diff --check`：通过，仅有工作区 LF/CRLF 提示，无空白错误。
  - Phase 7 文档乱码片段扫描：`docs/Avalonia-Industrial-Design-System.md`、`docs/Avalonia-UI-验收清单.md`、`docs/avalonia-ui-refactor-plan/00_INDEX.md` 未命中本轮关注的常见乱码片段。
- 剩余风险：
  - `1600x1000` 与 `1900x1200` 完整真实窗口人工验收仍依赖目标屏幕或工控机。
  - Phase 7 固化的是仓库内设计系统与测试约束；全局 skill 同步结果见下方记录。
  - 后续页面如要接入新业务状态，必须先确认真实数据来源，不得为了视觉完整度新增 mock 状态。

## Avalonia 全局 Skill 同步与 Codex 模板收口记录

- 完成内容：
  - 新增全局专用 skill：`C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish\`，用于 `IIoT.EdgeClient.AvaloniaMigration` 的 Avalonia 工业上位机 UI、主题、资源、日志、设备状态和验收任务。
  - 保留现有 `iiot-frontend-polish` 的 Cloud/Vue 与 AICopilot 职责，不把 Edge Avalonia 规则混入旧 skill。
  - 新增 `SKILL.md`，固化 Phase 状态读取流程、真实数据底线、主题 token 规则、验证命令和停止条件。
  - 新增 `references/phase-status.md`、`references/design-system.md`、`references/visual-acceptance.md`，将当前 Phase 0-7 状态、设计系统和验收口径浓缩到全局 skill reference。
  - 用 `skill-creator` 脚本生成 `agents/openai.yaml`，显示名为 `IIoT Avalonia HMI Polish`。
  - 更新 `docs/avalonia-ui-refactor-plan/09_SKILL_Avalonia工业上位机前端规则.md`，标记规则已同步到全局 skill，并移除从 zip 原 Phase 重启执行的倾向。
  - 更新 `docs/avalonia-ui-refactor-plan/10_Codex执行总提示词模板.md`，要求先读 `00_INDEX.md` 和迁移记录，再判断当前 Phase 是否已完成。
  - 更新 `docs/avalonia-ui-refactor-plan/00_INDEX.md`，将 SKILL 与 Codex 模板状态改为已同步，待实际使用验证。
- 边界：
  - 未修改 `src/` 代码。
  - 未修改 Application、Domain、Runtime、Infrastructure、Modules、PLC/MES/Cloud、缓存、上传、重试、死信链路。
  - 未新增 Cloud/API/Runtime/PLC/MES 公共接口。
  - 未修改 `C:\Users\jinha\.codex\skills\iiot-frontend-polish\`。
  - 未新增脚本依赖，只复用 `skill-creator` 自带脚本。
- 验证：
  - `python C:\Users\jinha\.codex\skills\.system\skill-creator\scripts\quick_validate.py C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish`：通过，`Skill is valid!`。
  - 新增 skill 与仓库同步文档乱码片段扫描：通过，未命中本轮关注的常见乱码片段。
  - `git diff --check`：通过，仅有工作区 LF/CRLF 提示，无空白错误。
  - `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，65 passed，0 failed。
- 剩余风险：
  - 新 skill 已落地到全局目录，但当前 Codex 会话的 skills 列表可能需要刷新或新开会话后才显示。
  - 这批只同步规则，不补 `1600x1000`、`1900x1200` 目标屏幕截图。

## Avalonia 迁移整批评审交付记录

- 完成内容：
  - 新增 `docs/Avalonia-UI-整批评审交付说明.md`，汇总本批目标、改动分类、未触碰边界、真实数据底线、截图资产、封版验证和剩余人工验收项。
  - 复核当前整批 diff，改动集中在 Avalonia Shell、Launcher、Presentation、Shared Avalonia Theme、测试、docs 和全局 `iiot-avalonia-hmi-polish` skill。
  - 复核未跟踪文件，包含设计系统文档、验收清单、zip 计划文档和 `Presentation` 展示层 helper；未发现 Cloud、旧 WPF EdgeClient、AICopilot、Application、Domain、Runtime、Infrastructure、PLC/MES/Cloud 链路文件误入。
  - 复核 `%TEMP%\iiot-avalonia-visual-check\` 截图资产，确认 Launcher、Shell/Monitor、Shell 宿主页和五个标准宿主页 `1366x768` 截图均存在、尺寸正确且非空。
  - 明确 `launcher-1600x1000.png`、`launcher-1900x1200.png`、`shell-runtime-1600x1000.png`、`shell-runtime-1900x1200.png` 在当前 `1440x900` 屏幕上实际输出约 `1455x915`，只能作为受限截图记录，不能标记为完整通过。
- 边界：
  - 本轮封版只做交付说明、记录收口、截图复核和验证。
  - 未修改 `src/` UI 行为。
  - 未修改 Application、Domain、Runtime、Infrastructure、Modules runtime、PLC/MES/Cloud、缓存、上传、重试、死信链路。
  - 未新增 Cloud/API/Runtime/PLC/MES 公共接口。
  - 未修改 `C:\Users\jinha\.codex\skills\iiot-frontend-polish\`。
- 验证：
  - `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，0 warning，0 error。
  - `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，65 passed，0 failed。
  - `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`：通过，27 passed，0 failed。
  - `python C:\Users\jinha\.codex\skills\.system\skill-creator\scripts\quick_validate.py C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish`：通过，`Skill is valid!`。
  - 文档/skill 乱码片段扫描：通过，未命中本轮关注的常见乱码片段。
  - `git diff --check`：通过，仅有工作区 LF/CRLF 提示，无空白错误。
  - diff 范围检查：通过，未发现 Cloud、旧 WPF EdgeClient、AICopilot、Application、Domain、Runtime、Infrastructure、PLC/MES/Cloud 运行链路文件误入。
- 剩余风险：
  - `1600x1000` 与 `1900x1200` 真实窗口完整验收仍依赖目标屏幕或工控机。
  - 当前交付材料可支持评审，但正式 PR 审核后若出现修改意见，需要单独对齐后再处理。
