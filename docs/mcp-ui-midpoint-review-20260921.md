# UI 重构中期评审与后续路线（2026-09-21 第四轮）

范围：CrashScope 工作区，分支 `feature/local-desktop-work`，HEAD `71c0603`，之上全部未提交的 UI 工作（28 修改 / 2 删除 / 41 未跟踪）。
方法：重新构建、重跑全量 Core 测试、重跑 WPF 冒烟、重跑主题审计与本地化 parity、静态扫描源码、查看本轮隔离渲染截图；对照
`docs/mcp-ui-refactor-progress-20260921.md`（P0–P3）、`docs/mcp-product-experience-plan-20260921.md`（批次 A–E）与
`docs/mcp-ui-refactor-fix-round-20260921.md` 第 8 节的遗留项逐项核对。
边界：未启动产品或测试包、未启停录制、未提交、未修改任何源码或用户配置。全部输出在 `.mcp-probes/midreview-20260921/`，未触碰旧证据与旧包。
本文路径相对于 CrashScope 仓库根目录。

## 一、结论

第三轮修复后的工作树是**干净、可验证、可提交**的：构建 0 警告、495 测试全过、386 项 WPF 冒烟全过、主题审计与 locale parity 无差异，
上一轮验收提出的 6 项建议中 5 项已落实并被自动检查锁住。UI 重构处于"视觉与结构完成、体验批次 A–C1 完成、D/E 未开始"的中点。

当前最大的风险不是视觉，而是工程状态：三天的 UI 工作（连同混入的数据读取契约改动）零提交，且所有验收证据仍是隔离渲染，没有一次真实窗口验收。
下一步应先把已完成的工作固化（分批提交 + 干净基线包），再用一个小批次收掉 P2 尾巴，然后才进入批次 D。

## 二、独立复核结果

| 项 | 结果 | 证据（`.mcp-probes/midreview-20260921/`） |
| --- | --- | --- |
| `dotnet build src/CrashScope.Desktop` Release，隔离 `--artifacts-path` | exit 0，0 warning | `build-desktop.log`、`status.txt` |
| `dotnet test test/CrashScope.Core.Tests` 全量 | 495 通过 / 0 失败 / 0 跳过 | `test-core.log`、`core-tests.trx` |
| `scripts/test-ui-smoke.sh`（`SMOKE_OUT` 隔离） | exit 0，**386 PASS**，27 张截图 | `smoke/ui-smoke.log`、`smoke/screenshots/` |
| `scripts/audit-theme-resources.py` | PASS：无未闭环键，主题 parity 完好 | `audit.log` |
| 本地化 parity | en 443 = zh 443，双向差集为空 | `locale-parity.txt` |
| 源码硬编码颜色 | 主题字典之外仅 `FloatingBarWindow.xaml` 3 处（既定 OSD 设计）、`TrayIconManager.cs` 5 处（GDI 托盘图标）、`ChartPalette.cs`/`SeverityPalette.cs` 各 1 处兜底 | `hardcoded-colors.txt` |
| Git | HEAD `71c0603`（09-20 18:30），其后 0 次提交；`git status --short` 71 条 | `env.txt` |
| 运行中的 CrashScope 进程 | 无 | tasklist |

与 `docs/mcp-ui-refactor-fix-round-20260921.md` 第 6 节的数字逐项一致，结果可复现。

## 三、对照阶段目标

| 目标 | 状态 | 依据 |
| --- | --- | --- |
| P0 视觉样板 / P1 主题与组件基础 | 达到 | 两份纯色板 + `Themes/ControlStyles.xaml`；审计 PASS；`Services/ThemeService.cs` 已含 Dark/Light/System 与 `SystemEvents` 订阅/释放 |
| P2 页面迁移 | 基本达到，剩 code-behind 文案尾巴 | 见第四节第 3 项清单 |
| P3 真实窗口验收 | **未做** | 所有截图为隔离渲染；侧栏录制按钮在截图中为空白（Content 由 code-behind 设置），指标文案无法据截图验收 |
| 批次 A / B / C / C1 | 自动化层面完成 | 各实施文档；本次 386 项冒烟含 28 项编译事件页交互检查 |
| 批次 D（诊断与图表）/ E（全局打磨） | 未开始 | 计划文档末段 |
| 导航 IA：监控 / 诊断 / 工具 / 高级，设置钉底 | 达到 | `MainWindow.xaml:96-184` 四组顺序正确，`NavSettings`（`:226`）在 ScrollViewer 之外；`NavigationLayout.cs` 一致 |
| 首屏预算：1200x750 露出状态 + 三指标 + 2 条事件 | 达到 | `smoke/screenshots/zh-CN-Dark-1200x750-1.00.png`、`zh-CN-Light-1200x750-1.00.png`；冒烟已断言 |
| 上一轮第六节 6 项建议 | 1、3、4、5、6 已处理；**2（分批提交）未处理** | `docs/mcp-ui-refactor-acceptance-review-20260921.md` 第八节 |

