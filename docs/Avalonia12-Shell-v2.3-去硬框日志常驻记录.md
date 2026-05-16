# 主客户端 Shell v2.3 去硬框与日志常驻记录

## 完成内容

- 将主客户端右侧区域从“单个激活工具 Tab”调整为常驻上下结构：上半区显示设备状态，下半区显示运行日志。
- 保留 Dock 插件注册与加载机制，中央主业务区仍由现有 Dock 文档区承载，右侧只改变 Shell 展示方式。
- 去掉 Header 底部分隔线，弱化左侧 rail、主工作区和右侧工具区的硬边框感。
- 共享主题中继续收敛圆角、浅暖灰画布和无边框工具区样式，让主窗口更接近圆滑上位机工作台。
- 运行日志仍复用现有 `ILogService.EntryAdded` 缓存与日志文件读取结果，不新增 mock 日志。

## 边界

- 本批只修改 `IIoT.EdgeClient.AvaloniaMigration` 主客户端 Shell 展示层、Shell ViewModel 绑定状态和共享 Avalonia 主题。
- 未修改 PLC、MES、Cloud、缓存、上传、重试、死信、模块运行时、认证和启动链路。
- 未修改 Dock 注册协议、模块加载协议和任何模块业务逻辑。
- 未新增生产字段、后端接口或模拟数据。

## 验证命令

- `dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
- `dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
- `dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`

## 验证结果

- Shell 构建通过，0 warning，0 error。
- `IIoT.Edge.AvaloniaShell.Tests` 通过 59 个测试。
- `IIoT.Edge.Launcher.Tests` 通过 27 个测试。
- 目标 AXAML / 主题 / Shell ViewModel 范围内 inline 中文扫描无命中。
- 目标范围乱码片段扫描无命中。

## 剩余风险

- 仍需要人工在真实窗口中检查 `1900x1200`、`1600x1000`、`1366x768` 三档视觉效果。
- 中央模块页面是否仍有旧表格硬框，取决于模块自身 Presentation 层；本批只修 Shell 容器。
- 如果右侧常驻区在 `1366x768` 下仍显拥挤，下一步应只做窄屏降级，不应回退到默认 Dock 外观。

## 下一阶段进入条件

- 人工确认 Shell v2.3 的去硬框、设备状态常驻和运行日志常驻方向可接受。
- 再按页面优先级继续精修模块页面或宿主页，不一次性扩散到全部模块。
