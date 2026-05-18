# Phase 2：Shell 信息骨架重建小计划

> 本阶段目标：把 AvaloniaShell 重新做成工业上位机主工作台。
> 先骨架，后细节。不要先堆卡片，不要先做假 KPI。

---

## 1. 阶段目标

恢复并现代化旧 WPF 上位机骨架：

- 左侧导航
- 顶部 Header
- 中央业务页
- 右侧设备状态 + 运行日志
- 底部状态栏

最终 Shell 必须看起来像一个完整的圆滑工业工作台，而不是 Dock 控件拼出来的界面。

---

## 2. 固定布局规格

建议布局：

```text
Window Canvas：浅暖灰
└── Big Shell：大白圆角，ClipToBounds，柔和阴影
    ├── Left Icon Rail：72~84px
    └── Main Area
        ├── Header：64~76px
        ├── Workspace
        │   ├── Document Area：中央业务页
        │   └── Right Rail：360~420px
        │       ├── Equipment Status：约 36% 高度
        │       └── Runtime Log：约 64% 高度
        └── Footer：28~36px
```

1366×768 降级要求：

- 左侧 rail 可保持窄宽。
- Header 文案必须截断，不挤爆。
- 右侧宽度可降到 320~360。
- 中央业务页保留滚动。
- 日志仍默认可见。

---

## 3. 允许修改的文件类别

允许修改：

- `src/Edge/IIoT.Edge.AvaloniaShell/Views/MainWindow.axaml`
- `src/Edge/IIoT.Edge.AvaloniaShell/ViewModels/MainWindowViewModel.cs`
- `src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia/Views/HeaderView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia/Views/FooterView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia/Features/SysMenu/Views/SysMenuView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia/ViewModels/**`，仅限展示绑定和文案状态
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/Views/EquipmentView.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/Views/LogView.axaml`
- `src/Shared/IIoT.Edge.UI.Avalonia/Themes/**`
- 对应资源字典
- 对应 UI 测试

---

## 4. 禁止修改的文件类别

禁止修改：

- `src/Application/**`，除非只改资源化接口且单独说明
- `src/Runtime/**`
- `src/Infrastructure/**`
- `src/Modules/**/Runtime/**`
- `src/Modules/**/Integration/**`
- `src/Modules/**/Config/**`
- 插件加载协议
- ViewRegistry 协议
- PLC/MES/Cloud/上传/重试/死信

---

## 5. 必须遵守的 Shell 架构规则

### 5.1 Shell 不 hardcode 模块业务

Shell 可以 hardcode：

- 左侧导航区域
- Header/Footer
- 右侧设备状态/日志区域
- Dock 承载区

Shell 不允许 hardcode：

- 匀浆页面内容
- 匀浆 Runtime 状态
- 模块业务字段
- 模块上传状态专属逻辑

模块页面必须仍通过 ViewRegistry / 插件注册进入中央业务区。

### 5.2 Dock 只能做承载机制

必须隐藏：

- Dock 文档页签
- Dock 工具页签
- Dock chrome
- Dock 默认边框
- Dock 默认背景

禁止：

- 让用户看到 Dock 控件标题栏。
- 让用户现场拖拽破坏主布局。
- 把右侧日志做成需要点击才出现的 Tab。

### 5.3 右侧面板常驻

右侧上半区：设备状态。
右侧下半区：运行日志。

禁止：

- 用单个 ActiveTool 切换设备/日志。
- 默认只显示设备，不显示日志。
- 默认只显示日志，不显示设备。

---

## 6. Header / Footer 要求

### Header

必须包含真实有意义的信息：

- 当前产品标题。
- 运行状态 chip：运行中 / 启动中 / UI-only 开发模式 / 启动失败。
- 当前模式：本地 / 生产 / 开发，按真实配置显示。
- 当前产线/工序：来自 `MachineProfile` 或配置。
- 用户/权限入口。
- 窗口控制。

禁止：

- 没有业务行为的搜索框。
- 假通知角标。
- 假设备数。
- 假产线在线状态。

### Footer

必须显示：

- 系统状态。
- 版本。
- 本地时间。
- 运行目录/Edge Id 等可真实确认的信息。

禁止：

- 假成功状态。
- 写死“系统运行正常”。

---

## 7. UI 细节要求

- Big Shell 背景为白/近白。
- Window canvas 为浅暖灰。
- 左侧 rail 是白色或极浅色，选中状态 lime。
- Header 不使用硬底线，使用留白和浅色层次分隔。
- 中央业务页背景与 shell 融合，不套硬框。
- 右侧面板之间用间距分隔，不用黑线/硬线。
- Footer 轻量，不抢视觉。

---

## 8. 测试要求

必须覆盖：

1. `MainWindowViewModel` 中能找到日志 dockable 和设备 dockable。
2. 日志和设备不再是互斥 ActiveTool 展示。
3. Shell 默认布局中右侧设备和日志均有 Content 绑定。
4. Header 不显示无意义搜索资源键，除非存在真实搜索行为。
5. 资源键中中文/英文成对。
6. 不出现常见乱码片段。

---

## 9. 验收标准

- 旧 WPF 信息骨架被继承。
- Shell 无明显硬框、蓝条、框套框。
- 右侧设备状态和日志默认常驻。
- Dock 控件感消失。
- Header/Footer 信息真实。
- 中央业务区仍由插件/导航注册驱动。
- 不改业务链路。

---

## 10. 验收命令

```powershell
dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

---

## 11. 停止条件

必须停止：

- 需要改插件加载协议才能做布局。
- 需要把模块页面复制到 Shell。
- 需要引入新 Dock 库。
- 无法同时常驻设备和日志。
- 需要伪造状态才能让 Header 好看。
