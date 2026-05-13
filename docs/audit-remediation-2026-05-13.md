# 审计整改记录

日期：2026-05-13

适用范围：`IIoT.EdgeClient`。本记录只覆盖客户端本轮审计整改，不包含 `IIoT.CloudPlatform` 或 `AICopilot`。

## 完成内容

- 从 `DeviceService` 提取 `IDeviceSessionCacheCoordinator` / `DeviceSessionCacheCoordinator`，集中处理设备会话缓存读写异常与日志。
- `DeviceService` 改为通过构造函数注入缓存协调器，不再直接承担文件缓存异常处理。
- `DeviceUploadGatePolicy` 增加 bootstrap 失败原因到上传阻断原因的映射，设备服务继续保留心跳、识别、上传闸门和状态事件职责。
- 新增缓存协调器回归测试，覆盖缓存读取和保存异常时的日志行为。
- `DeviceService.cs` 已从 524 行降至 499 行，低于 500 行治理警戒线。

## 验证命令

```powershell
dotnet test src\Tests\IIoT.Edge.NonUiRegressionTests
dotnet build src\Edge\IIoT.Edge.Shell
```

## 验证结果

- `IIoT.Edge.NonUiRegressionTests`：通过 367 个测试。
- `IIoT.Edge.Shell`：构建成功，0 警告，0 错误。

## 剩余风险

- `DeviceService` 仍接近 500 行。后续如果继续新增设备运行时职责，应优先继续提取心跳生命周期、状态事件或上传闸门状态协调能力，而不是继续扩大该类。
