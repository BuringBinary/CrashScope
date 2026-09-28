# UI 重构验收评审（独立复核）

日期：2026-09-21（17:25–17:40 复核）
范围：CrashScope 工作区，分支 feature/local-desktop-work，HEAD 71c0603 之上的全部未提交 UI 工作。
方法：重新构建、重跑全量测试与主题审计、重跑 UI smoke、查看隔离渲染截图、静态扫描源码；不是复述既有文档。未启动产品、未启停录制、未提交、未打包、未修改任何源码。
本文路径相对于 CrashScope 仓库根目录。

## 一、结论

方向和基础设施成立，质量不错；但对照项目自己定义的验收要求，尚未达到"可验收 / 可交付"。

- P0 视觉样板、P1 主题与组件基础：达到。
- P2 页面迁移：约三分之二；Agent 三页与部分 code-behind 文案未迁移。
- P3 视觉与工程验收：未达到。只有隔离渲染证据，真实窗口、实际 DPI、逐页冒烟、跟随系统主题端到端均未做。
- 体验计划 A/B/C/C1：自动化层面完成；D/E 未开始。
- 阻断项不在视觉：唯一的 WPF 自动冒烟门禁今天下午已损坏、全部工作零提交、最新三个测试包版本号倒退为 0.9.2。

## 二、独立复核结果

| 项 | 结果 | 证据 |
| --- | --- | --- |
| `dotnet build src/CrashScope.Desktop` Release | 0 错误、0 警告 | `.mcp-probes/review-20260921/build-desktop.log` |
| `dotnet test test/CrashScope.Core.Tests` 全量 | 495 通过 / 0 失败 / 0 跳过 | `.mcp-probes/review-20260921/test-core.log`、`core-tests.trx`（仅既有 `RpcTests.cs:151` CS1998） |
| `scripts/audit-theme-resources.py` | PASS：主题 parity 42 = 42，正文/徽标对比度全部达标，无视觉孤岛，无事件泄漏 | `.mcp-probes/review-20260921/audit.log` |
| 本地化 parity | en 442 = zh 442，双向差集为空 | `.mcp-probes/review-20260921/summary2.txt` |
| XAML 硬编码颜色 | 仅 `FloatingBarWindow.xaml` 4 处（已定案为有意 OSD 设计） | ripgrep |
| `scripts/test-ui-smoke.sh` | **exit 1**，`InvalidCastException`，无任何检查计数输出；26 张源 XAML 截图仍生成 | `.mcp-probes/review-20260921/ui-smoke.log`、`screenshots/` |
| Git | HEAD 71c0603（09-20 18:30），其后 0 次提交；27 修改 / 2 删除 / 41 未跟踪 | `.mcp-probes/review-20260921/env.txt` |
| 运行中的 CrashScope 进程 | 无 | tasklist |

查看的截图：`zh-CN-Dark-1200x750-1.00.png`、`zh-CN-Light-900x600-1.00.png`、`en-US-Dark-900x600-1.00.png`、`fixture-recording-zh-CN-Dark.png`。

## 三、对照要求逐项

需求来源：`docs/mcp-ui-refactor-progress-20260921.md`（P0–P3）、`docs/mcp-product-experience-plan-20260921.md`（批次 A–E）、`docs/mcp-navigation-ia-refactor-20260921.md`。

| 要求 | 状态 | 依据 |
| --- | --- | --- |
| P0：精致专业、石墨灰底、克制紫强调、无发光装饰，正常/未录制/异常三态 | 达到；用户已反馈"还不错" | 截图；`Themes/DarkTheme.xaml`、`LightTheme.xaml` |
| P1：修正 ARGB、统一语义色/间距/字号/圆角、补齐选中/悬停/按下/禁用/焦点、深浅切换无残留、小窗口导航可达 | 基本达到 | 两份纯色板各 42 键；`Themes/ControlStyles.xaml`；审计 PASS；`MainWindow.xaml:96` ScrollViewer |
| P2：仪表盘、事件、硬件、磁盘、工具箱、设置、辅助窗口逐页迁移 | 部分。Dashboard 图表主题、Incidents、Timeline、Settings 已接 ThemeChanged；Agent 三页仍有旧色板与英文硬编码；Hardware/DiskHealth/SpeedTest 只做了资源键迁移 | 见第四节第 6 项 |
| P3：中英 × 深浅 × 100/125/150 DPI × 默认/最小窗口；真实窗口；构建/诊断/测试通过；独立候选包 | 未达到。位图渲染比例不是真实 DPI；真实键鼠、逐页冒烟、跟随系统主题端到端未做；smoke 门禁当前失效 | 各实施文档"仍需现场验收"段；本次 smoke 结果 |
| 批次 A：事件可读与直达 | 自动化完成（30 测试 + 28 编译页交互检查）；真实窗口路由未验 | `docs/mcp-experience-a-implementation-20260921.md` |
| 批次 B：状态可信 | 自动化完成（42 测试） | `docs/mcp-experience-b-implementation-20260921.md` |
| 批次 C/C1：首页与导航减负 | 结构完成；首屏与导航两个布局目标未达 | 第四节第 4、5 项 |
| 批次 D、E | 未开始 | 计划文档末段 |
| 导航 IA：6 组压 4 组，录制器并入首页，侧栏卡瘦身 | 代码达成；组顺序与文档不一致 | `NavigationLayout.cs`、`MainWindow.xaml:133-179` |

