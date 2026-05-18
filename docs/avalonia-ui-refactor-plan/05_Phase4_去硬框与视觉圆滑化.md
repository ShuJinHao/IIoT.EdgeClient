# Phase 4：去硬框与视觉圆滑化小计划

> 本阶段目标：把界面水准拉到参考图方向。
> 不是照抄内容，而是达到同等级的圆滑、细致、层次、卡片节奏和工业高级感。

---

## 1. 阶段目标

彻底处理当前 UI 的几个核心观感问题：

1. 外层 border / 硬框明显。
2. 框套框。
3. Dock 控件感。
4. 蓝条或旧工具软件感。
5. 页面粗糙、留白不均、阴影和圆角不统一。
6. Launcher / Shell / 模块页视觉不统一。

---

## 2. 视觉目标关键词

必须达到：

- 浅暖灰画布
- 大白圆角 shell
- 左侧 icon rail
- 柔和阴影
- 白色卡片
- lime 点缀
- 状态 chip
- 精致表格
- 真实空态
- 细腻滚动条
- 圆润按钮
- 无硬线分割
- 无 Dock 原生 chrome

---

## 3. 允许修改的文件类别

允许修改：

- `src/Shared/IIoT.Edge.UI.Avalonia/Themes/IndustrialTheme.axaml`
- `src/Shared/IIoT.Edge.UI.Avalonia/Themes/AppTypography.axaml`
- `src/Edge/IIoT.Edge.AvaloniaShell/Views/MainWindow.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Shell.Avalonia/Views/**`
- `src/Presentation/IIoT.Edge.Presentation.Panels.Avalonia/Views/**`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Themes/LauncherTheme.axaml`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Themes/LauncherTokens.axaml`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Views/**`
- 对应资源文件
- 对应 UI 测试

---

## 4. 禁止修改的文件类别

禁止修改：

- Runtime
- Infrastructure
- Application 业务逻辑
- Module Runtime/Integration
- PLC/MES/Cloud/上传/重试/死信
- 业务数据模型
- 业务服务接口，除非单独批准

---

## 5. 设计 token 要求

所有视觉值必须尽量沉淀到主题中。

### 5.1 颜色

建议 token 语义：

- `Canvas`：窗口背景，浅暖灰。
- `Shell`：主 shell 白色/近白。
- `Card`：白卡。
- `CardSoft`：浅卡片背景。
- `TextPrimary`：主文字。
- `TextSecondary`：说明文字。
- `TextMuted`：弱提示。
- `AccentLime`：主点缀。
- `StatusSuccess` / `Warning` / `Error` / `Neutral`。

禁止：

- 页面中散落大量 `#RRGGBB`。
- 蓝色作为主视觉。
- 状态颜色和真实状态不一致。

### 5.2 圆角

建议：

- Shell：28~32
- 主卡片：20~24
- 小卡片：16~20
- Chip：14~18
- Icon button：圆形或 16+

禁止：

- 大量 0 圆角硬框。
- 同一页面圆角混乱。

### 5.3 阴影

要求：

- 使用柔和阴影制造层次。
- 阴影不能过重。
- 不用硬边框替代层次。

### 5.4 字体与层级

建议层级：

- 页面标题：22~28，SemiBold。
- 区块标题：16~18，SemiBold。
- 正文：13~15。
- Caption：11~12。
- KPI 数字：26~34。

禁止：

- 所有文字一个字号。
- 小字过多、灰度过低导致现场不可读。

---

## 6. 必须去掉的视觉问题

### 6.1 硬框

检查并消除：

- 主窗口外层 1px 硬线。
- Header 底部硬线。
- 左右区域硬分隔线。
- 中央页面外层边框。
- 右侧面板嵌套边框。
- 表格外层过重边框。

### 6.2 蓝条

检查并消除：

- 蓝色主按钮泛滥。
- 蓝色卡片标题条。
- 蓝色选中背景。
- 蓝色状态强调。

允许少量蓝色用于信息提示，但不能成为产品主调。

### 6.3 Dock 控件感

必须隐藏：

- Dock tabs
- Dock headers
- Dock splitters 的硬线感
- Dock tool chrome

### 6.4 无意义搜索框

删除所有没有真实搜索业务的搜索框。

特别检查：

- Launcher profile 搜索。
- Shell Header 搜索。
- 页面顶部装饰性搜索。

---

## 7. Launcher 视觉要求

Launcher 必须与 Shell 同源：

- 同样浅暖灰背景。
- 同样白色大圆角 shell。
- 同样 lime 点缀。
- 同样按钮和 chip 风格。
- 同样柔和阴影。

登录页：

- 输入框圆润。
- 错误提示清楚。
- 改密弹窗精致。
- 入口卡不拥挤。

工序页：

- 一工序一卡。
- 卡片大、清楚、现场可读。
- 按钮主次明确。
- 没有 variant chip。

---

## 8. Shell 视觉要求

Shell 必须：

- 像参考图一样整体圆滑。
- 一眼看到左侧导航、顶部状态、中央业务、右侧状态/日志、底部状态。
- 页面之间留白统一。
- 右侧面板卡片节奏一致。
- 日志不糊成一团。
- 设备状态不全是绿色。

---

## 9. 人工验收截图要求

必须截三档：

- 1900×1200
- 1600×1000
- 1366×768

截图必须包含：

- Launcher 登录页。
- Launcher 工序页。
- Shell 主界面。
- Shell 右侧设备 + 日志。
- 匀浆页或一个中央业务页。
- 日志空态或真实日志状态。

人工检查：

- 是否达到参考图水准。
- 是否还有硬框。
- 是否还有蓝条。
- 是否还有框套框。
- 是否有空白图标。
- 是否有无意义搜索框。

---

## 10. 验收标准

本阶段通过条件：

- 主界面视觉明显接近参考图水准。
- Shell/Launcher 同源。
- 无硬框、无蓝条、无框套框。
- 细节统一：卡片、按钮、chip、日志行、表格、空态。
- 不新增 mock 数据。
- 不破坏真实来源。

---

## 11. 验收命令

```powershell
dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

---

## 12. 停止条件

必须停止：

- 需要 fake 数据才能达到视觉效果。
- 需要改业务层才能显示卡片。
- 需要引入未批准 UI 库。
- 需要照抄参考图业务内容。
- 1366×768 无法保证日志可见。
