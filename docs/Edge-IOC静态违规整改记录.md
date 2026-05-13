# Edge IOC 静态违规整改记录

## 阶段：Edge IOC 静态违规整改

### 目标

本阶段只处理 `IIoT.EdgeClient` 中含 IO、副作用、业务分支或静态可变状态的静态类，按 IOC 规则改为接口、DI 注册和构造函数注入；不修改 `IIoT.CloudPlatform`、`AICopilot`、工业协议适配或 Launcher DI。

### 完成内容

- 将崩溃日志写入改为 `ICrashLogWriter` 单例服务，保留主路径、降级路径和诊断输出三级路由。
- 将 Shell 配置加载和运行时路径解析改为 `IShellConfigurationLoader`、`IShellRuntimePathResolver` 实例服务。
- 将模块发现、manifest 读取、依赖排序、版本兼容校验和插件程序集加载改为 `IModuleCatalog` 及模块加载协作者。
- 将运行时 `ProductionContext` 与 PLC IO 映射绑定改为 `IProductionContextSignalBindingStore` 单例服务。
- 更新 Shell、模块契约和非 UI 回归测试，去除旧静态入口调用。

### 改动范围

- `IIoT.Edge.Host.Bootstrap`：崩溃日志、模块目录、插件程序集加载、Shell 配置/路径解析、PLC 任务绑定和 DI 注册。
- `IIoT.Edge.Shell`：启动期组合根使用启动服务容器解析 Shell IOC 服务，并把崩溃日志实例传入主服务容器。
- `IIoT.Edge.Runtime` 与 `IIoT.Edge.Module.Homogenization`：生产上下文信号绑定 store 注入和逻辑信号访问器创建。
- `IIoT.Edge.*Tests`：同步测试构造方式和服务注册。

### 验证记录

- `dotnet build IIoT.EdgeClient/src/Edge/IIoT.Edge.Shell --no-restore`：通过，0 警告，0 错误。
- `dotnet test IIoT.EdgeClient/src/Tests/IIoT.Edge.Shell.Tests --no-restore`：通过，62 个测试；存在既有测试桩未使用事件警告。
- `dotnet test IIoT.EdgeClient/src/Tests/IIoT.Edge.Module.ContractTests --no-restore`：通过，28 个测试。
- `dotnet test IIoT.EdgeClient/src/Tests/IIoT.Edge.NonUiRegressionTests --no-restore`：通过，341 个测试。
- 静态入口搜索：未再命中旧 `CrashLogWriter`、`DirectoryModuleCatalog`、`ShellConfigurationLoader`、`ShellRuntimePathResolver`、`ShellModuleCatalog`、`ProductionContextSignalBindings` 的静态调用或静态类声明。

### 剩余事项

- Launcher 手动 new 服务未纳入本阶段，后续单独做 Launcher DI 收口。
- Cloud Admin 检查、`RefreshTokenSession`、`EfRepository.Update`、ProductionService 测试空项目、工业协议适配和 AICopilot 命名债务均未纳入本阶段。
