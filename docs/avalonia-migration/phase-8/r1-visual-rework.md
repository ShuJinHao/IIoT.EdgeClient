# Phase 8 R1 视觉返工备忘（v2）

> **触发**：用户运行 R1 实施版后反馈"主界面没啥大变化"，实机截图核查证实视觉效果未达 v1.5 §5 / §1 / §3 / §4 规格。
> **作用域**：本文档不替代 `phase-8-shell-pilot.md v1.5`（计划本身没变），是对 R1 实施结果的返工指令。

---

## 1. 现状评估（基于用户实机截图）

| 区块 | 当前实施状态 | v1.5 计划要求 | 差距 |
|---|---|---|---|
| Dashboard KPI 卡 | 1×4 横排，**仅显示数字**（"0/1"、"0"、"0.0%"、"0"），无标题 | 2×2 网格，主副分层 C 方案 | 严重偏差 |
| KPI 主数字字号 | 视觉 ~30px | 48px Bold | 偏小 |
| KPI 副数据 | 完全没有 | 每卡 2 行副数据（标签左、数字右） | 缺失 |
| KPI 分割线 | 没有 | 1px `#EEF0EA` | 缺失 |
| Dashboard 告警列表 | 内嵌一个浅色 banner 框"暂无告警"占位 | 行结构卡，行高 56，状态点 + 时间 + 设备 + 内容 | 占位敷衍 |
| Header 右侧 | 运行中 chip + 本地模式 chip + 产线 chip + 铃铛 + OP 头像 + 本地会话 | 只有账号 chip | 信息过载，破坏 Apple 极简 |
| NavRail 顶部 logo | 40+px 大黄方块（像空按钮） | 24×24 小圆点 `#FFC400` | 尺寸 + 形状错 |
| NavRail "配置"入口图标 | 空白方框 | 正常 PathIcon | 渲染错 |
| 重点视觉锚点 | 全部白卡，无锚点 | 至少 1 张黑色重点卡 | 缺失 |
| 整体观感 | 工程师卡片堆砌 | Apple 级浅色工业控制台 | 没出参考图 1 漂浮感 |

---

## 2. 根因复盘

- **T1-T5、T7 代码层面基本符合 v1.5**，做到了视觉骨架（暖灰外圈、米白舞台、圆角 24、NavRail 黄色选中态）
- **T6 是核心视觉锚点，被偷工**：
  - 复用了 `EdgeKpiCard`（其默认外观是单数字小字号），**没按 v1.5 §5.2 用 `EdgeCard` 自己拼"主副分层"结构**
  - 1×4 而非 2×2 网格
  - 没加标题、副数据、分割线
- **T5 Header 没做"克制简化"**：保留了 v1.5 §4.2 说"可选保留**或隐藏**"的 EdgeStatusChip 群，R1 应选"隐藏"才符合 Apple 极简
- **NavRail logo 渲染错误**：尺寸 / 形状不对
- **NavRail 配置入口 PathIcon Data 渲染失败**：Path 用纯线段（无闭合）可能被 Avalonia 当作空形状

---

## 3. T6-v2 详细规格（必做）

### 3.1 布局

```
<ScrollViewer>
  <StackPanel Margin="32" Spacing="24">
    <SectionHeader Title="总览" Subtitle="今日生产状况一览" />  <!-- 可选保留 -->

    <Grid ColumnDefinitions="*,*" RowDefinitions="Auto,Auto"
          ColumnSpacing="20" RowSpacing="20">
      <!-- 左上：今日产量 -->
      <!-- 右上：良率 -->
      <!-- 左下：设备在线 -->
      <!-- 右下：关键告警（黑色反色卡）-->
    </Grid>

    <EdgeCard>告警列表</EdgeCard>
  </StackPanel>
</ScrollViewer>
```

- 整页 padding 32
- 4 张 KPI 卡 2×2 网格，间距 20px
- 每卡宽高比约 1:0.65

### 3.2 单卡 XAML 草稿（白色卡，前 3 张）

