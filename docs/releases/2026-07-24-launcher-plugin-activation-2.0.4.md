# 2026-07-24 正负极工序激活与 Launcher 修复

## Host 2.0.4

修复三仓拆分后 Launcher 只读取 Host 默认工序、无法从已安装 AP/CP 插件发现正负极工序的问题。Launcher 现在会读取插件包内受控的 `activation/manifest.json`，校验工序、模块、机器配置和宿主入口后生成对应工序卡片；无效插件只记录告警并回落默认工序，不阻断 Launcher 启动。

首装设备绑定改为只消费已经成功写入机器配置的项。插件尚未安装或工序尚未就绪时，对应绑定继续保留，避免启动后丢失 AP/CP 的 `ClientCode` 与 bootstrap secret。

工序启动按钮只在 Launcher 内改用既有品牌黄色资源，不修改共享 SDK 主题，也不改变 Shell、PLC、MES、Cloud、DataPipeline、补传或设备身份业务链。

Host 继续发布为 `win-x64` self-contained，Launcher 与 Shell 必须携带 `coreclr.dll`、`hostfxr.dll`、`hostpolicy.dll` 和 `System.Private.CoreLib.dll`。Host API 代际保持 `2.0.0`。

## AP/CP 2.0.4

AP 与 CP 插件包新增受控 activation payload：

- `activation/manifest.json`
- `activation/launcher/<profile>.json`
- `activation/machine/<profile>.json`

打包时强制校验模块、工序、`InstanceId`、`Shell.MachineProfile`、Shell 可执行入口、唯一启用模块以及空 Cloud 身份模板。AP/CP 版本和 Host 兼容窗口均锁定为不可变 `2.0.4`，`hostApiVersion` 保持 `2.0.0`。

## 已完成验证

- Edge 默认 selector：Architecture 5/5、Security 9/9、Launcher UI 13/13、Launcher Filesystem 75/75、Launcher Unit 35/35。
- AP/CP activation 打包契约测试通过。
- 三仓拆分边界保持：Host 不内置具体工序插件，AP/CP 继续独立打包和发布。

## 安全边界

本次不创建设备、不注册 `ClientCode`、不生成真实设备安装包、不轮换设备 bootstrap secret，不修改现场 PLC/MES/Cloud 配置或生产数据。

无 .NET、无网络 Windows x64 实机安装和 AP/CP 实际启动仍需使用本次 Cloud 下载产物完成最终现场验收；在该验收完成前不得宣称离线实机验收通过。
