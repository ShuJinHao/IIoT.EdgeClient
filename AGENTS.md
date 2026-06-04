# IIoT.EdgeClient Instructions

修改 `IIoT.EdgeClient` 前先读：

- 工作区总规则：`../docs/总规则.md`
- 客户端详细规则：`docs/客户端规则.md`
- 历史核心记录：`../docs/历史核心记录.md`

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