## 四、发现的问题（按严重度）

### 1. 高：WPF 冒烟门禁已坏，最近四轮改动没有任何 WPF 级验证

- 现象：`scripts/test-ui-smoke.sh` exit 1。`InvalidCastException: Unable to cast object of type 'System.Windows.Application' to type 'CrashScope.Desktop.App'`，位置 `App.get_Current()`（`src/CrashScope.Desktop/App.xaml.cs:21`）← `IncidentsPage.OnLoaded`（`src/CrashScope.Desktop/Pages/IncidentsPage.xaml.cs:57`）。
- 根因：`App.Current => (App)Application.Current` 是硬转型。`docs/mcp-ui-work-review-20260921.md` 第十一节的主题孤岛修复在 `IncidentsPage.OnLoaded` 中新增 `App.Current?.Theme`，`?.` 保护的是结果而不是转型本身；smoke 夹具托管的是裸 `Application`，转型即抛。
- 时间线：`.mcp-probes/experience-c1-20260921/ui-smoke.log`（10:17）为 368 项 PASS，含 28 项编译事件页检查；当前失败发生在其后。`docs/mcp-navigation-ia-refactor-20260921.md` 6.1 用 `git stash` 证明"与本次重构无关"成立，但推断为"夹具既有局限"不成立：stash 不回退未跟踪文件，且 C1 时该检查是通过的。它是同日第十一节修复引入的回归。
- 后果：主题孤岛修复、跟随系统主题、录制器页删除、侧栏瘦身四轮改动都未通过过 WPF 冒烟，而它们改的正是 MainWindow 与页面生命周期。
- 修法（小）：`public static new App? Current => Application.Current as App;` 并让调用点容忍 null；或在夹具中托管真正的 `App` 子类。修复后重跑应恢复不少于 368 项。

### 2. 高：全部 UI 重构工作零提交

- HEAD 仍为 71c0603。27 个修改、2 个删除、41 个新文件（10 个新源文件、12 个新测试文件、smoke 工程、14 份文档）全部只存在于工作树。
- 今天 6 个测试包的构建身份均为 `worktree.<hash>`，不能从 Git 重建。`docs/mcp-docs-chain-review-20260919.md` R1 已将同类情况列为最高风险，现再次累积。
- 建议按可独立回退的边界分批提交：主题基础与 smoke 工程 → A → B → C/C1 → 主题孤岛与跟随系统 → 导航 IA 与侧栏瘦身 → 文档。

### 3. 中高：最新三个测试包版本号倒退为 0.9.2

- `artifacts/test-packages/20260921-155748`、`161842`、`163220` 的 `verification-result.json` 版本均为 `0.9.2-test.*`；上午 A–C1 为 0.9.6–0.9.9。原因是 `scripts/publish-test-package.ps1:10` 默认 `$BaseVersion = '0.9.2'`，这三次未传参。
- 后果：最新包（含导航重构与侧栏瘦身）显示比 0.9.9 更旧；journal 版本 `0.9.2.0` 与 2026-09-18 真实游戏会话的 0.9.2 撞号，后续按版本追溯事故数据会混淆。`docs/mcp-test-package-guide-20260921-155748.md` 照录了该版本号但未指出问题。
- 建议：下一包显式 `-BaseVersion 0.9.10`，并修改脚本默认值。

### 4. 中：导航在默认 1200×750 已溢出，"高级 / Agent"位置与设计不一致

