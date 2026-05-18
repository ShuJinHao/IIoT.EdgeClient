# Avalonia 工业上位机设计系统

## 当前口径

本文件固化 Avalonia 迁移阶段已经形成的浅色工业上位机风格，适用于 Launcher、Shell、Monitor、日志中心、右侧状态区和宿主页。目标是让后续页面复用同一套 token 和 class，而不是在页面内继续自由写颜色、圆角、阴影和状态表达。

## Token 规则

- `Edge.*` 是主客户端 canonical token。新页面优先使用 `Edge.Bg.*`、`Edge.Border.*`、`Edge.Text.*`、`Edge.Accent.*`、`Edge.Status.*` 和 `Edge.Shadow.*`。
- `Ind.*` 是历史兼容 token。短期保留，已经引用的页面不用立即改名；新样式不再新增 `Ind.*`。
- `Launcher.*` 是 Launcher 侧 token，但状态语义必须和 `Edge.*` 对齐。运行、停止、失败、开发、本地或空态表达不得另起一套含义。
- `App.FontFamily.*` 和 `App.FontSize.*` 承担全局字体口径。页面内可微调布局，不应随意使用视口宽度缩放字体。

## 状态语义

- running：真实运行中或连接中，使用 `Edge.Status.Running` / `RunningSoft`。
- stopped：真实停止、离线、未启动或不可用，使用 `Edge.Status.Stopped` / `StoppedSoft`。
- failed：真实错误、失败或 FATAL/ERROR 日志命中，使用 `Edge.Status.Failed` / `FailedSoft`。
- development：开发模式、本地模式或测试入口提示，使用 `Edge.Status.Development` / `DevelopmentSoft`。
- neutral/muted：无数据、未知、只读说明和低强调信息，使用 `Edge.Status.Neutral`、`Muted` 及对应 soft token。

## 组件 Class

- 页面底：`edge-page-panel`，只承载页面留白和浅灰底，不嵌套成大卡片。
- 内容卡：`edge-card`、`edge-summary-card`、`edge-toolbar-card`，用于真实内容分组。
- KPI：`edge-kpi-card`、`kpi-value`，只展示已有 ViewModel 派生的真实指标。
- 状态卡：`edge-status-card` 及 `running/stopped/failed/development` 变体，用于 PLC、MES、Cloud、缓存队列等真实状态。
- 表格：`edge-table-card`、`edge-table-shell` 和 DataGrid 全局样式，负责表头、行高、滚动和裁剪。
- 日志：`edge-log-list`、`edge-log-entry`、`edge-log-level`、`edge-dot`，日志时间和级别必须来自日志源。
- Chip：`edge-chip`、`edge-status-pill`，用于短状态，不承载长说明。
- 空态：`edge-empty-state`、`empty-state-title`、`empty-state-body`，只表达真实无数据或未知，不伪装正常。
- 表单和弹窗：`edge-form-section`、`edge-form-actions`、`edge-dialog-card`，只用于明确的表单或确认操作。

## 页面模板

宿主页默认结构为：浅灰页面底、顶部标题区、紧凑工具栏或状态摘要、白色内容卡、稳定表格容器、真实空态或错误态。右侧面板常驻日志或真实状态卡，不做无来源 Tab、假搜索、假通知角标和假正常提示。

## 真实数据底线

- 不新增业务字段来补截图观感。
- 不 mock 产量、节拍、良率、PLC/MES/Cloud、缓存队列、告警或通知。
- 没有真实来源时显示空态、未知或失败原因，不显示正常。
- 不改变 ItemsSource、Command、权限判断、启动流程、PLC/MES/Cloud、缓存、上传、重试和死信链路。

## 禁止事项

- 非主题 XAML 不新增十六进制颜色。
- 不新增手写 SVG Path 图标。
- 不新增 UI 库、图标库、动画库或截图依赖。
- 不在 page section 外层套多层卡片。
- 不用营销式 hero、装饰光斑或一色系大面积渐变替代工业控制台信息密度。
- 用户可见文案必须成对进入对应 `zh-CN.xaml` / `en-US.xaml`，中文仍为默认语言。
