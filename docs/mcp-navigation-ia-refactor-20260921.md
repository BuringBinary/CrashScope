# 导航信息架构重构：录制器并入首页、分组由 6 压到 4

日期：2026-09-21
触发：用户反馈「首页和录制器功能是不是重复了，左边的功能栏用着有点费劲」

## 1. 问题确认（重构前）

### 1.1 同一个动作有三个入口

「开始录制」这一个动作，在同一屏里出现三次，且三套文案互不一致：

| 位置 | 文案键 | 中文 |
|---|---|---|
| 首页状态卡 | `common.start` | 开始 |
| 录制器页 | `recorder.buttons.startRecording` | 开始录制 |
| 侧边栏常驻卡 | `sidebarUi.startRecording` | 开始记录 |

同理，录制状态文本也有三套独立实现（`overviewUi.stopped` / `recorder.status.stopped` / `sidebarUi.stopped`），
三处各自刷新，是三份需要同步维护的状态机。

### 1.2 首页内容全部是别处的镜像

| 首页区块 | 已有专页 |
|---|---|
| 录制状态 + 开始/停止 | 录制器页、侧边栏常驻卡 |
| CPU / GPU / 内存卡片 | 仪表盘 |
| 最近事件列表 | 事件页 |

首页没有任何独占内容，是一个纯聚合入口，而它聚合的每一项都另有专页。

### 1.3 导航项数与分组密度双超标

13 个顶层项分 6 组，平均每组 2.2 项。微软 NavigationView 指引建议左侧导航承载
5-10 个同等重要的顶层项，并明确「层级尽量浅，两层最理想」；通用 IA 实践建议顶层
6-8 项封顶。只含 1-2 项的分组不提供结构，只增加扫视成本。

此外「实时监控」既是分组名又是组内条目名，同一个词在同一列表里指代两个不同层级。

## 2. 决策

| 项 | 决定 |
|---|---|
| 首页 vs 录制器 | 录制器**并入**首页，删除录制器页与 `Recorder` 路由 |
| 分组 | 6 组压到 4 组：监控 / 诊断 / 工具 / 高级 |
| 「实时监控」重名 | 组名改用 `navGroups.monitoring`，首页归入该组，重名消解 |
| 侧边栏常驻录制卡 | **保留**。它是「每页可达」的设计（代码注释已写明），并成为录制的唯一常驻控制点 |

录制入口收敛后为两处，且职责不同：侧边栏卡片 = 任何页面都能触达的常驻控制；
首页 = 完整状态与会话详情。不再有第三套。

## 3. 新导航结构

```
监控          Home / Dashboard / LiveMonitor
诊断          Incidents / Timeline
工具          Hardware / DiskHealth / SpeedTest
高级（可折叠） AgentMonitor / AgentIncidents / AgentActions
─────────────────────────────────────────
设置（底部固定，不参与滚动）
```

顶层项 13 -> 12，分组 6 -> 4，每个非固定分组至少 2 项。

## 4. 录制器页内容的去向

删除该页前，逐项确认其内容均有着落，无信息丢失：

| 原录制器页内容 | 去向 |
|---|---|
| 开始/停止录制 | 首页状态卡（已有）+ 侧边栏常驻卡（已有） |
| 录制状态 + 状态点 | 首页状态卡（已有） |
| 运行时间 / 采样数 / 会话 ID | **新增**到首页状态卡下方计数行 |
| 打开数据目录 | **新增**到首页操作按钮行 |
| 数据目录 / 管理员权限 | 首页「会话与运行环境」折叠区（已有） |
| Agent 版本 | 首页 `ElevationText` 已包含，未重复添加 |
| 上次会话（含 clean/unclean 着色） | **新增**到首页折叠区 |
| 写入吞吐量 | **新增**到首页折叠区，改为由真实 `SampleInterval` 计算 |

最后一项顺带修掉一个既有缺陷：录制器页的吞吐量卡片（`1 Hz` / `0.5 Hz` 等）是写死在
XAML 里的静态占位，code-behind 从未赋值，即无论实际采样频率如何都显示固定数字。
迁移后改为 `1 / SampleInterval.TotalSeconds` 实算。

## 5. 改动清单

