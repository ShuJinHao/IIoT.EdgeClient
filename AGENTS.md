# IIoT.EdgeClient Instructions

修改 `IIoT.EdgeClient` 前先读：

- 工作区总规则：`../docs/总规则.md`
- 客户端详细规则：`docs/客户端规则.md`
- 历史核心记录：`../docs/历史核心记录.md`

## Historical Reviews

历史复盘不是默认必读材料。

出现以下情况时，必须按模块名、Rule ID、错误码或关键类型检索相关复盘：

- 修复历史回归；
- 修改已冻结业务链路；
- 当前实现与专题契约冲突；
- 测试失败原因无法从源码和契约确定；
- 同类问题曾经发生；
- 用户明确要求追溯历史决策。

检索词可按具体故障补充故障症状。读取历史复盘不能替代读取当前正式规则和专题契约，代码改动完成前仍必须新增本批复盘记录。

上述条件检索仅适用于项目滚动复盘 `docs/改动复盘与规则沉淀.md`。`../docs/历史核心记录.md` 仍保留在本文件的必读入口；是否调整其入口地位必须另行审计。

## Red Lines

- 桌面客户端启动不得被业务配置、设备、PLC、MES、Cloud、IO 映射、模块绑定或诊断问题阻断。
- Cloud 上传与 MES 上传必须分离。
- 设备上传身份链必须是 `ClientCode -> bootstrap -> DeviceId`。
- 新工序必须通过模块扩展，不得写回宿主硬编码。
- Edge UI 只接真实路由、真实数据、真实命令、真实权限；禁止假按钮、假状态、假数据。
- 可复用 UI 控件和视觉必须归口 `IIoT.Edge.UI.Shared`。

## Validation

- 业务、Runtime、PLC、MES、Cloud、缓存改动：跑对应 build/test，并验证启动路径或等价集成测试。
- UI 改动：build 通过后必须真实运行 Shell/Launcher 或截图/UIAutomation 验收。
