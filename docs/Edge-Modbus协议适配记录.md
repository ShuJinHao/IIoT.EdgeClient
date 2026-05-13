# Edge Modbus 协议适配记录 - 2026-05-12

## 范围

- 只修改 `IIoT.EdgeClient`。
- 只新增 Modbus TCP/RTU，不混入 EtherNet/IP、OPC UA、Cloud、AICopilot 或 Launcher DI。

## 完成内容

- 使用 `NModbus` / `NModbus.Serial` 接入 Modbus TCP/RTU，不手写协议栈。
- 将 `IPlcService.Init(ip, port)` 改为 `IPlcService.Init(PlcEndpoint endpoint)`，新增 `TcpPlcEndpoint` 和 `SerialPlcEndpoint`。
- 新增 `IModbusAddressParser` 和 `ModbusAddressParser`，通过 DI 注册，避免新增静态协议服务。
- 新增 `ModbusPlcService`，由 `PlcServiceFactory` 按 `PlcType.ModbusTcp` / `PlcType.ModbusRtu` 创建。
- 新增 `IPlcEndpointResolver`，Modbus RTU 通过网络 PLC 配置的 `Command1` 绑定串口设备名称，串口参数来自已配置的串口设备，`Port1` 用作 1 到 247 的 Modbus 从站 ID。
- 保持 `PlcIoScanTaskBase`、`IPlcSignalBlockPlanner`、buffer 读写链路不变，不引入逐点读取。

## 验证记录

- `dotnet build IIoT.EdgeClient/src/Infrastructure/IIoT.Edge.Infrastructure.DeviceComm/IIoT.Edge.Infrastructure.DeviceComm.csproj --no-restore`：通过，0 警告，0 错误。
- `dotnet test IIoT.EdgeClient/src/Tests/IIoT.Edge.NonUiRegressionTests/IIoT.Edge.NonUiRegressionTests.csproj --no-restore`：通过，363 个测试。
- `dotnet build IIoT.EdgeClient/src/Edge/IIoT.Edge.Shell/IIoT.Edge.Shell.csproj --no-restore`：通过，0 警告，0 错误。
- `dotnet build IIoT.EdgeClient/src/Edge/IIoT.Edge.TestSimulator/IIoT.Edge.TestSimulator.csproj --no-restore`：未执行，当前仓库不存在该项目文件。
- `dotnet list IIoT.EdgeClient/src/Infrastructure/IIoT.Edge.Infrastructure.DeviceComm/IIoT.Edge.Infrastructure.DeviceComm.csproj package --vulnerable --include-transitive`：未发现易受攻击的包。

## 现场配置说明

- Modbus TCP：在网络 PLC 设备中选择 `ModbusTcp`，使用原 IP/端口字段。
- Modbus RTU：在串口设备页维护 COM 口、波特率、数据位、停止位、校验位；在网络 PLC 设备中选择 `ModbusRtu`，`Command1` 填写串口设备名称，`Port1` 填写从站 ID。
- IO 映射仍以网络 PLC 的 `IoMappingEntity` 为真实来源，运行时按块规划读取和写入。