| 文件 | 改动 |
|---|---|
| `NavigationLayout.cs` | 删 `Workspace` 组与 `Recorder` 条目；`Home` 改属 `Monitoring`；兜底组改 `Monitoring` |
| `MainWindow.xaml` | 删 `NavRecorder`；删 `navGroups.overview` 组标题；`Home` 并入监控组 |
| `MainWindow.xaml.cs` | 删路由分支 `"Recorder" => new RecorderStatusPage()` |
| `Pages/HomePage.xaml` | 新增计数行、打开数据目录按钮、上次会话与吞吐量两行 |
| `Pages/HomePage.xaml.cs` | 计数刷新接入 `RefreshRecorder`；上次会话与采集频率接入 `RefreshDataRoot`；新增 `OpenDataFolder_Click` |
| `Pages/RecorderStatusPage.xaml{,.cs}` | **删除** |
| `Locales/{en-US,zh-CN}.json` | 删除 10 个孤儿键，parity 452 -> 442 |
| `NavigationLayoutTests.cs` | 删 `Recorder` 用例；新增 3 项（路由退役、分组最小基数、Home 归属） |
| `Ui.Smoke/Program.cs` | 路由断言去掉 `NavRecorder`；新增反向断言确认其确实不存在 |

## 6. 验证结果

| 项 | 结果 |
|---|---|
| `dotnet build` Desktop Debug | 0 error / 0 warning |
| `dotnet test` Core.Tests | **495 通过 / 0 失败**（原 493；新增 3、删除 1 个 InlineData） |
| `audit-theme-resources.py` | PASS，主题 parity 完好，无视觉孤岛，无事件泄漏 |
| locale parity | en 442 = zh 442，双向差集为空 |

### 6.1 关于 UI 冒烟（`scripts/test-ui-smoke.sh`）

该脚本**退出码 1**，但**与本次重构无关**，已证伪：将本次全部改动 `git stash` 后，
在干净工作树上重跑，得到完全相同的失败与堆栈：

```
System.InvalidCastException: Unable to cast object of type 'System.Windows.Application'
                             to type 'CrashScope.Desktop.App'.
   at CrashScope.Desktop.App.get_Current() in App.xaml.cs:line 21
   at CrashScope.Desktop.Pages.IncidentsPage.OnLoaded(...) in IncidentsPage.xaml.cs:line 57
```

成因是冒烟夹具用裸 `Application` 托管控件树，而 `IncidentsPage.OnLoaded` 会取
`App.Current.Service`，强制转型失败。属夹具既有局限，非本次引入，留作独立开放项。

## 7. 需要人工确认的点

1. 左栏扫视是否变轻松：4 个组标题，最长一组 3 项。
2. 首页是否已能独立完成「看状态 -> 开始/停止 -> 打开数据目录」全流程，无需再找录制器页。
3. 开始录制后，首页计数行的运行时间/采样数/会话 ID 是否每秒跳动。
4. 展开首页「会话与运行环境」，确认上次会话与写入吞吐量两行有真实值
   （吞吐量应随设置里的采样间隔变化，而非恒为 `1 Hz`）。
5. 上次会话为非正常关闭时，该行应显示为警示色。
6. 中英文切换后，左栏无截断、无残留的「录制器」条目。

## 8. 追加：侧边栏录制卡瘦身（同日第二轮）

### 8.1 用户追问与核实结论

用户问「侧边栏这张卡是不是可以去掉，首页不是有了吗」。核实后结论是**不能删，但要瘦身**。

关键区别在于**可见范围**：首页那个按钮只在首页可见，而应用有 12 个页面。
核实全仓库能真正触发录制的入口，只有三个：

| 入口 | 可见范围 |
|---|---|
| 首页 `StartStopButton` | **仅首页** |
| 侧边栏 `SidebarRecordButton` | 所有页面常驻 |
| 托盘右键菜单 | 需右键托盘图标，隐蔽 |

顺带核实：悬浮条（`FloatingBarWindow`）只显示网速，不含录制控制，因此不存在三重复。

删除侧边栏卡的代价：在时间线页发现异常想立刻停止录制，必须先切回首页再点停止——
一个高频且时间敏感的操作被加了一跳。

### 8.2 真正的毛病在视觉权重

用户的感受是真实的，问题不在「存在」而在「太重」：

