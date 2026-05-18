# Avalonia UI 验收清单

## 自动检查

- `dotnet build src/Edge/IIoT.Edge.AvaloniaShell/IIoT.Edge.AvaloniaShell.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
- `dotnet build src/Edge/IIoT.Edge.Launcher.Avalonia/IIoT.Edge.Launcher.Avalonia.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.AvaloniaShell.Tests/IIoT.Edge.AvaloniaShell.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
- `dotnet test src/Tests/IIoT.Edge.Launcher.Tests/IIoT.Edge.Launcher.Tests.csproj --no-restore /m:1 /p:UseSharedCompilation=false`
- Navigation、Panels、Shell、Launcher 的 `zh-CN.xaml` / `en-US.xaml` key 配对无缺项。
- 主题 token 和关键 class 存在：card、KPI、状态卡、表格、日志、chip、空态、表单、右侧面板。
- 非主题 XAML 不新增十六进制颜色，不新增手写 SVG Path 图标。

## 真实数据检查

- 运行状态来自现有 runtime state 或配置，不造假正常。
- PLC、MES、Cloud、缓存队列、日志告警只展示现有真实来源。
- 没有真实来源的字段显示未知、空态或错误原因，不补假数据。
- 日志时间解析失败时显示未知时间，不回退到当前时间。
- 通知角标没有真实通知源时保持 0 并隐藏。

## 1366x768

- Shell 无原生标题栏，窗口内容完整可见。
- Monitor 首页、右侧日志常驻区、右侧状态卡和底部表格不重叠。
- 五个标准宿主页直接承载截图非空，表格容器不溢出，工具栏可见。
- 中文标题、按钮、chip、表头不挤出父容器。
- 空态不伪装为正常状态。

## 1600x1000

- 需要在目标屏幕或工控机做真实窗口人工验收。
- 当前本机 1440x900 屏幕下生成的受限截图不能标记为完整通过。
- 重点检查右侧面板宽度、日志卡密度、表格列宽和顶部状态区间距。

## 1900x1200

- 需要在目标屏幕或工控机做真实窗口人工验收。
- 检查大屏下页面是否保持工作台密度，不出现过度留白、硬边框压迫或卡片套卡片。
- 检查 Launcher 单入口、Shell 默认 Monitor、宿主页直接承载截图三类路径是否和记录一致。

## 人工验收结论写法

- 通过：写明尺寸、页面、截图路径和关键观察点。
- 受限：写明本机屏幕限制或依赖缺失，不能把受限截图写成通过。
- 失败：写明可见问题、涉及页面和允许修补的展示层文件。
- 后续：只记录需要目标设备复核的项目，不把业务链路改动混入 UI 验收。
