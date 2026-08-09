# Edge 客户端 UI 第二层规范

本文档约束 `IIoT.EdgeClient` Avalonia 页面级组合方式。它补充 `docs/客户端规则.md` 和 `docs/PLC选择与状态展示控制.md`：共享控件归口 `IIoT.Edge.UI.Shared`，唯一共享控件清单见 [Edge 共享控件“控件清单”](../../IIoT.Edge.Sdk/src/IIoT.Edge.UI.Shared/Avalonia/Controls/README.md#控件清单)，业务页面只声明布局、绑定、资源文案和真实命令连接，不在本文复制第二份清单。

当前状态：第 0～7 批候选代码继续保留；复审问题尚未全部收口，本文档描述下一批源码整改必须达到的 UI 契约，当前候选代码不是生产基线。

## 1. 统一目标

- 统一的是页面组合规则，不是强迫所有页面长一样。
- 同一操作语义在不同页面出现时，名称、图标、Kind、顺序和位置必须一致。
- 主数据表格必须吃满当前页面可用展示区域，或在受约束区域内明确滚动；不得使用固定小高度造成下方大面积空白。
- 禁止为美化添加假按钮、假状态、假数据、假 PLC 在线、假 MES/Cloud 状态或 UI-only 流程。
- `Grid`、`StackPanel`、`TextBlock`、`Run` 等中性布局和文本原语可以用于结构排版、绑定真实文本及应用共享 class/resource；它们不得组合成按钮、卡片、弹窗、状态、输入、导航、表格、通知或其它已有共享控件承载的交互/视觉语义，也不得在页面内写私有颜色、边框、圆角、阴影、字号或状态样式模拟 `Edge*` 控件。

## 2. 页面类型

| 类型 | 页面 | 工具栏规则 |
| --- | --- | --- |
| CRUD 表格页 | 网络设备、串口设备、后续同类设备配置页 | `新增 / 修改 / 删除`，固定放在 `EdgeTablePanel.ActionContent` |
| 模板导入 + 编辑页 | IO 映射 | `重置标准点位 / 修改`，固定放在 `EdgeTablePanel.ActionContent`；不得恢复第二 PLC 下拉 |
| 表单设置页 | 参数页 | `重置默认值 / 保存`，固定放在页头 ActionContent |
| 只读展示 + 紧急编辑页 | 配方页 | 保留概况卡、只读表、行内删除和紧急编辑区；只统一动作命名、图标、Kind 和位置 |
| 实时监控页 | IO 交互 | `刷新 / 读取 / 写入` 按真实能力展示；不得套 CRUD 骨架 |
| 右侧冻结卡片 | 设备运行 | 固定展示设备号、当前工序、主批计划真实空态或真实计划 |

## 2.1 导航层级

- 同一屏内黑胶囊选中态（`Edge.Brush.Segmented.ItemSelected` 深色填充）只允许出现一层，即顶部一级导航（`EdgeSegmentedNav`）。
- 页面内部的次级页签必须使用 `EdgeTabControl Variant="Secondary"`（下划线弱化变体）；`Variant` 缺省为 Primary 胶囊，只允许用在没有上层胶囊导航的独立页面。
- 二级页签选中态用文字加重 + 下划线表达，不允许再引入第二个胶囊底座或第二种私有页签样式。
- 三级及更深层不再新增页签控件；用分组卡片标题或页内锚点表达。
- 该变体只能在 `IIoT.Edge.UI.Shared` 的 `Navigation.axaml` 维护；页面不得私写 TabItem 模板改选中态。

## 3. 动作规范

- `新增`：`Edge.Icon.Add`，`Kind=Primary`。
- `修改`：`Edge.Icon.Write`，`Kind=Secondary`。
- `删除`：`Edge.Icon.Delete`，`Kind=Danger`。
- `保存`：`Edge.Icon.Save`，`Kind=Primary`。
- `刷新`：`Edge.Icon.Refresh`，默认 `Kind=Primary`。
- `读取` / `写入`：只在有真实读写链路和当前状态允许时显示或启用。
- `重置标准点位`：唯一命名，不再并存“套用模板”“播种”等内部词；该操作会清空当前 PLC 已有 IO 映射并按插件标准模板重新生成，必须保留二次确认。
- `重置默认值`：用于参数默认值恢复，不得伪装成普通刷新。
- 页面动作组使用共享 `EdgeActionToolbar`，不得每页私写不同间距、顺序和危险按钮位置；页头、表格 ActionContent 里的动作按钮不允许裸放在 StackPanel/Grid 中。
- 危险动作（删除等）禁用时必须回落到中性弱化外观（灰底/灰字），不允许保留红色实心或红色描边；无可删除对象时按钮呈禁用态而不是隐藏。禁用态样式统一在共享层 `Actions.axaml` 维护。

## 4. 表格规范

- 主表格容器使用 `EdgeTablePanel`，主数据区使用 `Classes="fill"`。
- 主表格内 `EdgeDataGrid` 使用受约束父级高度；需要吃满页面时设置 `ViewportMaxHeight="0"`，不得回到默认小视口造成只显示几行。
- 主表格页根布局必须给表格明确可用高度，例如 `Grid RowDefinitions="Auto,*"` 或等价结构。
- 不得把主表格放入整页 `EdgeScrollHost + StackPanel` 形成无限高度；外层滚动只包非主表格内容或分组列表。
- 表格头部通过 `EdgeTablePanel.HeaderMetaContent` 展示总数，例如“共 N 条”。
- 空态统一使用 `EmptyStateView` 或 `EdgeTablePanel` 的空态属性，文案必须是真实空态，不能暗示设备不存在或伪造运行结果。
- `EmptyStateView` 的共享默认 `Title` / `Message` 必须保持语言中立空值；每个生产 AXAML 使用点都必须显式提供成对的资源或绑定，不能依赖控件内置中文兜底。
- `EdgeTablePanel` 错误态必须把本地化短标题绑定到 `ErrorTitle`，把经过安全处理的真实失败说明绑定到 `ErrorMessage`；不得把长错误正文塞进单行标题并把正文留空。
- 当前 PLC 点位量级不做分页；现场需要一屏扫视和受约束滚动。
- `任务绑定`、Launcher 版本摘要等少量固定行属于紧凑清单：使用内容驱动高度和共享 `MaxHeight`，不得套 `Classes="fill"` 拉伸到整页；行数超过上限后再由表格内部滚动。
- `详情`、`修改` 等行级操作列统一使用共享 `EdgeActionColumn`，不得在普通模板列里手调按钮边距或列宽；操作按钮必须与表头和其他列的行中心对齐。
- 空态只能占用当前表格内容区；不得在已有 `EdgeTablePanel.IsEmpty` 之外再放一个绝对定位或同层 `EmptyStateView`，避免空态、表头和滚动条互相覆盖。
- 表格列优先用 `Auto`、`*`、最小宽度和 Tooltip 保留可读性；备注、错误详情等长文本列不得写死成占据大面积空白的固定宽度，也不得挤压地址、类型、数量和操作列到不可读。

## 4.1 滚动与弹窗

- `EdgeScrollHost`、`EdgeDataGrid` 的滚动条轨道、滑块、命中区、悬停反馈、边距和占位只允许在 `IIoT.Edge.UI.Shared` 维护；页面不得复制 ScrollBar 模板或用私有颜色/宽度补丁。
- 纵向和横向滚动条都必须保持轻量视觉与可操作命中区；滚动条不能覆盖最后一列、操作按钮或最后一行，也不能因为内容不足仍显示一根贯穿卡片的长滑块。
- 弹窗统一使用 `EdgeDialogChrome`；详情弹窗按“身份 / 连接 / 运行时间 / 错误”这类真实信息层级组合共享卡片或字段行，编辑弹窗按业务分组组合 `EdgeFieldRow`，页面不得私造弹窗标题栏、关闭按钮和底部操作区。
- 使用 `EdgeRoundedWindowRegion` 的窗口，其原生 region 半径必须与实际应用模板后的最外层可见 `Border.CornerRadius` 一致；Headless 测试应读取真实模板树并与窗口常量比较，不得只用源码字符串断言冒充窗口行为。
- 半透明遮罩窗口必须显式请求 `TransparencyLevelHint="Transparent"`，同时保留共享 `edge-dialog-overlay-window` 背景；不得用本地 `Background="Transparent"` 覆盖遮罩，也不得在无平台证据时添加 `ExtendClientAreaToDecorationsHint`。
- Launcher 最后兜底错误窗必须复用 `EdgeDialogChrome` 和共享 token；因语言资源初始化也可能失败，生产调用必须传入非空 title/message/closeText 实值。错误正文只允许本地化固定摘要、稳定原因码或异常类型，不得显示原始异常消息、路径、端点、响应正文或凭据片段。

## 4.2 IO 备注

- IO 映射与 IO 交互直接绑定同一个 `IoMappingEntity.Remark`，五类页面口径一致。
- 标准模板只持久化短业务名；模块名只放在分组标题，不重复写进每行备注。
- 页面层禁止裁剪、拼接、翻译或改写备注。旧自动生成长备注只能由 schema reconciliation 按模板声明的精确 legacy alias 修复；无法精确识别的值和人工备注原样保留。

## 5. 设备运行冻结槽位

- 右侧 `设备运行` 卡片是已验收冻结 UI，不得在无数据时删除槽位。
- `当前工序` 必须固定展示，优先来自当前配方/运行快照 `ProcessName`。
- 没有可用工序名时显示明确空态“未配置工序”，不能只显示 `-`、`—` 或空白。
- 不能用“数据”等泛化菜单名替代业务工序名。
- 主批计划无数据时必须显示“暂无主批计划数据”。

## 6. 例外

- 配方页不是 CRUD 表格页，不得为了统一而重写为“新增/修改/删除/保存”骨架。
- IO 交互是实时监控页，不得为了统一而新增配置类 CRUD 按钮。
- 页面动作集合可以不同；只要同一动作出现，命名、图标、Kind、顺序和位置必须一致。

## 7. UI 门禁分层

- C# 源码结构、依赖方向、公开 API 和业务边界由 Roslyn Analyzer 负责；Analyzer 不声明自己能够解析或证明 AXAML 结构。
- AXAML 的资源键、共享控件使用、危险按钮样式、空态成对文案、导航层级和页面组合约束，由独立的 AXAML 解析/构建门禁负责；不得用 C# Analyzer 名义包装文本搜索后宣称覆盖 AXAML。
- AXAML 门禁必须解析实际 XML/编译输入并报告文件和节点位置；纯字符串命中只能作为辅助提示，不能替代结构验证。
- 两类门禁使用独立诊断编号、独立测试和独立验收结论；只有二者分别通过时，才能说明本规范涉及的源码与视图约束均已覆盖。