- 使用了与首页主操作相同的实心 `AccentButton`，两个紫色主按钮同屏争夺焦点
- 标题 + 状态点 + 状态文字 + 说明文字 + 大按钮，占据整块侧边栏底部
- 文案与首页不统一（「开始记录」vs 首页「开始」）

### 8.3 改法：压成一行

```
改前（5 行，带卡片边框）        改后（1 行 + 分隔线）
┌──────────────────┐
│ 记录状态          │          ────────────────────
│ ● 未录制          │    ->    ● 未录制        [▶]
│ 开始记录后显示... │
│ [ ▶ 开始记录 ]   │
└──────────────────┘
```

| 项 | 改动 |
|---|---|
| 卡片边框与 `CardBg` 背景 | 去掉，改为一条 `BorderColor` 细分隔线划出边界 |
| 「记录状态」标题 | 去掉，并入整行 ToolTip |
| 说明文字 `SidebarHintText` | **控件删除**，文案并入整行 ToolTip（信息不丢失） |
| 按钮样式 | `AccentButton`/`DangerButton` 实心 -> `SecondaryButton` 描边 |
| 按钮内容 | 图标 + 文字 -> **仅 MDL2 图标**，完整文案进 ToolTip 与 `AutomationProperties.Name` |
| 停止态区分 | 实心红底 -> 图标前景色 `DangerColor` |
| 错误行 `SidebarErrorText` | 保留，仅在录制器真的故障时出现 |

图标提取用 `IconOnly()`：仅当首字符落在私用区 `U+E000..U+F8FF` 时截取首字符，
否则原样返回。`IconTextTemplate` 在标签为空时会自动去掉左边距，正好支持纯图标按钮。

无障碍未削弱：按钮有 `AutomationProperties.Name`，整行有 ToolTip，屏幕阅读器可读。

### 8.4 验证

| 项 | 结果 |
|---|---|
| `dotnet build` Desktop | 0 error / 0 warning |
| `dotnet test` | 495 通过 / 0 失败 |
| 主题审计 | PASS |
| locale parity | en 442 = zh 442 |
| 打包 | **PACKAGE VERIFIED**，包内回归 408 通过 |
| 启动冒烟 | MainWindow shown，22s 存活 |
| 渲染截图 | 900x600 实测：分隔线独立成行，未遮挡任何导航条目 |

`Ui.Smoke/Program.cs` 同步更新：`SidebarHintText` 断言改为 `SidebarStateText`，
新增反向断言确认 hint 控件确实已退役、`SidebarRecorderRow` 存在。

### 8.5 需要人工确认（追加）

7. 侧边栏底部应为一行「● 状态 [图标按钮]」，上方一条细分隔线，不再是一张卡片。
8. 悬停该行应出现 ToolTip，内容为「记录状态: <状态>」加提示文字。
9. 点图标按钮能正常开始/停止；录制中图标应为停止符且呈红色。
10. 在非首页（如时间线页）确认该行仍在，且能一键停止录制。
11. 深浅两个主题下，分隔线与图标按钮都应清晰可见。

## 9. 勘误：第 6.1 节的冒烟结论不成立（同日第三轮）

第 6.1 节称冒烟失败"与本次重构无关、属夹具既有局限"。同日验收复核
（`docs/mcp-ui-refactor-acceptance-review-20260921.md` 第一节）推翻了这一结论：

- C1 批次 10:17 的 `ui-smoke.log` 是 **368 PASS**，说明当天早些时候冒烟是通过的，
  失败是同日引入的回归，不是既有局限。
- 失败点 `IncidentsPage.OnLoaded` 取 `App.Current?.Theme` 订阅主题切换，而 `App.Current`
  是 `(App)Application.Current` 硬转型，在裸 `Application` 托管下抛 `InvalidCastException`；
  `?.` 对硬转型无效。`git stash` 复现相同堆栈，是因为主题订阅那几行属于更早一批未提交改动，
  stash 后仍在工作树的基线里。

处理：`App` 新增 `TryCurrent`（`Application.Current as App`），六处主题订阅改用它；
冒烟恢复为 386 PASS（含本轮新增 18 项首屏/导航断言）。另外第 3 节声明的分组顺序
（高级组最后）此前并未在 `MainWindow.xaml` 中落实，本轮已将高级组移到工具组之后。
全部细节与验证见 `docs/mcp-ui-refactor-fix-round-20260921.md`。
