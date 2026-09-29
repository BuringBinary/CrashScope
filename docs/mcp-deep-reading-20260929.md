# CrashScope 深度阅读记录（2026-09-29）

> 范围：`feature/local-desktop-work`，HEAD `f026a3d`，另有两轮修复未提交。本轮通过 Bridge 读取了 `src/`、`test/`、`scripts/`、`docs/` 下的 269 个文本文件，逐个按 sha256 核对，268 个一致（`docs/validation-plan.md` 未核对通过，内容可读）。另外读取了 Git 状态和差异。未构建、未运行测试、未启动程序、未修改源码；只在已被忽略的 `.mcp-probes/deep-read-20260929/` 下生成了 Git 状态和差异快照，并新增本文档。除 N1 用等价正则做过模拟验证外，下文结论都是读代码得出的推断，尚未实际复现。

## 1. 规模与现状

| 部分 | 文件 | 行数 |
| --- | --- | --- |
| `src/CrashScope.Core` | 62 | 8,982 |
| `src/CrashScope.Desktop` | 66 | 11,646 |
| `src/CrashScope.Agent` / `Service` / `Contracts` | 7 | 560 |
| `test/CrashScope.Core.Tests` | 45 | 5,883 |
| `test/CrashScope.Ui.Smoke` | 5 | 655 |
| `scripts/` | 11 | 847 |
| `docs/` | 69 | 7,513 |

- 9/28 已完成五批提交（`c5faf90`..`f026a3d`），本地比远端领先 15 个提交，尚未推送。
- 未提交改动：20 个修改、6 个未跟踪，对应 `docs/mcp-ui-empty-state-font-fix-20260928.md` 和 `docs/mcp-agent-layout-localization-fix-20260928.md` 两轮修复，以及 0.9.11 验收记录。

## 2. 架构速览

1. **采集**：`RecorderEngine` 每秒执行一次 tick，各采集器按 1/2/5/10/30 秒的节拍写入约 16 类 JSONL；`CrashSafeJsonlWriter` 使用 WriteThrough，每条记录都会 fsync。
   - 按系统提交内存（commit）占比分三级降级：90% 预警、95% 限流（同时停止 ETW）、98% 危急（只保留 GPU 温度、commit、前台切换和 2004 事件）。
   - GPU ETW 默认关闭；System ETW 需要管理员权限。
2. **会话状态**：`state/last-session.json` 用临时文件加改名的方式原子写入，每 5 秒一次心跳；`state/recorder.lock` 的独占句柄保证同一数据目录只有一个录制器。
3. **恢复**：下次开始录制或桌面程序启动时，上次未正常结束的会话先进入 `recovery-pending/`，再依次收集事件日志、各文件尾部、WER/LiveKernelReports/Minidump，经 staging 目录发布为 `incidents/recovered-<id>`，最后写入完成回执。中途失败会保留任务，下次重试。
4. **分析**：`IncidentAnalyzer` 依次调用 `HypothesisEvaluator`（6 类假设，输出证据支持指数，不是概率）、`TimelineBuilder` 和 `ReportGenerator`；另有跨事件分析和推荐引擎。
5. **界面与控制**：
   - Desktop 直接内嵌 `RecorderEngine`。
   - Agent 和 Service 通过命名管道 RPC 对外提供数据：管道只允许当前用户连接，消息按长度前缀分帧并限制大小，连接先握手（Hello/Welcome）。
   - 动作平面可修改电源计划和进程优先级：必须填写原因、写审计日志、可回滚，且不允许设为 RealTime 优先级。
6. **数据保留**：默认保留 30 天、总量 512 MiB，会话和事件各占一半；桌面启动时和录制器每 24 小时各执行一次。

设计上的强项：崩溃安全的写入顺序、带回执的持久恢复队列、数据目录租约、观察者异常隔离、按 commit 压力降级、tick 分阶段计时、采集健康状态可见、保护状态读不出时拒绝清理、RPC 帧大小受限且只允许当前用户。中英文资源各 494 个键，一一对应，占位符也一致。

## 3. 本轮新发现