```xml
<edge:EdgeCard Elevation="Card" CardPadding="24">
  <Grid RowDefinitions="Auto,*,Auto,Auto">

    <!-- Row 0: 卡标题 -->
    <TextBlock Grid.Row="0"
               Text="今日产量"
               FontSize="13"
               Foreground="{DynamicResource Edge.Brush.Text.Muted}" />

    <!-- Row 1: 主数字区（居中）-->
    <StackPanel Grid.Row="1"
                VerticalAlignment="Center"
                HorizontalAlignment="Center"
                Spacing="4"
                Margin="0,16,0,16">
      <TextBlock Text="{Binding TodayOutput}"
                 FontSize="48"
                 FontWeight="Bold"
                 Foreground="{DynamicResource Edge.Brush.Text.Primary}"
                 TextAlignment="Center" />
      <TextBlock Text="件"
                 FontSize="13"
                 Foreground="{DynamicResource Edge.Brush.Text.Muted}"
                 HorizontalAlignment="Center" />
    </StackPanel>

    <!-- Row 2: 分割线 -->
    <Border Grid.Row="2"
            Height="1"
            Background="{DynamicResource Edge.Brush.Border.Subtle}"
            Margin="0,0,0,16" />

    <!-- Row 3: 副数据 2 行 -->
    <Grid Grid.Row="3"
          ColumnDefinitions="*,Auto"
          RowDefinitions="Auto,Auto"
          RowSpacing="8">
      <TextBlock Text="白班" FontSize="13"
                 Foreground="{DynamicResource Edge.Brush.Text.Muted}" />
      <TextBlock Grid.Column="1" Text="{Binding DayShiftTotal}"
                 FontSize="18" FontWeight="SemiBold"
                 Foreground="{DynamicResource Edge.Brush.Text.Primary}" />
      <TextBlock Grid.Row="1" Text="夜班" FontSize="13"
                 Foreground="{DynamicResource Edge.Brush.Text.Muted}" />
      <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding NightShiftTotal}"
                 FontSize="18" FontWeight="SemiBold"
                 Foreground="{DynamicResource Edge.Brush.Text.Primary}" />
    </Grid>

  </Grid>
</edge:EdgeCard>
```

### 3.3 4 张卡的 binding 映射

VM **不动**。复用 `DashboardViewModel` 已有 binding。如果某副数据 binding 在 VM 不存在，**禁止新增 VM 字段**，对应副数据位显示 `"—"`。

| 卡 | 标题 | 主数字 binding + 单位 | 副数据 1 | 副数据 2 |
|---|---|---|---|---|
| 今日产量 | 今日产量 | `{Binding TodayOutput}` 件 | 白班 / `{Binding DayShiftTotal}` 或 `"—"` | 夜班 / `{Binding NightShiftTotal}` 或 `"—"` |
| 良率 | 良率 | `{Binding TodayYield}` % | 良品 / `{Binding OkCount}` | 不良 / `{Binding NgCount}` |
| 设备在线 | 设备在线 | `{Binding ConnectedDevices}` | 在线 / `"—"` | 离线 / `"—"` |
| 关键告警 | 关键告警 | `"0"` 条（固定） | 等待处理 / `"—"` | 已处理 / `"—"` |

> codex 在 PR 中**显式列出**哪些 binding 在 VM 不存在、对应位显示"—"，方便后续单独任务补 VM。

### 3.4 第 4 张卡：黑色反色重点卡

"关键告警"卡做反色，作为整页视觉锚点：

| 元素 | 白卡（前 3 张） | 黑卡（第 4 张） |
|---|---|---|
| 卡片背景 | `Edge.Brush.Surface.Card`（白） | `#18201A`（深黑绿）—— 可用 `Edge.Brush.Text.Primary` |
| 标题文字 | `Edge.Brush.Text.Muted` | `#A8B0A5`（70% 白）—— 可新增 Token `Edge.Color.OnDark.Muted` |
| 主数字 | `Edge.Brush.Text.Primary`（黑） | `#FFFFFF`（白） |
| 单位文字 | `Edge.Brush.Text.Muted` | `#A8B0A5` |
| 分割线 | `Edge.Brush.Border.Subtle`（`#DADFD3`） | `#3A4238`（深灰） |
| 副数据标签 | `Edge.Brush.Text.Muted` | `#A8B0A5` |
| 副数据数字 | `Edge.Brush.Text.Primary` | `#FFFFFF` |
| 阴影 | `Edge.Shadow.Card` | 同款 |

