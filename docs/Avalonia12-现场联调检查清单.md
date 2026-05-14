# Avalonia 12 现场联调检查清单

## 适用范围

- 仅适用于旁路副本 `IIoT.EdgeClient.AvaloniaMigration` 生成的 Avalonia 联调包。
- 默认入口为 UI-only，不启动真实运行链路。
- 只有显式选择运行联调入口或传入 `--start-runtime` 时，才启动运行时生命周期。
- 本清单只覆盖只读快照和受控写入运行时缓冲，不承诺自动判断 PLC 物理写入最终成功。

## 联调步骤

1. 启动 `avalonia-launcher`，选择 Avalonia UI-only 迁移验证入口。
2. 确认 Shell 可以打开，Header/Footer 显示 UI-only 状态，诊断页无运行时启动错误。
3. 退出 UI-only 入口，再选择 Avalonia 运行联调入口，确认启动参数包含 `--start-runtime`。
4. 等待 Header/Footer 显示运行中，并在诊断页确认模块数、PLC 设备数、运行目录和阻断问题数。
5. 打开 I/O 交互页，选择现场 PLC 设备，执行手动读取。
6. 确认页面显示数据来源为运行时快照，刷新时间已更新；如果提示未启动、无快照或设备未绑定，先保存截图和诊断日志，不继续写入。
7. 在有权限、PLC 已连接、目标交互项可写时，申请写入运行时缓冲。
8. 确认弹窗文案说明“写入运行时缓冲，等待运行链路按块策略写入 PLC”，取消时不得改缓冲。
9. 确认写入后，检查 I/O 行的写入结果为“已进入运行时缓冲，等待扫描任务按块写入”。
10. 等待至少一个扫描周期，刷新 I/O 页或诊断页，查看“PLC 写入轨迹”是否出现尝试、成功或失败记录。
11. 打开 Equipment 面板，确认“最近块写入”展示设备、地址、字数、状态和时间。
12. 保存以下证据：I/O 页面截图、Diagnostics 的 I/O 写入闸门和 PLC 写入轨迹截图、运行目录下日志文件、现场 PLC 侧状态截图。
13. 如需统一回收证据，执行 `scripts\CollectAvaloniaFieldEvidence.ps1 -CreateZip`，并把截图放入生成包的 `screenshots` 目录。

## 禁止操作

- 不从 Avalonia UI 直接调用 PLC 单点写入。
- 不在诊断页执行 Cloud/MES 清理、重试、删除。
- 不修改数据库结构、业务配置 JSON、PLC 块规划策略或 Cloud/MES API。
- 不把运行联调入口设为默认 Launcher profile。

## 回退口径

- UI-only 启动失败：回退 WPF Launcher/WPF Shell，保留 Avalonia 日志和截图。
- `--start-runtime` 启动失败：不继续 I/O 写入申请，保存启动失败详情和诊断日志。
- I/O 写入轨迹失败：不自动重试、不清理缓冲，交由现场人工结合 PLC 侧状态判断。
- 证据采集脚本只复制日志、Diagnostics 摘要、Launcher profile 和联调文档，不读取或修改业务数据库。
