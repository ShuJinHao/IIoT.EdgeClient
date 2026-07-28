# IIoT.EdgeClient Instructions

工作区 `../docs/总规则.md` 是唯一默认必读入口。本文件只负责项目路由和少量 Edge 硬边界。

## 按需路由

- 进入 Edge 实际修改后，只读取 `docs/客户端规则.md` 中与本批模块直接相关的章节、相关源码和受影响测试。
- 在 `docs/业务规则临时和待后续处理问题/待后续处理问题.md` 的 `GAP-PLC-023` 关闭前，涉及 Launcher、安装、更新、Cloud/MES 或 PLC 的修改还必须读取 `docs/业务规则临时和待后续处理问题/业务规则.md` 中尚未迁移的对应规则及相关 GAP；只有完成 62/62 迁移、六项长期文档删除门禁和用户明确确认并将 `GAP-PLC-023` 关闭后，才能删除本路由。
- 修改项目图、聚合/Persistence owner、插件 seam、Analyzer 或测试物理归口时，才读 `docs/Edge架构边界契约.md` 的相关章节。
- 修改 PLC/IO/设备选择/状态展示时，才读 `docs/PLC选择与状态展示控制.md`；修改对应 Avalonia 页面时再读 UI 专题规范。
- 部署、发布、更新或安装链才读取 `docs/客户端部署.md` 和工作区部署总览的对应章节。
- 历史事实只从 Git、发布目录和部署记录追溯；不得新建滚动复盘、历史核心类文档、日期式治理快照或日期式 release 文档。真实事故统一写入工作区 `docs/事故/生产事故.md` 或 `docs/事故/部署事故.md`。

## 项目硬边界

- Launcher/Shell 必须启动到可登录、可诊断、可修配置的 UI；业务配置、设备、PLC、MES、Cloud、IO 映射、模块或诊断问题不得造成 fatal 启动失败。
- Cloud 与 MES 的 probe、gate、queue、retry、fallback 和 deadletter 必须分离；Cloud 上传身份固定为 `ClientCode -> bootstrap -> DeviceId`。
- 新工序只通过插件扩展，不能写回 Host 全局分支；UI 只接真实路由、数据、命令和权限，Release 不得注入视觉假数据。
- Host、SDK、Private Plugins 是独立仓库，只能通过正式 SDK PackageReference 依赖；Host 不拥有具体工序源码、资源、配置或 pack 实现。

## 任务与部署

- 沟通/审计只读且不运行测试；业务开发只运行 Architecture、Security 和 owner 选出的受影响 Business。启动/UI 改动另做真实启动或可视验收；重型质量与三端对齐只在用户明确授权时运行，影响无法归属时停止。
- Edge 候选验证、产物准备、普通投放分别走工作区 `deploy/Validate-Candidate.ps1`、`deploy/Prepare-Release.ps1`、`deploy/Deploy-Changed.ps1 -PreparedReleaseId <id>`。测试和 Host/AP/CP pack 只能发生在 Validate/Prepare；正式 Deploy 只上传预制包，不创建提交、不编辑文件、不再运行测试或构建。
- 用户明确说“赶生产”“立刻上线”或“紧急生产”时走工作区 `deploy/Deploy-ProductionNow.ps1 -Target Edge`：不运行测试或 CI，先封存 Host/SDK/Private Plugins 当前字节，再只打包一次；发布失败归档本轮新版本，结果不得标记为绿色。
- 三端从零部署只走工作区 `deploy/Deploy-FromZero.ps1 -PreparedReleaseId <id>`；Cloud 清空前必须已准备 Host/AP/CP 全部包，清空后只上传和验收；可重新签发发布 API key并写回 Keychain，但不创建设备、不注册 `ClientCode`、不轮换设备 bootstrap secret。
- 长期规则直接进入本文件、`docs/客户端规则.md` 或对应专题契约；事故只进入工作区事故文档，普通提交和版本变化只由 Git、发布目录与部署记录保存。
