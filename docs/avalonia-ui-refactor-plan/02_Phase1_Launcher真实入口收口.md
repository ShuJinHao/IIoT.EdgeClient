# Phase 1：Launcher 真实入口收口小计划

> 本阶段只解决一个 P0 问题：正式产品中一个工序只能有一个启动入口，并且该入口必须进入真实运行链路。

---

## 1. 阶段目标

将 Launcher 从“迁移验证/运行联调选择器”改成“现场正式启动器”。

最终要求：

- `HomogenizationLine` 在正式 Launcher 中只出现一个入口。
- 该入口文案为现场用户能理解的“匀浆产线”或等价名称。
- 该入口启动 AvaloniaShell 时必须进入真实运行链路。
- UI-only 只能作为开发内部模式保留，不能出现在正式 Launcher 默认页面。

---

## 2. 当前问题

当前 `launcher.profiles.json` 中同一个 `MachineProfile = HomogenizationLine` 存在两个入口：

- UI-only：不启动真实运行链路。
- Runtime：传入 `--start-runtime`，用于运行联调。

这会造成现场产品语义错误：用户会看到一个工序有多个启动方式。

---

## 3. 允许修改的文件类别

只允许修改：

- `src/Edge/IIoT.Edge.Launcher.Avalonia/launcher.profiles.json`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Models/LauncherProfileDefinition.cs`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Services/LauncherProfileCatalog.cs`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Services/ShellLaunchService.cs`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/ViewModels/LauncherProfileViewModel.cs`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Views/LauncherProfileView.axaml`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Resources/Languages/zh-CN.xaml`
- `src/Edge/IIoT.Edge.Launcher.Avalonia/Resources/Languages/en-US.xaml`
- `src/Tests/IIoT.Edge.Launcher.Tests/**`
- 本阶段文档记录

---

## 4. 禁止修改的文件类别

禁止修改：

- `src/Edge/IIoT.Edge.AvaloniaShell/Services/AvaloniaShellStartupCoordinator.cs`，除非 Phase 0 证明必须改且本阶段计划已更新
- `src/Edge/IIoT.Edge.Host.Bootstrap/**`
- `src/Application/**`
- `src/Runtime/**`
- `src/Infrastructure/**`
- `src/Modules/**/Runtime/**`
- `src/Modules/**/Integration/**`
- PLC/MES/Cloud/上传/重试/死信代码

---

## 5. 具体执行要求

### 5.1 收口正式 profile

`launcher.profiles.json` 默认正式配置中，只保留一个 `HomogenizationLine` profile。

建议目标语义：

```json
{
  "ProfileId": "HomogenizationLine",
  "DisplayName": "匀浆产线",
  "Description": "启动匀浆产线客户端并进入真实运行链路。",
  "MachineProfile": "HomogenizationLine",
  "ExecutablePath": "..\\avalonia-shell\\IIoT.Edge.AvaloniaShell.exe",
  "Arguments": [ "--start-runtime" ]
}
```

要求：

- 默认正式 profile 必须带 `--start-runtime`，或者 `ShellLaunchService` 必须对正式 profile 自动补入真实启动参数。
- 两种方案只能选一种，不能两边都偷偷处理造成重复语义。
- 推荐优先改 profile，保持 Shell 启动协调器语义不变。

### 5.2 UI-only 内部化

如需保留 UI-only：

允许方式：

- 单独开发配置文件，例如 `launcher.profiles.Development.json`。
- 命令行开发开关。
- 测试专用 fixture。

禁止方式：

- 默认 `launcher.profiles.json` 里出现 UI-only。
- 正式 Launcher 页面出现“迁移验证 UI-only”。
- 正式 Launcher 页面出现“运行联调”。
- 同一个工序显示 Variant chip。

### 5.3 去掉无意义搜索框

如果正式产品页只有一个或极少数工序，并且没有真实搜索业务需求：

- 删除 Profile 搜索框。
- 删除搜索图标。
- 删除搜索相关占位文案。

如果必须保留搜索：

- 必须说明真实业务场景。
- 必须有测试覆盖过滤行为。
- 搜索框不能只是装饰。

### 5.4 去掉 Variant 展示

正式产品入口不显示 Variant。

禁止显示：

- UI-only variant
- Runtime variant
- 多个启动 chip
- 多个模式入口

如果内部配置中同一 `MachineProfile` 出现重复 profile：

- 正式 Catalog 应报错或过滤内部 profile。
- 测试必须覆盖重复正式入口。

---

## 6. UI 要求

Launcher 要接近参考图水准：

- 浅暖灰画布。
- 大白圆角 shell。
- 左侧品牌/环境/版本卡。
- 右侧登录/工序卡。
- 工序卡留白足够，按钮明确。
- lime 点缀用于“运行/正式/可启动”。
- 错误提示柔和但醒目。
- 没有硬边框、蓝色强主导、旧 WPF 表单感。

---

## 7. 测试要求

必须新增或修改测试，覆盖：

1. 默认 `launcher.profiles.json` 中同一个 `MachineProfile` 只能有一个正式入口。
2. `HomogenizationLine` 正式入口启动参数包含 `--start-runtime`。
3. Launcher ViewModel 不生成 UI-only/Runtime variants 给正式 UI。
4. 搜索框如果删除，相关资源和绑定不残留。
5. ShellLaunchService 仍正确传入 `Shell__MachineProfile`。
6. 找不到 exe 时仍有明确错误。

---

## 8. 验收标准

本阶段通过条件：

- Launcher 正式页只显示一个“匀浆产线”入口。
- 不显示 UI-only。
- 不显示运行联调。
- 不显示迁移验证。
- 不显示 Variant chip。
- 点击后真实启动链路。
- 构建和 Launcher 测试通过。

---

## 9. 验收命令

```powershell
dotnet build src\Edge\IIoT.Edge.AvaloniaShell\IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.AvaloniaShell.Tests\IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
dotnet test src\Tests\IIoT.Edge.Launcher.Tests\IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false
```

---

## 10. 停止条件

遇到以下情况必须停止：

- 需要改 Runtime 生命周期才能实现正式启动。
- 无法确认哪个 profile 是正式入口。
- 测试依赖 UI-only 默认入口且无法安全改造。
- 需要新增 mock 启动状态。

停止后输出：

```text
已停止：Launcher 正式入口语义无法在当前允许范围内收口。
原因：...
需要人工决策：...
```
