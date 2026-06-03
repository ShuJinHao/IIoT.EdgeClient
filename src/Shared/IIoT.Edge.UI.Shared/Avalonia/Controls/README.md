# Edge 共享控件

Phase 8.32 起，`IIoT.Edge.UI.Shared` 以少量基础控件 + 属性、变体、共享 class 调度为基线。公开 namespace 继续保持为 `IIoT.Edge.UI.Shared.Avalonia.Controls`；目录只用于归口和查找，不改变业务 XAML 引用方式。

当前公开 `Edge*` class：38 个，其中可实例化控件 37 个，`EdgeStatusControlBase` 是状态行为抽象基类。废弃控件：0 个。

公开 class 和 enum 名单是共享 UI 基座契约的一部分。新增、删除、重命名公开 `Edge*` class 或公开 enum，必须先确认不能由现有基础控件 + 属性/变体/shared class 表达，并同步更新基线脚本；不能只靠“数量不变”绕过治理。

## 控件清单

- `Actions`：`EdgeActionButton`、`EdgeWindowButton`
- `Surfaces`：`EdgeCard`、`EdgeDialogChrome`、`EdgeNoticeBar`、`EdgeSectionHeader`
- `Inputs`：`EdgeTextBox`、`EdgeCheckBox`、`EdgeFieldRow`、`EdgeFilterComboBox`、`EdgeFilterDatePicker`
- `Navigation`：`EdgeSegmentedNav`、`EdgeSegmentedNavItem`、`EdgeTabControl`
- `Data`：`EdgeDataGrid`、`EdgeTablePanel`、`EdgeTextColumn`、`EdgeTemplateColumn`、`EdgeActionColumn`、`EdgeCheckColumn`、`EdgeScrollHost`、`EdgeListBox`、`EdgeLogList`、`EdgeLogListItem`
- `Status`：`EdgeStatusControlBase`、`EdgeStatusDot`、`EdgeStatusChip`、`EdgeStatusListItem`、`EdgeStatusSegment`、`EdgeStatusSegmentBar`、`EdgeVisualStatus`、`EdgeVisualVariant`
- `Metrics`：`EdgeMetricCard`、`EdgeSummaryItem`
- `Charts`：`EdgeBarLineChart`
- `Shell`：`EdgeAccountChip`、`EdgeHeaderBrand`、`EdgeHeaderDivider`

## 新增规则

