# PLC 选择与状态展示控制

本文档固化 `IIoT.EdgeClient` 设备选择、PLC 状态、IO 映射和 Dashboard 展示的长期契约。修改右侧设备运行面板、IO 页面、Dashboard PLC 状态表、系统日志、产能查询或相关 ViewModel/Service 前必须先读本文档。

## 1. 统一选择源

- `IDeviceSelectionService` 是全局设备选择的唯一状态源。
- 右侧“设备运行”区域的“设备号”选择是全局选择入口；左侧或页面内部不得另建互不相通的设备选择状态。
- `全部/汇总` 表示展示全部已配置 PLC/设备的聚合状态；选择具体设备号时，IO、PLC 状态表、系统日志、产能查询等页面必须同步到同一设备。
- 页面允许保留本地显示对象或下拉选项集合，但选中值必须与 `IDeviceSelectionService.SelectedDeviceKey` 双向同步。
- 新增设备相关页面时，默认必须接入 `IDeviceSelectionService`；确实不能接入时，必须先说明业务原因并得到当前轮明确批准。

## 2. PLC 和 IO 数据来源

- IO 映射真实来源是硬件配置中按 PLC 保存的 `IoMappingEntity`，按 `NetworkDeviceId` 读取，不能从插件 profile、静态模板或 UI 文案推导运行数据。
- 插件 PLC profile 只能用于导入标准点位和开发播种；运行页面、调试读取、任务绑定和诊断必须读取当前 PLC 已保存映射。
- PLC 点位分类固定为 `信号交互`、`单点读数据`、`连续读数据`。
- 实时扫描只处理 `信号交互`；`单点读数据` 和 `连续读数据` 由业务任务或手动调试读取，但页面必须展示当前 PLC 已保存的对应映射。
- 当所选 PLC 已有 `单点读数据` 或 `连续读数据` 映射时，IO 页面不得显示“当前设备暂无单点读写数据”或“无连续读取数据”之类空态。
- 如果确实没有已保存映射，空态必须说明是“当前 PLC 未配置对应 IO 映射”，不能暗示 PLC 不存在或系统无数据。

## 3. Dashboard PLC 状态表

- Dashboard `PLC 状态表` 必须以已配置 PLC 为基准行，再叠加运行时快照。
- `IPlcConnectionManager.GetRuntimeStatuses()` 只提供运行时覆盖信息，不是 PLC 列表的唯一来源。
- 当配置存在 12 台 PLC，但运行时快照暂为空时，Dashboard 不得出现 `0 / 12` 但表格空白；必须列出已配置 PLC，并显示真实的未连接、未知或未采集状态。
- 禁止为了填满 Dashboard 伪造 PLC 在线、延迟、最后连接时间、最后失败或错误详情。
- PLC 状态、延迟、最后连接和最后失败只能来自真实运行时快照、诊断或本地已保存状态；没有真实值时显示空态或未知。

## 4. 日志、产能和右侧设备运行

- 系统日志筛选必须跟随 `IDeviceSelectionService`；选择具体设备时，日志只展示该设备相关记录。
- 产能查询页必须跟随 `IDeviceSelectionService`；不得自建独立设备筛选导致与右侧设备号不一致。
- 右侧“设备运行”面板只负责选择和展示真实设备运行摘要，不得绕过业务服务伪造 PLC、MES、Cloud 或主批计划状态。
- 主界面、IO 页面和绑定页面必须对同一个设备号给出一致的 PLC/IO 上下文。

## 5. 修改前检查清单

修改上述链路前必须完成：

- 阅读 `docs/客户端规则.md`、本文档、相关 ViewModel、相关 Service、相关测试和近期 git/GitHub 历史。
- 确认是否触碰已验收功能；已验收功能默认冻结，不能顺手重构或改交互。
- 确认数据来源是真实配置、真实本地缓存、真实运行时快照或真实服务返回。
- 补充或更新测试，至少覆盖 `IDeviceSelectionService` 同步、`IoMappingEntity` 映射展示、Dashboard `PLC 状态表` 配置基准行。
- UI 改动必须真实运行或截图验收；build 不等于 UI 通过。

## 6. 禁止事项

- 禁止页面自建独立设备选择并绕开 `IDeviceSelectionService`。
- 禁止把 Dashboard PLC 表只绑定到运行时快照而忽略已配置 PLC。
- 禁止把 `单点读数据`、`连续读数据` 映射从 IO 页面隐藏掉。
- 禁止用插件 profile 替代当前 PLC 已保存 `IoMappingEntity`。
- 禁止伪造 PLC 在线、延迟、状态、错误、MES 状态、Cloud 状态或日志。