| 编号 | 严重度 | 位置 | 问题 | 依据 |
| --- | --- | --- | --- | --- |
| N1 | P1 隐私 | `src/CrashScope.Desktop/ExportRedactor.cs:36-37,89` | 用户目录的正则只能匹配单反斜杠路径。JSON 文件里的路径是转义后的 `C:\\Users\\<用户>`，匹配不上，所以“已脱敏”的导出中，`previous-session.json`、`analysis.json` 里的 `sessionDirectory` 仍会带出用户名 | 用等价正则模拟，JSON 文本原样未被替换；`DiagnosticExportRedactionTests` 只覆盖了单反斜杠的情况 |
| N2 | P1 隐私 | `src/CrashScope.Desktop/DiagnosticExportService.cs:119-123` | `summary.txt` 的“待确认问题”一节没有经过 `Redact`，而这些问题里可能包含采集详情或进程名；与同一文件里“进程名已匿名化”的声明不符 | 代码直读 |
| N3 | P1/P2 隐私 | `ExportRedactor.Redact` | 对“域\用户名”格式只替换了机器名部分（例如 TerminalServices 事件中的 `User` 字段），IPv6 地址也没有处理 | 依据事件格式推断，待构造样本验证 |
| N4 | P1 诊断可信度 | `src/CrashScope.Core/Diagnostics/CrashEvidenceCollector.cs:51-53,60`；`src/CrashScope.Core/Analysis/HypothesisEvaluator.cs:187-198` | 证据窗口是“最后心跳前 10 分钟”到“执行恢复的时刻”，而 6008、WHEA、4101、应用崩溃、WER 等 Windows 证据在评估时不再按时间过滤。如果隔了几天才打开程序，这期间无关的后续事件也会被算进本次事故的假设 | 代码直读 |
| N5 | P1 诊断可信度 | `src/CrashScope.Core/Monitoring/RecorderEngine.cs:679-683`；`src/CrashScope.Desktop/AppService.cs:118-126,157-160`；`src/CrashScope.Desktop/App.xaml.cs:172` | 退出程序或注销时最多只等 3 秒停止录制。超时后进程直接结束，会话不会被标记为正常结束，下次启动时会被当作异常会话，生成一条误报事件 | 代码路径推断，未实测 |
| N6 | P2 可靠性 | `RecorderEngine.cs:358-363`；`src/CrashScope.Core/Monitoring/HardwareMonitor.cs:57-64`；`src/CrashScope.Core/Monitoring/CollectorHealthTracker.cs:18-28` | tick 中任何异常都会让录制器停止（Faulted），直到手动重启。其中传感器读取（LHM Update）和健康文件原子写都没有单独捕获异常；如果显卡驱动重置时 LHM 抛出异常，录制恰好会在最关键的时刻中断 | 推断，需要确认 LHM 实际行为 |
| N7 | P2 性能 | `RecorderEngine.cs:443-445,648-660` | 健康详情里带有每秒都在变化的 commit 字节数和 CPU 占用，导致 `collector-health.json` 几乎每秒都被原子重写并 fsync；加上 JSONL 每秒至少 3 次 fsync。README 写的是“内存压力每 5 秒采集一次”，实际代码每秒都写 | 代码直读；长测中 CPU p95 为 0.82%，可以接受，但 I/O 放大和异常面都偏大 |
| N8 | P2 数据保留 | `src/CrashScope.Core/Infrastructure/RetentionService.cs:52-71,173-174` | 会话预算约 256 MiB，按约 48 MiB/h 的写入量只能留约 5 小时。一次长会话本身受保护，但会挤掉其余所有旧会话；30 天规则也会删除事件。9/18 的游戏会话和 2 条事件预计在 10/18 之后首次启动时被清理（已有两份备份） | 代码直读，结合 9/27 的数据量 |
| N9 | P2 工程 | `scripts/publish-test-package.ps1:188`；`.gitignore` 中的 `test-*.txt` | 出包脚本依赖的 `scripts/test-package-readme.zh-CN.txt` 没有进入 HEAD，从干净检出无法出包。0.9.11 的 `dirtyWorkingTree=false` 并不能说明它可以完全从提交复建。未提交改动中已包含修复 | `git ls-files` 中没有该文件 |
| N10 | P2 展示 | `src/CrashScope.Desktop/Pages/SettingsPage.xaml:299` | “关于”页的版本号写死为 `v0.5.0-dev`，与 0.9.11 不符 | 代码直读 |
| N11 | P3 | `src/CrashScope.Core/Monitoring/SessionJournal.cs:50-60`；`RecorderEngine.cs:159` | `last-session.json` 损坏时按设计拒绝开始录制，但界面上没有隔离或修复入口，用户会一直无法录制 | 代码直读 |
| N12 | P3 | `RecorderEngine.cs:662-673`；`src/CrashScope.Core/Monitoring/DwmStateTracker.cs`；`src/CrashScope.Desktop/Pages/AgentMonitorPage.xaml.cs:22` | ETW 因内存压力停止后，本会话内不会自动恢复；DWM 在 Win8 及以后始终开启，所以“软件渲染回退”这个信号实际不可能触发（早先已记为 M4/F1），但 README 仍宣称支持；Agent 页用同步的 `Dispatcher.Invoke` 切回界面线程 | 代码直读 |

