# IIoT.EdgeClient Instructions

修改 `IIoT.EdgeClient` 前先读：

- 工作区总规则：`../docs/总规则.md`
- 客户端详细规则：`docs/客户端规则.md`
- 当前改动对应的架构边界契约、专题红线、源码与测试。

项目滚动复盘、`../docs/历史核心记录.md`、旧计划和历史证据不是默认全文必读材料。只有修复历史回归、修改已冻结链路、当前实现与契约冲突、失败原因无法从源码和当前契约确定、同类问题曾发生，或用户明确要求追溯历史决策时，才按模块名、Rule ID、错误码或关键类型检索并读取命中邻域。长计划按当前 Batch/Phase 分段读取，首次完整读取强制规则后只补读新 diff 和当前批次章节。

## Red Lines

- 桌面客户端启动不得被业务配置、设备、PLC、MES、Cloud、IO 映射、模块绑定或诊断问题阻断。
- Cloud 上传与 MES 上传必须分离。
- 设备上传身份链必须是 `ClientCode -> bootstrap -> DeviceId`。
- 新工序必须通过模块扩展，不得写回宿主硬编码。
- Edge UI 只接真实路由、真实数据、真实命令、真实权限；禁止假按钮、假状态、假数据。
- 可复用 UI 控件和视觉必须归口 `IIoT.Edge.UI.Shared`。
- Host 只经正式 `PackageReference` 消费同级独立 `IIoT.Edge.Sdk` 仓产出的 Contracts/Module.Sdk/UI.Shared/Analyzer 包；具体工序源码、资源、配置和 pack 实现只归插件仓，三仓不得跨仓引用源码或项目。

## Validation

- Edge 架构边界由 Analyzer、ArchitectureTests、ConformanceTests 和项目图严格阻断；业务 Unit/Aggregate/Application/Workflow/Contract/Persistence/UI 测试可随功能同批增删改移。inventory/count/quality baseline 只防止静默丢失、漏跑、Skip 和未说明的质量回退，不得恢复 transition、waiver、receipt 或其他测试变更授权机制。
- 业务、Runtime、PLC、MES、Cloud、缓存改动：跑对应 build/test，并验证启动路径或等价集成测试。
- UI 改动：build 通过后必须真实运行 Shell/Launcher 或截图/UIAutomation 验收。
