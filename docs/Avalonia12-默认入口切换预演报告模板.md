# Avalonia 12 默认入口切换 Preview 报告模板

## 基本信息

| 项目 | 内容 |
| --- | --- |
| 生成时间 |  |
| readiness summary |  |
| release root |  |
| preview 输出目录 |  |

## 当前默认入口

| 项目 | 当前入口 |
| --- | --- |
| Launcher | `IIoT.Edge.Launcher.exe` |
| Shell | `IIoT.Edge.Shell.exe` |

## 目标 Avalonia 入口

| 项目 | 目标入口 |
| --- | --- |
| Launcher | `IIoT.Edge.Launcher.Avalonia.exe` |
| Shell | `IIoT.Edge.AvaloniaShell.exe` |
| UI-only profile | `HomogenizationLineAvalonia` |
| 运行联调 profile | `HomogenizationLineAvaloniaRuntime` |

## 回退入口

| 项目 | 回退口径 |
| --- | --- |
| Launcher | `IIoT.Edge.Launcher.exe` |
| Shell | `IIoT.Edge.Shell.exe` |
| 回退负责人 | 人工填写 |

## 结论

- 本报告只用于 preview 评审，不代表真实切换。
- 第十七批不修改 Launcher profile、不改发布链路、不改 WPF 默认入口。
