# 匀浆插件配置说明

本目录只存放匀浆插件自己的业务配置和开发样本配置。共享层不能在这里替插件决定 MES 字段、PLC 点位、设备状态文本或样本数据。

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

## homogenization.io.seed.json

该文件是匀浆开发样本点位，不是运行时唯一配置源。`HomogenizationDevelopmentSampleContributor` 在样本开关启用时读取它，向本地设备和 IO 映射表写入开发用 PLC 设备、读写地址、分类、分组和显示角色。

- `devices[].deviceName/deviceModel/ipAddress/port1`：开发样本 PLC 连接信息。
- `devices[].mappings[].label`：运行时代码通过 label 读取 PLC 缓冲区，不能随意改名。
- `plcAddress/addressCount/dataType/direction`：PLC 地址、长度、数据类型和读写方向。
- `category/groupName/displayRole/remark`：硬件配置页和诊断页展示用的中文分类信息，属于插件业务文案。

如果后续接真实设备，应优先通过 UI 或正式配置导入覆盖开发样本，而不是把现场差异写进共享层。
