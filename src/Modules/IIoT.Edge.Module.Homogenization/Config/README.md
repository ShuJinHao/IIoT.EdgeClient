# 匀浆插件配置说明

本目录只存放匀浆插件自己的业务配置。共享层不能在这里替插件决定 MES 字段、PLC 点位、设备状态文本或样本数据。

## 插件身份、任务 Key 与外部链路

匀浆插件身份由模块入口 `DependencyInjection` 持有，`ModuleId`、`ProcessType` 和配置 Section 继续使用 `Homogenization`，不再为这些字符串单独建立公开常量类。`plugin.json` 是宿主加载程序集前必须读取的 manifest，里面的字符串保留不改，并通过契约测试与模块入口保持一致。

匀浆 PLC 任务 Key 只在运行时工厂和任务类自身使用，字符串值保持 `Homogenization.*` 不变，用于任务绑定表和运行上下文步骤持久化。运行配置只放参数、开关、路径和码表，不承载模块身份或任务持久化 Key。

Cloud 上传相关类型放在 `Integration/Cloud`。当前云端匀浆过站契约尚未单独确认，`HomogenizationCloudUploader` 只负责显式跳过真实上传并避免进入 Cloud retry；Cloud DTO 仅作为契约草案随 Cloud 链路归档，不代表运行时已启用真实上传。后续如 MES 需要显式 DTO，再按 `Integration/Mes` 单独归位，不建立通用 DTO 杂项目录。

## homogenization.module.json

- `Modules:Homogenization:Module:Presentation`：匀浆 UI 刷新和页面缓存配置，`DataViewRefreshIntervalMs` 控制数据页刷新间隔，`MaxOutboundRecords` 控制内存中保留的最近出料记录数量。
- `Modules:Homogenization:Module:Runtime`：匀浆 PLC 任务循环配置，握手、心跳、实时上传任务都会从这里读取循环间隔和最小间隔。
- MES 开关、服务地址、工站号、签名令牌和接口路径统一维护在 `Config/Parameters/HomogenizationParams.cs` 的 MES 参数枚举特性上，由宿主参数系统播种、编辑和读取，不再维护平行 Options 配置。
- `Modules:Homogenization:Codes:Plc`：PLC 触发码和上位机应答码。运行时任务用这些码判断触发、正常应答、异常应答和 MES 拒绝应答。
- `Modules:Homogenization:Codes:Mes:Channels`：诊断面板使用的 MES 业务通道名，不参与接口路径拼接。
- `Modules:Homogenization:Codes:Mes:RealtimeItems`：实时数据上传的 MES 字段码表。
- `Modules:Homogenization:Codes:Mes:RecipeItems`：配方/工艺参数上传的 MES 字段码表。
- `Modules:Homogenization:Codes:Mes:OutboundProduceItems`：出料上传 `produce` 数组的 MES 字段码表。
- `Modules:Homogenization:Codes:Mes:EquipmentStatusTexts`：PLC 设备状态码到 MES 状态文案的映射。

JSON 文件保持标准 JSON 格式，不能直接写注释；字段含义以本文件和 `HomogenizationModuleConfiguration.cs` 的中文 XML 注释为准。

## 开发样本

匀浆开发样本 PLC 由 `Samples/HomogenizationDevelopmentSampleContributor.cs` 内置默认设备生成，不再额外维护设备 JSON。IO 点位唯一真实来源是硬件配置里按 `NetworkDeviceId` 保存的 `IoMappingEntity`，开发播种只会用通用枚举 PLC 信号 profile 作为插件标准点位来源，导入当前 PLC 缺失的映射。

点位 SignalKey、默认地址、长度、方向、分类、分组和显示角色全部维护在 `Config/Hardware/HomogenizationPlcSignals.cs` 的枚举特性上。如果后续接真实设备，应在硬件配置页维护每台 PLC 的实际地址。插件模板只负责初始化，不允许再新增 JSON 点位源，也不允许 IO 交互绕过数据库直接读取模板。
