# 匀浆插件配置说明

本目录只存放匀浆插件自己的业务配置。共享层不能在这里替插件决定 MES 字段、PLC 点位、设备状态文本或样本数据。

## homogenization.module.json

- `Modules:Homogenization:Module:Presentation`：匀浆 UI 刷新和页面缓存配置，`DataViewRefreshIntervalMs` 控制数据页刷新间隔，`MaxOutboundRecords` 控制内存中保留的最近出料记录数量。
- `Modules:Homogenization:Module:Runtime`：匀浆 PLC 任务循环配置，握手、心跳、实时上传任务都会从这里读取循环间隔和最小间隔。
- `Modules:Homogenization:Mes:SignToken`：匀浆 MES 签名令牌，`HomogenizationMesChannel` 用它和 `ClientCode/timestamp` 生成 `sign`。
- `Modules:Homogenization:Mes:Paths`：匀浆使用的 5 个 MES 接口路径，分别对应进站、出料、配方、实时数据、设备状态。
- `Modules:Homogenization:Codes:Plc`：PLC 触发码和上位机应答码。运行时任务用这些码判断触发、正常应答、异常应答和 MES 拒绝应答。
- `Modules:Homogenization:Codes:Mes:Channels`：诊断面板使用的 MES 业务通道名，不参与接口路径拼接。
- `Modules:Homogenization:Codes:Mes:RealtimeItems`：实时数据上传的 MES 字段码表。
- `Modules:Homogenization:Codes:Mes:RecipeItems`：配方/工艺参数上传的 MES 字段码表。
- `Modules:Homogenization:Codes:Mes:OutboundProduceItems`：出料上传 `produce` 数组的 MES 字段码表。
- `Modules:Homogenization:Codes:Mes:EquipmentStatusTexts`：PLC 设备状态码到 MES 状态文案的映射。

JSON 文件保持标准 JSON 格式，不能直接写注释；字段含义以本文件和 `HomogenizationModuleConfiguration.cs` 的中文 XML 注释为准。

## 开发样本

匀浆开发样本 PLC 由 `Samples/HomogenizationDevelopmentSampleContributor.cs` 内置默认设备生成，不再额外维护设备 JSON。IO 点位唯一真实来源是硬件配置里按 `NetworkDeviceId` 保存的 `IoMappingEntity`，开发播种只会用 `HomogenizationPlcSignalProfile.Signals` 作为默认模板补齐当前 PLC 缺失的映射。

点位 label、默认地址、长度、方向、分类、分组和显示角色全部维护在 `Config/Hardware/HomogenizationPlcSignalProfile.cs`。如果后续接真实设备，应在硬件配置页维护每台 PLC 的实际地址。插件模板只负责初始化，不允许再新增 JSON 点位源，也不允许 IO 交互绕过数据库直接读取模板。
