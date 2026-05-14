# Avalonia 12 切换前差异矩阵

## 结论

当前 Avalonia 客户端已经具备本地发布、UI-only 启动、显式 `--start-runtime` 联调入口、只读诊断、I/O 运行时缓冲写入申请和现场证据包采集能力。它还不能直接替换生产 WPF 客户端，生产切换必须先完成发布包验收脚本和现场证据包验收。

## 功能差异

| 功能域 | WPF 当前能力 | Avalonia 当前状态 | 切换结论 |
| --- | --- | --- | --- |
| Launcher | 生产入口，默认启动 WPF Shell | 已有 Avalonia Launcher，含 UI-only 和 `--start-runtime` 两个 profile | 可现场试运行，生产默认仍保留 WPF |
| Shell 主壳 | 生产入口，承载 Dock、菜单、登录、状态栏 | Avalonia Shell 已具备 Dock、菜单、语言、Header/Footer、登录和运行状态 | 可进入候选包验收 |
| Header/Footer | 展示登录、运行、Cloud/MES 状态 | 已展示运行链路状态和只读同步状态 | 可试运行 |
| 菜单/导航 | WPF 页面注册主线 | Avalonia registry 已注册核心页面和匀浆插件页 | 可试运行，需以差异矩阵继续核对菜单缺口 |
| Dock 布局 | AvalonDock 生产布局 | Dock.Fluent 布局可用 | 可试运行，现场需验证拖拽、多屏、高 DPI |
| 登录 | WPF 登录链路 | Avalonia 登录已接入现有 Auth 服务 | 可试运行，账号资料以现场配置为准 |
| Monitor | 真实服务驱动 | Avalonia 已接入 `IMonitorViewService` | 可试运行 |
| DataView | 查询、汇总、导出 | Avalonia 已接入真实服务，导出 UTF-8 CSV | 可试运行，Excel 模板化导出延期 |
| Capacity | 设备、今日/历史产能 | Avalonia 已接入真实服务，支持 CSV 导出 | 可试运行 |
| PlcTaskBinding | 任务绑定与权限控制 | Avalonia 已接入真实服务和禁用确认 | 可试运行，现场需复核心跳类任务禁用提示 |
| Diagnostics | 生产诊断和部分运维入口 | Avalonia 只读展示注册、持久化、I/O 写入申请、PLC 写入轨迹、现场摘要 | 可试运行，不开放清理、重试、删除 |
| HardwareConfig | WPF 配置编辑入口 | Avalonia 已迁移配置加载、保存确认和权限控制 | 可试运行，真实保存需现场授权流程复核 |
| IOView | WPF 现场 I/O 查看和交互 | Avalonia 只读快照、运行时缓冲写入申请、PLC 写入轨迹证据链 | 可试运行，不直接 PLC 单点写入 |
| Recipe | WPF 配方页面 | Avalonia 已迁移标准页面 | 可试运行，Cloud 同步仍沿用现有服务，不在 UI 内新增补偿操作 |
| Param | WPF 参数页面 | Avalonia 已迁移标准页面 | 可试运行 |
| 匀浆插件 UI | WPF 插件页 | 已拆为 Core + WPF 壳 + Avalonia 插件壳，Avalonia 按插件方式注册 | 可试运行，只迁匀浆数据页，不改业务运行链路 |

## 延期项

| 项目 | 原因 | 当前处理 |
| --- | --- | --- |
| Cloud/MES 人工清理、重试、删除 | 涉及补偿链路和现场运维权限，不属于 UI 切换前置条件 | Avalonia 只读展示状态，不提供操作按钮 |
| WPF 主线清理 | 需要现场试运行结论后才能移除回退入口 | WPF Shell 和 WPF Launcher 保持可构建 |
| Excel 模板化导出 | 当前 Avalonia 已可导出 CSV，模板化属于体验增强 | 延后单独规划 |
| 现场 PLC 物理写入成功率调参 | 需要现场 PLC 侧状态配合确认 | 通过证据包记录 UI 申请、缓冲接收和块写入轨迹 |
| SukiUI 视觉线 | 当前主线已收敛为 Avalonia 12 + Fluent + Dock.Fluent | 不进入候选包 |