## 四、发现的问题（按严重度）

### 1. 高：零提交，且 UI 工作树混入数据层改动

- 71 条脏项全部只存在于工作树；今日 7 个测试包（含 0.9.10）的构建身份均为 `worktree.<hash>`，不能从 Git 重建。
- 混入的非 UI 改动：`src/CrashScope.Core/Monitoring/RecorderEngine.cs`（新增公开 `SampleInterval`，批次 B 的采样间隔阈值依赖）、
  `src/CrashScope.Desktop/AppService.cs`（事件读取结果契约，约 +41/−51 行）、`src/CrashScope.Desktop/IncidentCatalogReader.cs`（新文件）。
  这些是批次 A/B 的数据契约，回退边界应与纯视觉改动分开。
- 建议分 6 批提交，每批可独立回退（顺序即依赖顺序）：
  1. 数据契约：`RecorderEngine.cs`、`AppService.cs`、`IncidentCatalogReader.cs`、`RecorderOperationGate.cs` 及对应 `test/CrashScope.Core.Tests/*`；
  2. 主题与样式基础：`Themes/*.xaml`、`App.xaml(.cs)`、`Services/ThemeService.cs`、`ChartPalette.cs`、`SeverityPalette.cs`、`scripts/audit-theme-resources.py`、`ChartPaletteTests.cs`；
  3. 展示模型与页面（A/B/C）：`IncidentPresentation.cs`、`LiveSamplePresentation.cs`、`OverviewMetricText.cs`、`SidebarRecorderPresentation.cs`、
     `Pages/*`（不含 MainWindow）、`Locales/*.json`、`RecorderStatusPage` 删除；
  4. 导航 IA 与外壳：`NavigationLayout.cs`、`MainWindow.xaml(.cs)`、`FloatingBarWindow.xaml`；
  5. 冒烟工程与脚本：`test/CrashScope.Ui.Smoke/`、`scripts/test-ui-smoke.sh`、`scripts/publish-test-package.ps1`；
  6. 文档：`docs/mcp-*-2026092[01].md`、`docs/mcp-manual-acceptance-checklist.md`。
  `_o.txt` 与 `.mcp-probes/` 不入库。提交后以 `-BaseVersion 0.9.11` 出一个干净基线包替代 worktree 包。

### 2. 中：没有一次真实窗口验收

- 冒烟的 DPI 是渲染比例，不是 per-monitor 切换；侧栏录制按钮、指标文案、跟随系统主题翻转、真实键鼠路由均未在真实窗口验过。
- 这不阻塞提交，但阻塞"可交付"结论。`docs/mcp-manual-acceptance-checklist.md` 第 1 节已更新为 12 路由 + 2 窗口，可直接照做，预计 15–20 分钟。
- 如需远程代做，可在本机启动 0.9.10 测试包并用脚本截真实窗口（不启动录制），但要先获用户确认，因为这会在用户桌面弹出窗口并写入 `%LOCALAPPDATA%\CrashScope\app_settings.json`。

### 3. 中低：P2 尾巴——code-behind 英文与展示细节

本地化 parity 只保证键齐全，覆盖不到 code-behind 拼接的文案。逐文件扫描（`Loc.T/F` 调用数 vs 英文短语字面量）后的残留清单：

| 位置 | 内容 | 用户可见性 |
| --- | --- | --- |
| `AgentActionsViewModel.cs:26,52,62,90,97,104,125` | `Not connected` / `Connected: {pipe}` / `Connect failed:` / `Power plan set` / `Priority set` / `Rolled back` / `Failed:` | 状态栏，可见；`test/CrashScope.Core.Tests/ActionsTests.cs:312` 断言英文，需同批改 |
| `AgentMonitorViewModel.cs:124-164` | 5 处 `InvalidOperationException("Not connected.")` | 异常消息会被 `Failed: {ex.Message}` 拼进状态栏 |
| `Pages/DashboardPage.xaml.cs:486,494,499,571,612` | `N/A`、`GiB total`、`N/A (unavailable or suspect)` | 仪表盘指标与传感器表，可见 |
| `Pages/IncidentsPage.xaml.cs:317-318,377-415` | 对比面板 `N/A`、`No data` 共 9 处 | 事件对比模式，可见 |
| `Pages/DiskHealthPage.xaml.cs:59` | `Recording — waiting for the first storage sample...` | 磁盘页等待态，可见 |
| `RawSensorWindow.xaml.cs:84` | `Recording — the sensor catalog is being built...` | 原始传感器窗口，可见 |
| `MainWindow.xaml.cs:179` | `Mode.ToString() ?? "Unknown"` | 托盘/标题栏摘要，需确认是否走 `IncidentPresentation` 的枚举回退 |
| `Pages/AgentActionsPage.xaml:118-122` | `ComboBoxItem` `Idle/BelowNormal/Normal/AboveNormal/High` | 若是协议枚举值可保留 Tag 为英文、Content 本地化 |

