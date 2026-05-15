# Avalonia 12 第九批迁移记录：运行联调验收 + 只读现场闭环 + 发布布局预检

## 范围

- 继续只在 `IIoT.EdgeClient.AvaloniaMigration` 旁路副本实施。
- 默认启动仍是 UI-only；只有显式传入 `--start-runtime` 才启动运行链路。
- 本批不做真实 PLC 写入，不做 Cloud/MES 清理、重试、删除，不改数据库结构、Cloud/MES API、PLC 策略或业务规则。

## 本批完成项

- Shell 运行状态从单一布尔值扩展为 `UI-only`、`启动中`、`运行中`、`启动失败`、`停机中`，Header/Footer 均可显示当前状态。
- `--start-runtime` 成功后生成启动诊断摘要，包含模块数、PLC 设备数、阻断问题数和运行目录。
- 启动失败窗口展示可复制的错误详情、启动诊断摘要和诊断日志路径。
- I/O 手动读取区分未启动、已启动但无快照、设备未绑定、有运行时快照四类状态；读取仍只来自运行时快照，不访问真实 PLC。
- Equipment 面板补齐 PLC、Cloud、MES、运行链路分组，只读展示状态，不提供清理或重试按钮。
- Log 面板增加日志文件选择和刷新，只读取迁移运行目录日志，不写日志、不改日志目录。
- AvaloniaShell 增加 Avalonia 插件 artifacts 构建/发布复制目标，输出到 `Modules/{ModuleId}`。
- Launcher.Avalonia 固定两个 profile：迁移验证 UI-only、运行联调 `--start-runtime`。
- 增加发布布局预检测试，锁定 Launcher profile、插件 manifest、模块配置和 AvaloniaShell 插件复制目标。

## 验证重点

- 默认启动不调用 lifecycle；`--start-runtime` 才调用 lifecycle。
- 启动失败路径能带出错误详情和日志路径。
- I/O 页面只读读取运行时快照，真实写入继续禁用。
- 发布布局中必须包含匀浆 Avalonia 插件 DLL、`plugin.json`、`Config/homogenization.module.json` 和 launcher profiles。
- Avalonia 资源中不再保留 Demo 文案。

## 未纳入本批

- 真实 PLC 写入闭环。
- Cloud/MES 死信清理、重试、删除。
- 现场硬件写入联调。
- WPF 主线切换或清理。
