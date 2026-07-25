# 2026-07-24 Launcher Velopack 工序启动路径修复

## Host 2.0.5

本补丁在当前 2.x 代码上前向修复，不回退或覆盖已经发布的 2.0.4。Launcher 以 Host 基础 `launcher.profiles.json` 中的实际入口作为唯一运行时位置；AP/CP activation 贡献的工序 profile 不再各自解释包内相对路径，而是继承同一份 Host Shell 路径。机器配置 reconciliation 也从该入口推导 Host 目录。

在正式 Velopack 布局中，Launcher 位于 `current/`，Shell 位于 `current/host/IIoT.Edge.Shell.exe`。修复后 Default、正极模切和负极模切必须全部指向这一份 Shell，外部 machine profile 必须写入同一运行时数据布局，禁止再生成或使用错误的安装根 `host/`。

本次不修改 Shell 插件装载、PLC、MES、Cloud、DataPipeline、补传、设备身份、ClientCode 或 bootstrap secret 语义。

标准上传从 prepared release 绑定的 exact-SHA detached 快照执行；仅当工作区控制面显式标记该快照时允许分支名为 `HEAD`，同时仍核对本地 `main`、`origin/main` 与候选 SHA 完全一致。该修复只消除正式 Deploy 对合法预制快照的误拒，不放宽未推送、漂移或任意 detached checkout 的发布权限。

## AP/CP 2.0.5

AP 与 CP 产品版本和 Host 兼容窗口提升为不可变 `2.0.5`，`hostApiVersion` 保持 `2.0.0`。两个插件继续携带各自的 activation launcher/machine profile；插件业务源码、PLC 点位、双弹夹状态机、MES/Cloud 字段和设备播种不变。

## 当前验证

- Launcher Filesystem 定向回归：76/76 通过，覆盖真实 `current/host` 布局、AP/CP 同一 Shell 入口、机器配置生成和错误安装根目录不产生。
- AP/CP activation 打包契约通过，版本和 Host 兼容窗口均锁定 `2.0.5`。
- Edge 默认 selector 选择出的 Architecture 5/5、Security 9/9、Launcher Unit 35/35、Launcher UI 13/13、Launcher Filesystem 76/76 全部通过，共 138/138，0 失败、0 跳过。

## 尚未完成

- Windows 实际安装、Launcher 两张卡片逐一启动、正确 `Shell__MachineProfile` 进程保持和现场 PLC 加载尚未执行，状态为 `NOT-RUN`。
- 设备创建、ClientCode 注册、bootstrap secret 轮换、Cloud 更新详情恢复和生产发布均未执行。
- 在 Windows 实机与设备身份/更新链闭环前，不得把本地测试、卡片可见、ZIP 存在、catalog 版本或 HTTP 200 写成生产恢复完成。