同批可顺手处理的展示细节：

- 页面标题排版不一致：首页 `FontSize=22 SemiBold`（`Pages/HomePage.xaml:11`），其余 11 页 `24 Bold`（`Pages/*.xaml` 各页头）。首页是为首屏预算压的，
  建议其余页跟随首页收到 22/SemiBold 并抽成 `PageTitle` 样式，而不是把首页改回去。
- 事件详情"待确认问题"与"假设"为空时没有空态文案（`Pages/IncidentsPage.xaml.cs:191` 直接给空数组），截图 `fixture-incident-details-compiled.png`
  中该节只剩标题。计划第 5.4 节要求"尚缺信息"显式表达，建议空态写"本次记录未生成待确认问题"。
- 同一列表里并存 `未分类` 与 `未知分类` 两个标签（已知枚举 vs 未知枚举回退），语义对用户几乎无差别，建议回退文案改为"分类信息缺失"一类明显不同的措辞。
- 首页事件行标题同质化：两条都是"异常会话记录 · 未分类"，只能靠时间区分。若 `IncidentInfo` 里有前台进程/会话时长等可靠字段，可进入副标题；没有则保持现状，不臆造。

### 4. 低：`App.Current` 硬转型仍是冒烟的隐患

`App.xaml.cs:27` 新增 `TryCurrent` 后只改了 6 处 `Loaded/Unloaded` 调用点，其余 22 处 `App.Current` 仍是硬转型。产品内无问题，
但下一个在页面生命周期里调用 `App.Current` 的改动会再次让编译页冒烟在 `Loaded` 抛 `InvalidCastException`。
建议在冒烟或审计脚本里加一条约束：`Pages/*.xaml.cs` 的 `OnLoaded/OnUnloaded` 内不得出现 `App.Current.`。

## 五、做得好、应保留的部分

- 三个布局目标（左栏不溢出、高级组在末尾、首屏 2 条事件）都已被 386 项冒烟中的新增 18 项断言锁住，后续回归会直接失败而不是靠看截图。
- 主题为纯色板 + 独立模板层，XAML 零硬编码色，审计脚本可进 CI；跟随系统主题只在 `System` 偏好下订阅并在 `Shutdown` 释放。
- 展示逻辑全部抽为纯类并配测试（`IncidentPresentation`、`LiveSamplePresentation`、`OverviewMetricText`、`NavigationLayout`、`SidebarRecorderPresentation`、`RecorderOperationGate`）。
- 状态语义诚实：缺失用破折号、时间标为"最后记录"并注明来源、读取失败不冒充空列表。
- 冒烟脚本证据目录已参数化，不再覆盖旧批次证据。

## 六、建议路线

| 步骤 | 内容 | 规模 | 出口 |
| --- | --- | --- | --- |
| 0 | 按第四节第 1 项分 6 批提交；以 0.9.11 出干净基线包 | S | `git log` 可重建每个包；`dirtyWorkingTree: false` |
| 1 | P2 收尾小批次：第四节第 3 项清单 + `ActionsTests.cs:312` + `PageTitle` 样式 + 空态文案 + 第 4 项冒烟约束 | S–M | 构建 0 警告、测试全过、冒烟 ≥ 386、parity 相等；code-behind 英文字面量扫描归零（协议枚举除外） |
| 2 | 真实窗口人工验收：`docs/mcp-manual-acceptance-checklist.md` 第 1 节，重点导航/首屏 5 项、真实 DPI、跟随系统翻转 | 15–20 分钟 | 清单逐项打勾；问题回流到步骤 1 或新批次 |
| 3 | 批次 D：先做事件详情重排 + 仪表盘图表语言的**样板页**给用户确认，再实施；图表实施前核对数据缓存与刷新成本 | L | 样板认可；切换主题不丢历史；相关交互回归 |
| 4 | 批次 E：字号/弹层/焦点/状态保存的全页矩阵 | M | 全页状态矩阵 + 性能基线对比 |

步骤 0 与 1 顺序可互换，但都应在步骤 3 之前完成：D 会大面积改 `IncidentsPage` 与 `DashboardPage`，
在一个零提交、带英文残留的基线上开工会让后续回退与验收都变难。

## 七、边界

本次未启动产品或测试包、未启停录制、未提交、未打包、未修改源码或用户配置。构建、测试与冒烟输出均在 `.mcp-probes/midreview-20260921/`，
未覆盖既有证据。截图评价基于隔离渲染，不替代真实窗口验收。
