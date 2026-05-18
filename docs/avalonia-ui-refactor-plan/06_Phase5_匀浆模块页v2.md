# Phase 5：匀浆模块页 v2 小计划

> 本阶段只改匀浆模块 Presentation 层。
> 目标：把“匀浆出料数据”做成真实、精致、可信的业务页。

---

## 1. 阶段目标

匀浆页必须从“普通表格页”升级为“真实业务数据页”。

页面要回答：

- 这是哪个业务页？
- 数据来自哪里？
- 当前有没有真实出料记录？
- 当前记录数量是多少？
- 如果为空，为什么为空？
- 关键业务列是什么？

---

## 2. 允许修改的文件类别

只允许修改：

- `src/Modules/IIoT.Edge.Module.Homogenization/Presentation/HomogenizationDataViewModel.cs`
- `src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.axaml`
- `src/Modules/IIoT.Edge.Module.Homogenization/Presentation/Views/HomogenizationDataPage.axaml.cs`，如确有必要
- `src/Modules/IIoT.Edge.Module.Homogenization/Resources/Languages/zh-CN.xaml`
- `src/Modules/IIoT.Edge.Module.Homogenization/Resources/Languages/en-US.xaml`
- `src/Modules/IIoT.Edge.Module.Homogenization/Resources/HomogenizationText.cs`
- 对应 Presentation 测试
- 本阶段文档记录

---

## 3. 禁止修改的文件类别

禁止修改：

- `src/Modules/IIoT.Edge.Module.Homogenization/Runtime/**`
- `src/Modules/IIoT.Edge.Module.Homogenization/Integration/**`
- `src/Modules/IIoT.Edge.Module.Homogenization/Config/**`
- `src/Modules/IIoT.Edge.Module.Homogenization/Payload/**`，除非单独批准
- PLC 任务
- MES 上传
- Cloud 上传
- 缓存、重试、死信
- `HomogenizationContext` 业务结构
- 任何生产数据生成逻辑

---

## 4. 数据来源规则

允许展示：

- `IProductionContextStore.GetAll()` 中的 `HomogenizationContext`。
- `HomogenizationContext.OutboundRecords`。
- 从真实 records 直接计算的展示统计，例如记录数、最近出料时间。

禁止展示：

- fake 今日产量。
- fake 良率。
- fake 节拍。
- fake 当前运行工位。
- fake PLC/MES/Cloud 状态。
- 任何写死表格记录。

如果没有 records：

- 显示真实空态。
- 不显示假样例。
- 不自动插入开发样例。

---

## 5. 页面结构建议

```text
匀浆出料数据
说明：显示本地生产上下文中的真实出料记录

┌── 页面摘要卡 ───────────────────────────────┐
│ 记录数：真实 records count                  │
│ 最近出料：真实最新 CompletedTime 或 --       │
│ 数据来源：本地生产上下文                     │
└────────────────────────────────────────────┘

┌── 出料记录表 ───────────────────────────────┐
│ 托盘码 | 进站时间 | 出站时间 | 状态 | 搅拌速度 | 温度 | 真空 | CNT | NMP │
└────────────────────────────────────────────┘

空态：
暂无匀浆出料记录。运行链路启动并产生本地生产上下文后，将在此显示真实出料数据。
```

---

## 6. 表格列要求

保留真实业务列：

- 托盘码
- 进站时间
- 出站时间
- 状态
- 搅拌速度
- 温度
- 真空
- CNT 实际量
- NMP 实际量

要求：

- 列标题资源化。
- 长托盘码可截断或 tooltip。
- 时间格式统一。
- 空值显示 `--`，不要显示 `null`。
- 1366×768 下支持横向滚动。

---

## 7. UI 视觉要求

必须同源 Phase 4：

- 页面背景融入 Shell。
- 主卡片白色、大圆角、柔和阴影。
- 表格柔和，不用硬网格。
- 空态精致，不用简单 `--` 图标占位。
- chip 显示“只读 / 本地上下文 / 真实数据”。
- 不要照抄参考图工位流程图。

---

## 8. ViewModel 要求

允许新增展示属性：

- `RecordCountText`
- `LatestOutboundTimeText`
- `DataSourceText`
- `HasRecords`
- `HasNoRecords`
- 真实 records 派生的状态文案

禁止新增：

- 业务服务调用。
- PLC/MES/Cloud 调用。
- fake records。
- fake timer 生成数据。

刷新机制：

- 可以保留现有 timer 刷新。
- 刷新只读 `IProductionContextStore`。
- 不写入业务数据。

---

## 9. 测试要求

必须覆盖：

1. 没有 `HomogenizationContext` 时显示真实空态。
2. 有 `OutboundRecords` 时显示真实记录数。
3. 空字段显示 `--`。
4. 不生成 mock records。
5. 刷新只读上下文，不写入。
6. 资源键中英文成对。

---

## 10. 验收标准

本阶段通过条件：

- 匀浆页看起来像真实业务页。
- 数据来自真实上下文。
- 空态真实。
- 表格精致且可读。
- 不改 Runtime/Integration/Config。
- 不新增假数据。

---

## 11. 验收命令

```powershell
dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

必要时追加：

```powershell
dotnet test src\Tests\IIoT.Edge.Module.ContractTests\IIoT.Edge.Module.ContractTests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.NonUiRegressionTests\IIoT.Edge.NonUiRegressionTests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

---

## 12. 停止条件

必须停止：

- 需要改 `HomogenizationContext`。
- 需要改 Runtime task。
- 需要 fake 出料记录。
- 需要改 MES/Cloud 上传。
- 无法确认真实数据字段含义。
