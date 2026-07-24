# 2026-07-24 Windows 免运行时安装修复

## Host 2.0.3

修复 Host 2.0.2 的 Velopack 包未携带 .NET 运行时、导致 Windows 首装后提示下载 .NET 10 的问题。Launcher、Shell、安装素材和 Velopack 统一发布为 `win-x64` self-contained，现场机无需预装或在线下载 .NET。

本次不改变 Host API 代际，`hostApiVersion` 继续为 `2.0.0`；PLC、MES、Cloud/DataPipeline、队列、补传和设备身份链保持不变。

## AP/CP 2.0.3

AP/CP 业务逻辑不变，仅发布新的不可变插件补丁版本，并把已验证的 Host 兼容窗口扩展为 `2.0.0..2.0.3`，用于和 Host 2.0.3 形成可安装组合。

## 安全边界

本次不创建设备、不注册 `ClientCode`、不生成设备安装包、不轮换设备 bootstrap secret。发布完成后由管理员重新从 Cloud 下载中心为目标设备生成安装包。