待实测：Service 以 LocalSystem 运行时，“仅当前用户”的管道是否会挡住普通用户的 Desktop 连接；已提权的 Agent 与未提权的 Desktop 之间能否连接。

## 4. 未提交改动审阅

- 代码与两份修复记录一致：
  - `ILiveMonitorEventSource` 新增 `DisplayRecorderState` 和 `StatusChanged`，`AppService` 本来就已实现；
  - 空状态文案、图标和图表标题改为本地化；
  - Agent 页窄窗口布局和错误详情；
  - RawSensor 的“全部”选项；
  - README 模板及其 `.gitignore` 例外。
- 改动过的 XAML 没有新增写死的英文；中英文键数和占位符一致；新增冒烟检查覆盖了中英文 × 深浅色 × 两种窗口尺寸。
- 两轮都改了 locale 和 `Ui.Smoke/Program.cs`，按轮拆分提交意义不大。建议拆成“代码 + 测试”一个提交、“文档”一个提交。提交前先构建一次，跑安全范围内的 Core 测试和 UI 冒烟，并按 `docs/mcp-commit-plan-20260928.md` 的做法处理行尾。

## 5. 测试体系

- `CrashScope.Core.Tests`：44 个文件，373 个测试方法（含参数化测试）。上次全量结果是 9/28 的 497 个用例，本批新增 1 个。
  - 会接触真实系统资源的测试：`ActionsTests`（电源计划是模拟的，优先级只改测试进程自身）；`AgentMonitorViewModelTests`、`IncidentAccessTests`、`ProbeTests`、`RpcTests`（在临时目录启动真实录制器和命名管道）。`ReliabilityConvergenceTests` 走的是注入的替身实现（hooks）。
- `CrashScope.Ui.Smoke`：离屏 WPF，最近一次 1205 项检查通过。Timeline、Dashboard、Settings 三页直接依赖 `App.Current`，还没有交互覆盖。
- 缺少的测试：N1 的 JSON 转义路径、N2 的待确认问题脱敏、N4 的证据时间窗、N5 的慢停止、N6 的采集器异常隔离；另外所有 GUI 端到端路径都没有自动化测试。

## 6. 建议顺序

1. 经用户授权后，按第 4 节提交待审改动（同时修复 N9）。
2. 先修隐私问题 N1–N3：让正则兼容 JSON 转义路径，或按结构化字段脱敏；summary 全文过一遍脱敏；处理“域\用户名”和 IPv6；补回归测试。
3. 再处理诊断可信度 N4–N5：把证据窗口收窄到“心跳后首次启动附近”；退出时等待停止完成，或加“停止中”标记以免误报。
4. 顺带修正 N10，然后出新的修复包（不覆盖 0.9.11），在备份数据后做未录制状态的真实窗口复验。
5. 其余 P2/P3、正常结束会话中的游戏报错事件、体验 D/E、AMD 目标机验证和长时间稳定性测试，按既有规划推进。
