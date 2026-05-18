# Phase 6：宿主页逐页细腻化小计划

> 本阶段目标：把 Avalonia 宿主页面逐页做细，不再像临时迁移页面。
> 原则：一组页面一组页面做，不一次性乱改。

---

## 1. 阶段目标

逐页提升以下页面：

1. 诊断
2. I/O
3. 硬件配置
4. 网络设备
5. 串口设备
6. I/O 映射
7. 配方
8. 参数
9. PLC 任务绑定
10. 产能
11. 数据
12. 监控

目标是：

- 信息层级清楚。
- 操作边界清楚。
- 空态真实。
- 表格和表单精致。
- 危险操作清楚确认。
- 不破坏业务链路。

---

## 2. 总允许修改范围

允许修改：

- `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia/**/Views/*.axaml`
- `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia/**/ViewModels/*.cs`，仅限展示投影、状态文案、空态、排序、只读标记
- `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia/Resources/Languages/zh-CN.xaml`
- `src/Presentation/IIoT.Edge.Presentation.Navigation.Avalonia/Resources/Languages/en-US.xaml`
- `src/Shared/IIoT.Edge.UI.Avalonia/Themes/**`
- 对应 UI 测试

---

## 3. 总禁止修改范围

禁止修改：

- Application 业务逻辑
- Domain / SharedKernel
- Runtime
- Infrastructure
- PLC 通讯
- MES/Cloud 上传
- 数据库结构
- 缓存/重试/死信
- 模块 Runtime/Integration
- 插件加载协议

---

## 4. 页面改造顺序

### 4.1 第一组：诊断 + 日志关联

目标：让诊断页能解释“系统哪里有问题”。

要求：

- 启动诊断、运行诊断、Cloud/MES 死信、缓存、上传积压分区清楚。
- 阻断问题醒目。
- 操作按钮清楚。
- 死信清理必须确认。
- 不能把诊断摘要伪装成运行日志。

### 4.2 第二组：I/O + PLC 写入闸门

目标：现场操作员能清楚看到 I/O 当前状态和写入风险。

要求：

- 读/写分区清楚。
- PLC 未连接时禁止写入态明确。
- 无权限时禁止写入态明确。
- 最近写入 trace 清楚。
- 危险写入必须确认。
- 不允许 fake I/O 值。

### 4.3 第三组：硬件配置 + 网络 + 串口 + 映射

目标：配置页像工业配置台，不像普通 CRUD。

要求：

- 左侧设备列表，右侧详情/编辑。
- 网络和串口信息分组。
- 映射关系清楚。
- 保存/取消/校验状态明确。
- 错误提示资源化。
- 不改配置业务规则。

### 4.4 第四组：配方 + 参数 + PLC 任务绑定

目标：参数配置和配方页可读、可维护、可确认。

要求：

- 表单分组。
- 只读/可编辑状态明确。
- 修改状态明确。
- 保存前校验。
- 长参数名可读。
- 不改业务校验。

### 4.5 第五组：产能 + 数据 + 监控

目标：生产类页面统一视觉和真实状态。

要求：

- 只展示真实查询结果。
- KPI 必须从真实数据派生。
- 空态真实。
- 图表如没有真实数据，不显示假图。
- 不抄参考图内容。

---

## 5. 每个页面必须具备的结构

每个页面至少包含：

1. 页面标题。
2. 一句业务说明。
3. 当前数据来源或状态说明。
4. 主要内容区。
5. 操作区。
6. 空态。
7. 错误态。
8. 长文本处理。
9. 资源化文案。

---

## 6. UI 细节要求

- 页面统一使用 `edge-page-panel` / `edge-table-card` / `edge-form-section` 等共享样式。
- 不在页面内散落十六进制色值。
- 不新增页面专属奇怪样式。
- 表格统一表头、行高、空态。
- 按钮主次明确。
- 危险操作用 warning/error，但不刺眼。
- 滚动区域清楚，不出现双重滚动混乱。

---

## 7. 测试要求

每组页面至少补充：

- ViewModel 空态测试。
- 权限/只读状态测试。
- 危险操作确认测试。
- 资源键成对测试。
- 不含乱码测试。

---

## 8. 验收标准

本阶段通过条件：

- 页面逐组验收，不跨组乱改。
- 每组页面达到 Shell 设计系统水准。
- 没有 fake 数据。
- 没有业务层改动。
- 没有破坏 DDD/依赖倒置/插件加载。
- 1366×768 可用。

---

## 9. 验收命令

```powershell
dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

建议追加：

```powershell
dotnet test src\Tests\IIoT.Edge.NonUiRegressionTests\IIoT.Edge.NonUiRegressionTests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

---

## 10. 停止条件

必须停止：

- 某页面需要改业务层才能展示。
- 需要 fake KPI 或 fake 图表。
- 无法确认某操作是否危险。
- 需要绕过权限。
- 需要直接调 Infrastructure。