**Token 增量**（如果用 DynamicResource）：
- 在 `EdgeTheme.axaml` 新增 `Edge.Color.OnDark.Muted = #A8B0A5`、`Edge.Color.OnDark.Divider = #3A4238`、对应 Brush
- 不重命名现有 Token

### 3.5 告警列表卡

下方告警列表卡（满宽）：

- 卡片：纯白 + 圆角 18 + `Edge.Shadow.Card` + padding 24
- 卡标题区："告警列表" 16px Bold + "暂无稳定告警源" 13px Muted —— 沿用现有 EdgeSectionHeader
- **删除**当前内嵌的浅色 banner 框（看起来像未完成的占位 banner，丑）
- 列表区直接显示 3 行骨架行（不接业务数据）：
  - 每行高 56px，无分割线
  - 每行结构：`●（灰色 8px 点） · "—:—" 时间 · "—" 设备名 · "暂无数据"`
  - 灰色点 `#DADFD3`，所有文字 `Edge.Brush.Text.Muted`
- 底部"查看全部 ›" 13px Muted，hover `Edge.Brush.Text.Primary`

骨架行 XAML 草稿：

```xml
<ItemsControl>
  <ItemsControl.ItemsSource>
    <x:Array Type="x:Object">
      <x:Object /><x:Object /><x:Object />
    </x:Array>
  </ItemsControl.ItemsSource>
  <ItemsControl.ItemTemplate>
    <DataTemplate>
      <Grid Height="56" ColumnDefinitions="Auto,Auto,Auto,*" VerticalAlignment="Center">
        <Ellipse Width="8" Height="8" Fill="#DADFD3" Margin="0,0,16,0" VerticalAlignment="Center" />
        <TextBlock Grid.Column="1" Text="—:—" FontSize="13"
                   Foreground="{DynamicResource Edge.Brush.Text.Muted}"
                   Margin="0,0,24,0" VerticalAlignment="Center" />
        <TextBlock Grid.Column="2" Text="—" FontSize="13"
                   Foreground="{DynamicResource Edge.Brush.Text.Muted}"
                   Margin="0,0,24,0" VerticalAlignment="Center" />
        <TextBlock Grid.Column="3" Text="暂无告警数据" FontSize="13"
                   Foreground="{DynamicResource Edge.Brush.Text.Muted}"
                   VerticalAlignment="Center" />
      </Grid>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

---

## 4. 顺带必做的次要修复

### 4.1 Header 简化（T5 补丁）

`IIoT.Edge.Presentation.Shell/Views/ShellHeaderView.axaml` **删除以下元素**（不动 VM、不删 VM 绑定）：

- Grid.Column="2" 的 `<edge:EdgeStatusChip Status="Running" Text="{Binding HeaderStatus}" />`（运行中绿）
- Grid.Column="3" 的 `<edge:EdgeStatusChip Status="Info" Text="{Binding HeaderMode}" ... />`（本地模式）
- Grid.Column="4" 的 `<edge:EdgeStatusChip Status="Info" Text="{Binding HeaderProfile}" ... />`（产线 chip）
- 右侧 StackPanel 内的铃铛 Border + PathIcon（Data 起始 `M12,4 A4,4...`）

保留：

- 左侧汉堡图标 Border（可选保留或一并删除）
- 标题 TextBlock（绑定 NavRail.SelectedItem.Title）
- 右侧账号区（圆头像 + OperatorName + OperatorCode 竖排 2 行）

**Header 简化后整体观感**：左侧只有"总览" + 右侧只有账号——克制、Apple、参考图 1 的味道。

### 4.2 NavRail 顶部 logo 修正（T4 补丁）

`IIoT.Edge.Presentation.Navigation/Features/Shell/Views/NavigationRailView.axaml` 顶部 logo 容器：

```xml
<Border Width="24" Height="24"
        Background="#FFC400"
        CornerRadius="12"
        Margin="0,20,0,20"
        HorizontalAlignment="Center" />
