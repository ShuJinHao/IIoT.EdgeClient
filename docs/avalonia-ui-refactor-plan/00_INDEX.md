# Avalonia UI 7 批计划索引

本目录来自 `C:\Users\jinha\Downloads\avalonia_ui_refactor_plan.zip`。索引用于对齐 zip 计划、当前未提交 diff、真实窗口验收记录和后续批次边界。

## 文件清单

- `00_UI总体改造总计划.md`
- `01_Phase0_冻结范围与事实核查.md`
- `02_Phase1_Launcher真实入口收口.md`
- `03_Phase2_Shell信息骨架重建.md`
- `04_Phase3_日志与设备状态真实化.md`
- `05_Phase4_去硬框与视觉圆滑化.md`
- `06_Phase5_匀浆模块页v2.md`
- `07_Phase6_宿主页逐页细腻化.md`
- `08_Phase7_主题与设计系统固化.md`
- `09_SKILL_Avalonia工业上位机前端规则.md`
- `10_Codex执行总提示词模板.md`

## 阶段状态索引

| 阶段 | 当前状态 | 评审口径 |
|---|---|---|
| Phase 0 冻结范围与事实核查 | 已完成文档落地与索引 | zip 原计划已落地；事实状态以迁移记录的整批收口验收记录为准。 |
| Phase 1 Launcher 真实入口收口 | 已由当前 diff 覆盖，待评审 | Launcher 已收口为现场单入口并携带 `--start-runtime`；不恢复 UI-only 正式入口。 |
| Phase 2 Shell 信息骨架重建 | 已由当前 diff 覆盖，待评审 | Shell 默认进入 Monitor；Header/Footer 和右侧常驻区按真实状态收口。 |
| Phase 3 日志与设备状态真实化 | 已由当前 diff 覆盖，待评审 | 日志时间不回退当前时间；右侧 PLC/MES/Cloud/缓存状态卡和错误日志来自现有真实来源。 |
| Phase 4 去硬框与视觉圆滑化 | 已覆盖 1366 验收，1600/1900 待人工验收 | 本机 1440x900 无法完整采集 `1600x1000`、`1900x1200`，需要目标屏幕或工控机复核。 |
| Phase 5 匀浆模块页 v2 | 已由当前 diff 覆盖，待评审 | `HomogenizationDataPage` 已单页标杆化；未改模块运行时、PLC/MES/Cloud 或上传链路。 |
| Phase 6 宿主页逐页细腻化 | 已由当前 diff 覆盖，待评审 | 五个标准宿主页已通过直接承载 helper 生成 `1366x768` 截图；匀浆插件覆盖标准 `DataViewPage` 的边界已记录。 |
| Phase 7 主题与设计系统固化 | 本批已执行，待评审 | 已补齐 `Edge.*` canonical token、保留 `Ind.*` 兼容 token，并新增 `Avalonia-Industrial-Design-System.md` 与 `Avalonia-UI-验收清单.md`。 |
| SKILL 规则 | 已同步到全局 skill，待实际使用验证 | 已新增 `C:\Users\jinha\.codex\skills\iiot-avalonia-hmi-polish\`，专门服务 Edge Avalonia 工业上位机 UI 任务；不混入 `iiot-frontend-polish`。 |
| Codex 提示词模板 | 已收口，待实际使用验证 | 已改为先读索引和迁移记录，再判断当前 Phase 是否已完成，避免重复执行已完成阶段。 |

## 当前最终口径

- 当前 Avalonia UI 改造不是 7 批全部代码都已评审通过，而是 Launcher、Monitor、Shell、日志、匀浆页、五个宿主页、验收 helper 和 Phase 7 设计系统固化均已进入可评审状态。
- `1600x1000` 和 `1900x1200` 真实窗口完整截图仍未完成，受当前 1440x900 屏幕限制，只能在目标显示器或工控机上验收。
- Phase 7 已执行仓库内设计系统固化；全局 `iiot-avalonia-hmi-polish` skill 和 Codex 执行模板已同步，后续仍需在真实任务中验证触发效果。
