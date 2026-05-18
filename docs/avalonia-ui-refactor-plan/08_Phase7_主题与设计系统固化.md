# Phase 7：主题与设计系统固化小计划

> 本阶段目标：把 UI 规则固化，避免后续每个页面重新自由发挥。

---

## 1. 阶段目标

沉淀统一的 Avalonia 工业上位机设计系统：

- 设计 token
- 统一控件样式
- 统一页面结构
- 统一状态语义
- 统一日志行
- 统一表格
- 统一空态
- 统一弹窗
- 统一图标规则
- 统一资源化规则
- 自动化资源卫生测试

---

## 2. 允许修改的文件类别

允许修改：

- `src/Shared/IIoT.Edge.UI.Avalonia/Themes/IndustrialTheme.axaml`
- `src/Shared/IIoT.Edge.UI.Avalonia/Themes/AppTypography.axaml`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Themes/LauncherTheme.axaml`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Themes/LauncherTokens.axaml`
- 所有 Avalonia Presentation 资源字典
- UI 资源卫生测试
- 文档和前端规则 skill

---

## 3. 禁止修改的文件类别

禁止修改：

- Runtime
- Infrastructure
- Application 业务逻辑
- Domain
- Module Runtime/Integration
- PLC/MES/Cloud/上传/重试/死信
- 业务数据模型

---

## 4. 设计系统组件清单

必须固化以下样式：

### 4.1 Shell

- `edge-shell`
- `edge-nav-rail`
- `edge-header`
- `edge-footer`
- `edge-workspace-host`
- `edge-document-host`
- `edge-right-rail`

### 4.2 Card

- 普通卡片
- KPI 卡片
- 状态卡片
- 表格卡片
- 表单分组卡片
- 右侧面板卡片
- 空态卡片

### 4.3 状态

- success
- warning
- error
- neutral
- muted
- development
- running
- stopped
- failed

### 4.4 日志

- log metric
- log row
- log level chip
- log empty state
- log file selector

### 4.5 表格

- 表头
- 行高
- hover
- selected
- 空态
- 横向滚动
- 弱边框

### 4.6 表单

- 输入框
- 下拉框
- 错误提示
- 只读状态
- 禁用状态
- 保存/取消按钮

### 4.7 弹窗

- 登录弹窗
- 确认弹窗
- 危险操作弹窗
- 启动失败弹窗

---

## 5. 资源化规则

必须：

- 默认中文。
- 英文成对。
- 新增用户可见文案必须进资源字典。
- 不允许 View 中直接写大量中文。
- 不允许乱码。
- 不允许 key 缺失时直接显示 key 给用户。

必须测试：

- zh-CN 与 en-US key 成对。
- 常见乱码片段无命中。
- 关键入口文案能从资源服务读取。

---

## 6. 图标规则

必须：

- 每个图标都有真实 `Kind`。
- 图标库引用可用。
- 没有空白占位。
- 图标颜色跟状态语义一致。
- 低频入口不要抢主视觉。

禁止：

- 用文字 `--` 假装图标。
- 图标 Kind 不存在。
- 硬编码奇怪 emoji 当主图标。

---

## 7. 代码卫生规则

必须检查：

- 页面 XAML 是否散落十六进制颜色。
- 是否使用统一 class。
- 是否有重复 token。
- 是否还有旧 WPF 样式名。
- 是否还有 Dock 默认样式残留。
- 是否还有无意义搜索框。
- 是否有 fake 文案。

---

## 8. 文档输出

必须输出：

```text
docs/Avalonia-Industrial-Design-System.md
docs/Avalonia-UI-验收清单.md
```

文档包含：

- token 命名规则
- 常用样式示例
- 页面结构模板
- 日志/设备/表格/空态规则
- 禁止事项
- 三档分辨率验收表

---

## 9. 验收标准

本阶段通过条件：

- 主题 token 清楚。
- 常用组件样式固化。
- Launcher/Shell/页面风格统一。
- 资源测试通过。
- 没有乱码。
- 没有图标空白。
- 后续页面有明确规则可遵循。

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

- 设计系统需要改业务层。
- 需要新增未经批准 UI 库。
- 资源化需要删除大量现有资源且无法确认影响。
- 图标库不可用且需要换库。