```

- 尺寸 24×24（不是 40+）
- 完全圆形（CornerRadius 12 = 半径）
- 纯色填充，内部不放任何 PathIcon（参考图 1 那种小圆点感）
- 上下 margin 20px

### 4.3 NavRail "配置"入口图标修正

当前 PathIcon Data:
```
M7,7 L17,7 M7,12 L17,12 M7,17 L17,17 M9,5 L9,9 M15,10 L15,14 M11,15 L11,19
```

这是纯线段，无填充区域，Avalonia PathIcon 默认需要可填充形状才能渲染。

**修正**：改为有填充的齿轮简化图标：

```
M12,8 A4,4 0 1 0 12,16 A4,4 0 1 0 12,8 Z M12,2 L12,5 M12,19 L12,22 M2,12 L5,12 M19,12 L22,12 M4.9,4.9 L7,7 M17,17 L19.1,19.1 M4.9,19.1 L7,17 M17,7 L19.1,4.9
```

或者更稳的——用现成的 PathIcon Data 库（如 Material Symbols 的 settings 图标的简化版本）。

---

## 5. 验收标准（实机截图 ≥ 9/11 通过）

T6-v2 + 次要修复合并提交后，附 1920×1080 实机截图：

- [ ] Dashboard KPI 4 张卡 **2×2 网格**（不是 1×4）
- [ ] 每张卡都有**标题文字**（左上 13px Muted）
- [ ] 主数字字号 **≥ 48px Bold**
- [ ] 每张卡分割线下有 **2 行副数据**（标签 + 数字）
- [ ] 第 4 张卡（关键告警）是 **黑色反色卡**
- [ ] 卡片间距 **≥ 20px**
- [ ] 告警列表不再是套着浅色 banner 框的占位文字
- [ ] Header 右侧不再有 3 个 EdgeStatusChip 和铃铛，只剩账号区
- [ ] NavRail 顶部 logo 是 **24px 圆点**（不是 40+ 大方块）
- [ ] NavRail 配置入口图标正常渲染（不是空白）
- [ ] 整体观感对照参考图 1（`资料/UI例子/30b16364b50d29c34b534459c22322fb.jpg`），有"漂浮感、巨数字震撼、黑卡视觉锚点"

---

## 6. 红线（与 v1.5 §9 一致）

- **VM 不动**：DashboardViewModel、MainWindowViewModel、NavigationItemViewModel 一律不改
- **不接入新业务数据**：副数据缺失就显示 "—"，禁止 mock
- **不引入新 NuGet 包**
- **Launcher / 业务页（MonitorView 等）/ Modules / 业务服务 / PLC / MES / Cloud 链路** 一律不动
- **EdgeKpiCard** 暂时不删（其他页可能还在用），但 R1 Dashboard 不再使用它

---

## 7. 提交方式建议

由于本地是直接在主工作树上改、未做分支隔离（用户授权），建议：

1. T6-v2 + Header 简化 + NavRail logo 修正 + NavRail 配置图标修正 合并成**一个 commit**
2. commit message 建议：`edge: R1 visual rework v2 (dashboard 2x2, header strip, nav logo)`
3. commit 完成后用户立即跑起来对照参考图 1 验收
4. 通过后再决定 PR 走 GitHub 审核

---

## 8. 反思要点（给 codex）

R1 v1 实施暴露的问题：

1. **"用现成控件糊弄"** —— `EdgeKpiCard` 默认外观不符合 v1.5 §5.2 主副分层 C 方案。看到现成控件就用是工程师本能，但 R1 是产品级视觉返工，必须**按规格手工拼 EdgeCard 内部结构**，不要被现成控件框死
2. **"可选项一律保留"** —— v1.5 §4.2 说 EdgeStatusChip "可选保留或隐藏"，R1 的极简方向应该选**隐藏**。看到"可选保留"就习惯性保留是历史包袱思维
3. **"占位用默认控件"** —— 告警列表用 `EmptyStateView` 套个浅色框，看起来像未完成。空态也是设计，必须做**视觉骨架行**
4. **"测试只看 build 通过"** —— v1 实施 build 通过、代码看着合规，但**没自己跑起来看一眼对照参考图**就报告完成。R1 视觉返工的验收标准从 build 通过升级到**实机截图与参考图 1 比对**

下一轮 PR 提交前 codex 必须自评：
- 截图打开放屏幕上
- 参考图 1 (`资料/UI例子/30b16364b50d29c34b534459c22322fb.jpg`) 同时打开
- 两张图对照看："像不像同一个设计语言？"——如果不像，**别提交**，继续改
