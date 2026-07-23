# IIoT.EdgeClient Instructions

工作区 `../docs/总规则.md` 是唯一默认必读入口。本文件只负责项目路由和少量 Edge 硬边界。

## 按需路由

- 进入 Edge 实际修改后，只读取 `docs/客户端规则.md` 中与本批模块直接相关的章节、相关源码和受影响测试。
- 修改项目图、聚合/Persistence owner、插件 seam、Analyzer 或测试物理归口时，才读 `docs/Edge架构边界契约.md` 的相关章节。
- 修改 PLC/IO/设备选择/状态展示时，才读 `docs/PLC选择与状态展示控制.md`；修改对应 Avalonia 页面时再读 UI 专题规范。
- 部署、发布、更新或安装链才读取 `docs/客户端部署.md`、`docs/Edge安装更新验收.md` 和工作区部署总览的对应章节。
- 项目复盘、历史记录、旧计划和证据只在回归、冻结链路冲突、失败原因不明、同类故障追溯或用户明确要求时按关键词读取命中邻域。

## 项目硬边界

- Launcher/Shell 必须启动到可登录、可诊断、可修配置的 UI；业务配置、设备、PLC、MES、Cloud、IO 映射、模块或诊断问题不得造成 fatal 启动失败。
- Cloud 与 MES 的 probe、gate、queue、retry、fallback 和 deadletter 必须分离；Cloud 上传身份固定为 `ClientCode -> bootstrap -> DeviceId`。
- 新工序只通过插件扩展，不能写回 Host 全局分支；UI 只接真实路由、数据、命令和权限，Release 不得注入视觉假数据。
- Host、SDK、Private Plugins 是独立仓库，只能通过正式 SDK PackageReference 依赖；Host 不拥有具体工序源码、资源、配置或 pack 实现。

## 任务与部署

- 沟通/审计只读且不运行测试；业务开发只运行 Architecture、Security 和 owner 选出的受影响 Business。启动/UI 改动另做真实启动或可视验收；重型质量与三端对齐只在用户明确授权时运行，影响无法归属时停止。
- 普通部署只走工作区 `deploy/Deploy-Changed.ps1`：三仓必须 clean、已提交的 `main`，可 push 现有 HEAD，不创建提交、不编辑文件；只发布受影响 Host 或真实插件，Edge 部署不是远程安装 Windows，也不走 Harbor。
- 三端从零部署只走工作区 `deploy/Deploy-FromZero.ps1`；Cloud 清空后可重新签发发布 API key 并写回 Keychain，但不创建设备、不注册 `ClientCode`、不轮换设备 bootstrap secret。
- 只有形成长期规则、修复历史回归、处理生产事故或改变部署机制时，才更新项目复盘。