- 截图：1200×750 下侧栏已出现滚动条，"工具"组第三项（网速测试）被裁掉；900×600 下整个"工具"组不可见。
- `src/CrashScope.Desktop/MainWindow.xaml:133-179` 将折叠组"高级 / Agent"放在"故障诊断"与"工具"之间；`docs/mcp-navigation-ia-refactor-20260921.md` 第 3 节写的是 监控 / 诊断 / 工具 / 高级。
- 与体验计划"常用导航不依赖猜测或找到隐藏的折叠项"冲突。建议高级组移至最后，并压缩组标题与条目间距（当前每项约 44 px）。

### 5. 中：首页首屏目标未达

- 体验计划 5.1：默认 1200×750、滚动归零时应看到状态、三项指标与至少 2 条事件。截图中"最近事件"卡片只露出标题行，0 条事件可见，内容区出现滚动条。
- 页头（眉题 + 大标题 + 副标题约 90 px）与录制卡（约 250 px）偏高。`docs/mcp-experience-c-implementation-20260921.md` 把目标表述为"最近事件入口可见"，与计划原文不一致。

### 6. 中低：P2 尾巴，Agent 三页未迁移

- `src/CrashScope.Desktop/Pages/AgentMonitorPage.xaml.cs:95` 仍为 `Color.FromRgb(0x00, 0xD6, 0x8F)`（旧色板绿，浅色卡片上对比度约 1.9）。`docs/mcp-ui-work-review-20260921.md` 11.3 称"剩余 8 处全是 Transparent / Gray 兜底"漏计了此处。
- `AgentMonitorPage.xaml.cs:49,102`、`src/CrashScope.Desktop/AgentActionsViewModel.cs:52,125`：`Connected:`、`Last update:`、`Failed:` 英文硬编码。本地化 parity 只能保证键齐全，不覆盖 code-behind 拼接文案。

### 7. 低：证据与文档卫生

- `scripts/test-ui-smoke.sh:7` 仍硬编码 `LOG=.mcp-probes/experience-b-20260921`；今天 16:31 的失败运行已覆盖 B 批次 347 项 smoke 证据，该目录 `ui-smoke.log` 现为异常栈。
- `docs/mcp-manual-acceptance-checklist.md` 仍列出已删除的 RecorderStatus 页及 15 页清单，应更新为 12 个路由 + 2 个独立窗口。
- 截图夹具局限：默认态侧栏录制按钮为空白（Content 由 code-behind 设置）；录制态 fixture 指标有值而副标题仍为"未录制"。二者是夹具伪影，但说明现有截图不能用于验收侧栏按钮与指标文案。

## 五、做得好、应保留的部分

- 主题瘦身为纯色板，模板独立到 `Themes/ControlStyles.xaml`；XAML 零硬编码色；主题键全部 `DynamicResource`。
- 展示逻辑抽成纯类并配测试：`OverviewMetricText`、`IncidentPresentation`、`LiveSamplePresentation`、`IncidentCatalogReader`、`NavigationLayout`、`SidebarRecorderPresentation`、`RecorderOperationGate`。
- 审计脚本经过故障注入反向验证，可进 CI。
- 状态语义诚实：缺失用破折号、时间标为"最后记录"、支持度不冒充概率、读取失败不冒充空列表。
- 文档边界克制，基本没有夸大。

## 六、建议顺序

1. 修复 `App.Current` 安全转型，重跑 `scripts/test-ui-smoke.sh`，确认恢复不少于 368 项；同时把脚本输出目录参数化。
2. 分批提交全部工作。
3. 以 `-BaseVersion 0.9.10` 重新打包，并更新测试包说明。
4. 导航：高级组移至最后、压缩间距；首页：降低页头与录制卡高度，目标为 1200×750 露出 2 条事件。
5. 清理 Agent 三页的旧色板与英文硬编码。
6. 更新 `docs/mcp-manual-acceptance-checklist.md` 后做一次真实窗口人工验收：逐页冒烟、停留切主题、跟随系统翻转、实际 DPI、中英文长文案。

## 七、边界

本次未启动产品或测试包、未启停录制、未提交、未打包、未修改源码或用户配置。构建、测试与 smoke 输出均在 `.mcp-probes/review-20260921/` 隔离目录，未覆盖既有证据。截图评价基于隔离渲染，不替代真实窗口验收。

## 八、处理记录（同日第三轮）

第六节第 1、3、4、5、6 项已按顺序处理，第 2 项（分批提交）仍待用户确认后执行。
逐项改动、验证结果（构建 0/0、495 测试、审计 PASS、冒烟 386 PASS、locale 443=443）
与 0.9.10 测试包信息见 `docs/mcp-ui-refactor-fix-round-20260921.md`；
人工验收清单已同步更新为 12 路由 + 2 窗口并加入首屏/导航项。