- 纯皮肤差异只允许走属性、枚举或 shared class，例如颜色层级、密度、图标位置、是否显示状态点；不得新增 `EdgeSaveButton`、`EdgeOnlineBadge`、`EdgeRuntimeCard` 这类场景控件。
- 多个控件共享同一类行为时必须抽基类或共享协作者，例如状态到 class 的映射统一由 `EdgeStatusControlBase` 维护；不得在每个控件里复制 `StatusClasses` 和 `UpdateStatusClass()`。
- 新增按钮只使用 `EdgeActionButton` 的 `Kind`、`Role`、`Size`、`Icon`、`Content`、`Command` 和共享场景 class 表达。
- `EdgeActionButton.Kind` 只表达操作语义，当前只允许 `Primary`、`Secondary`、`Ghost`、`Danger`。
- `EdgeActionButton.Role` 表达按钮使用场景，当前只允许 `Standard`、`Cell`、`Nav`、`Language`、`RightRail`。
- `Size="Icon"` 表达纯图标按钮；不得再新增 `IconOnly` 这类 Kind。
- `plan-action`、`log-clear`、`shell-login-submit`、`shell-login-cancel`、`shell-account-action` 是已归口共享场景 class 白名单；新增场景 class 前必须先证明不能由现有 `Kind`、`Role` 和 `Size` 表达。
- `EdgeWindowButton` 只用于窗口动作按钮，保留 `Minimize`、`MaximizeRestore`、`Close` 这类窗口行为语义，不与普通业务按钮合并。
- 新增卡片只使用 `EdgeCard`、`EdgeMetricCard` 或共享 class 表达，不新增场景卡片 C# 控件。
- `EdgeCard` 是唯一通用 surface/card 容器；`Surface` 表达卡片底色层级，`Elevation` 表达阴影层级，`PaddingMode` 表达密度，场景差异只能通过共享 class 收敛。
- `EdgeMetricCard` 只用于标题、值、单位、说明、状态和图标组成的指标块；普通摘要、Hero、Profile、InfoTile、DataPanel、StatusSummary 不能重新新增 C# 卡片控件。
- 页面不得直接写卡片颜色、圆角、阴影和边框视觉；需要特殊层级时先补 `EdgeCard` 共享 class 或主题令牌。
- 新增弹窗内容壳优先使用 `EdgeDialogChrome`；标题、副标题、关闭按钮、内容区和 footer 的视觉由 Shared 控制，业务 Window 只保留真实事件、绑定和资源 key。
- 页面内弹窗也使用 `EdgeDialogChrome.inline-dialog`，遮罩使用 `edge-dialog-overlay`，关闭按钮通过 `CloseCommand` 绑定真实取消命令，并设置 `CloseTopLevelOnClose="False"`；不得用 `Border + EdgeCard` 私拼页面弹窗壳。
- 确认类弹窗必须使用 `EdgeDialogChrome` 和 `FooterContent` 承接按钮区；不得在业务 Window 里用 `Border + Grid + TextBlock + StackPanel` 私拼弹窗壳。
- Shell 崩溃/错误类弹窗也必须使用 `EdgeDialogChrome`，错误说明使用 `EdgeNoticeBar Status="Error"` 和 `Edge.Icon.Warning`；不得在业务 Window 里用 `EdgeCard + Border + TextBlock` 私拼错误弹窗视觉。
- 新增空态优先使用 `IIoT.Edge.UI.Shared.Avalonia.Views.EmptyStateView` 或共享空态 class；不得为了空态再新增场景 C# 容器或写假数据。
- 通用文本角色不新增 C# 控件，使用 Shared `TextBlock` class 承接，例如 `edge-text-list-item`、`edge-text-dialog-title`、`edge-text-form-label`、`edge-text-form-section-title`；页面不得为表单标签、弹窗标题、列表项文本重复写 `Foreground`、`FontWeight`、`FontSize`、`Padding` 这类视觉属性。
- 新增文本输入只使用 `EdgeTextBox`；登录、弹窗、搜索、表格内联等差异通过共享 class 表达，不再新增 `EdgeLoginInput` 这类场景输入控件。
- 新增布尔输入只使用 `EdgeCheckBox`；参数行布尔值通过 `EdgeFieldRow + EdgeCheckBox` 表达，不显示页面私有 `true/false` 文案。
- 新增筛选选择只使用 `EdgeFilterComboBox`、`EdgeFilterDatePicker`；业务页面不得直接使用原生 `ComboBox`、`CalendarDatePicker` 或私有弹层样式。
- `EdgeFilterDatePicker` 是日期筛选唯一入口，输入框、日历按钮和弹出日历视觉全部由 `Styles/Controls/Inputs.axaml` 统一接管；页面不得为日期弹层新增局部 `Style`、hex 色、私有图标或原生 `DatePicker`。
- 新增表单行只使用 `EdgeFieldRow` 承接 label、说明、输入控件和只读/布尔场景；说明裁切、tooltip、对齐由共享模板负责。
- 新增参数分组不再使用独立 C# 控件，统一用 `EdgeCard.parameter-panel + EdgeFieldRow`；不得重新新增 `EdgeParameterPanel` 或同类场景容器。
- 新增分段选择使用 `EdgeSegmentedNav`；它用于二级导航、模式切换和登录方式选择，不得用 `Border + StackPanel` 拼分段按钮。
- 新增内容页签使用 `EdgeTabControl`；`TabItem` 只允许作为 `EdgeTabControl` 的子项存在，页签视觉由共享样式控制，不新增 `EdgeTabItem` 包壳控件。
- 新增表格外壳只使用 `EdgeTablePanel`，标题、说明、筛选、动作、状态、内容、页脚和空态都通过它的属性或共享 class 表达。
- 新增数据表只使用 `EdgeDataGrid` 和共享列控件；`Density="Compact"` 用于高密运行表格，`Normal` 用于常规表格，`Diagnostic` 用于诊断/日志类表格。
- 新增表格列只使用 `EdgeTextColumn`、`EdgeTemplateColumn`、`EdgeActionColumn`、`EdgeCheckColumn`；页面不得直接使用原生 `DataGridColumn` 拼第二套表格视觉。
- 页面滚动只使用 `EdgeScrollHost`；`EdgeDataGrid` 内部滚动由控件模板负责，页面不得新增原生 `ScrollViewer` 或私有滚动条模板。
- 新增运行日志列表只使用 `EdgeLogList`、`EdgeLogListItem`；日志等级、时间、消息裁切和清空动作都归口共享模板。
- 业务页面不得使用原生 `Button`、`DataGrid`、`ScrollViewer`、`ListBox`、`TextBox`、`ComboBox`、`CalendarDatePicker`、`DatePicker`、`CheckBox`、`TabControl` 作为可见控件入口；需要这些能力时先确认是否已有 `Edge*` 控件承接。
- 新增状态展示使用 `EdgeStatusDot`、`EdgeStatusChip`、`EdgeStatusListItem`。
- `EdgeVisualStatus` 是状态语义入口，状态色只能表达 `Default / Running / Idle / Stopped / Offline / Info / Cache / Warning / Error`；页面不得用 `Ellipse`、`Rectangle`、hex 色或局部 brush 自己拼状态点。
- `EdgeStatusDot` 用于单点状态，`EdgeStatusChip` 用于短标签和胶囊状态，`ShowDot="True"` 时显示状态点，`EdgeStatusListItem` 用于状态摘要行；不得新增 `EdgeOnlineBadge`、`EdgeErrorTag` 这类场景状态控件。
- 连续状态概览使用 `EdgeStatusSegmentBar` 和 `EdgeStatusSegment`；片段颜色仍由 `EdgeVisualStatus` 驱动，页面不得用多个 `Border` 私拼绿/红/灰状态条。
- 新增提示/告警条使用 `EdgeNoticeBar`，其颜色、图标槽、内容槽、动作槽都由 Shared 控制；页面不得用 `Border + TextBlock` 私拼通知条。
- `EdgeTablePanel.StatusContent` 里的状态提示必须使用 `EdgeNoticeBar`，消息文字使用 `edge-notice-message` 或 `edge-notice-message prominent`；不得在 status slot 里用 `Border + TextBlock` 写局部背景、圆角、字号或颜色。
- 新增指标摘要使用 `EdgeMetricCard`；标题、值、单位、说明、图标和状态由它统一承接，不新增 `EdgeKpiCard`、`EdgeMetricStrip`、`EdgeSummaryCard` 这类场景指标控件。
- `EdgeSummaryItem` 是共享摘要键值数据项，不是可见控件；只有当共享摘要卡片需要统一键值输入时使用，不能把它当作页面业务 DTO 强制扩散。
- 新增图表只使用 `EdgeBarLineChart`、`EdgeChartSeries`、`EdgeChartPoint`；页面不得用 `Canvas`、`Line`、`Polyline`、`Polygon`、`Path` 私画图表。
- `EdgeBarLineChart` 保持立即模式绘制，适合周期刷新和非高频动画；不得为每个图表场景新增可视化树型 C# 控件。
- Header 账号入口使用 `EdgeAccountChip`，产品名使用 `EdgeHeaderBrand`，Header 分隔线使用 `EdgeHeaderDivider`；页面不得用普通 Button、Border 或 PathIcon 重新拼 Header 账号控件。
- 页面图标只允许引用 `Edge.Icon.*` 共享资源或 `Edge.Converter.*` 共享 converter 投影结果；不得在业务页面声明私有 `StreamGeometry` 或把 path data 直接写进 `PathIcon`。
- 共享图标资源唯一入口是 `Avalonia/Resources/EdgeIcons.axaml`，所有可复用图标必须声明为 `Edge.Icon.*`；业务页面、Launcher、Shell、模块页面不得新增本地图标字典或私有几何。
- `EdgeControls.axaml` 也不得直接写 `Data="M..."` path data；共享控件模板里的图标同样必须引用 `Edge.Icon.*`。
- 字体资产只允许放在 `IIoT.Edge.UI.Shared/Assets/fonts`，当前唯一共享图标字体文件是 `iconfont.ttf`；不得在 Launcher、Shell、Presentation 或 Modules 下新增 `.ttf/.otf/.woff/.woff2`。
- 颜色、阴影、字体、间距、圆角令牌只在 `EdgeTheme.axaml` 定义；`EdgeControls.axaml` 和业务页面必须引用 `Edge.*` 资源，不能新增 hex 色或 `Ind.*` 令牌。
- 业务页面不得写裸数字字号，例如 `FontSize="24"`；确需页面文本字号时只能引用 `Edge.FontSize.*`，更常见的标题、按钮、表格、弹窗字号应由共享控件 class 承接。
- Converter 只能做 UI 投影，资源统一放在 `Avalonia/Resources/EdgeConverters.axaml`，业务页面不得新增私有 converter。
- 业务页面不得新增局部 `Style`、hex 色、私有 `PathIcon` 资源、私有滚动条或场景 C# 控件来实现可见 UI。

## 样式入口

`Avalonia/Styles/EdgeControls.axaml` 只保留总入口 `StyleInclude`，不得直接写真实 `Style Selector`。共享控件样式按 `Styles/Controls/Actions.axaml`、`Surfaces.axaml`、`Inputs.axaml`、`Navigation.axaml`、`Data.axaml`、`Status.axaml`、`Metrics.axaml`、`Charts.axaml`、`Shell.axaml` 分层维护；新增 selector 必须进入对应分层文件，禁止保留空壳 include。
