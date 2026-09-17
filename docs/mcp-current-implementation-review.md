# CrashScope 当前实现评审

日期：2026-09-17
基线：`feature/local-desktop-work`，提交 `ac90e18`。开始评审时 Git 工作区干净。

## 结论

**技术栈总体合适，不建议推倒重写；当前实现仍不宜作为可靠的无人值守黑匣子基线，也不宜继续扩张系统操作功能。** 优先补齐崩溃恢复、录制生命周期和证据可信度，再统一桌面端与 Agent 的架构。

之前的评审记录声明全部关闭，但当前代码仍存在核心路径缺陷；“有修复提交”和“有测试通过记录”不等于用户场景已经闭环。下文以当前源码为依据，不沿用旧结论。

### 范围与验证边界

- 阅读项目说明、既有评审与 RPC 文档，检查工程依赖。
- 重点追踪 Agent / Service / Desktop 启动、RecorderEngine 生命周期、SessionJournal、文件持久化与清理、RPC 客户端和服务端、系统操作审计与回滚、部分采集器和分析计算。
- 抽查相关测试源码，不是逐行覆盖所有 UI、分析规则和依赖实现的穷尽审计。
- ShunCode 当前诊断返回 0 个 error/warning；这仅代表编辑器当前诊断结果，**不代表本轮完成编译或测试**。
- 本轮未运行构建、测试、Agent probe、系统设置变更或故障注入；未实际重现硬锁死、断电或多 GPU 场景。以下确定项来自直接控制流与数据流；条件性风险单独说明。
- 仅新增本报告，不修改业务代码。原有 189 个测试通过等数字属于历史记录，本轮未重新验证。

本文所有源码路径相对仓库 `CrashScope/`。P0 表示阻断黑匣子核心验收，不表示远程安全漏洞；P1 为高优先级可靠性/证据问题，P2 为后续加固。

## 一、技术栈判断

| 选择 | 判断 | 建议 |
|---|---|---|
| C# / .NET + Windows API | 适合 Windows 深度遥测与服务开发 | 保留；规划受支持 LTS 的升级与硬件依赖兼容验证，不把换运行时当作修复逻辑缺陷的方法 |
| WPF + OxyPlot | 适合本地仪表盘与托盘，无需为外观重写为 Web UI | 新页面延续可测试 ViewModel；逐步统一旧 code-behind 与 RPC 页面，不做无目标框架迁移 |
| LibreHardwareMonitor | 适合作为硬件兼容层，但不应被视为必定成功的可信数据源 | 每个采集器暴露 available/degraded/error、数据年龄、耗时；故障隔离，并在目标 AMD 机器验证 |
| TraceEvent + ETW | 适合实时 Windows 事件采集 | 当前实际依赖为 `Microsoft.Diagnostics.Tracing.TraceEvent 3.1.15`，README 写的 EventTracing.Processing 与工程不符；明确事件丢失、解码版本与持久化代价 |
| JSONL + WriteThrough/Flush | 易检查、易恢复尾部损坏，适合原始证据 | 保留原始流；补齐轮转、配额、恢复事务、schemaVersion。写穿不等于硬断电下零丢失或操作与日志的原子提交 |
| Named Pipe + Contracts | 适合本地 Agent/UI 通信，8 MB 入站帧限制与有界推送队列是正确方向 | 补齐并发帧写入、断线状态、超时、身份及权限模型；协议层不应承担隐式管理员授权 |
| Core / Contracts / Agent / Service / Desktop | 分层方向正确，但 Core 同时拥有平台采集、IPC、分析、系统修改 | 先梳理边界与接口，按需要拆模块；现在不需要微服务、云端、消息中间件或数据库大迁移 |

架构方向建议：**一个数据根只有一个 Recorder Owner；Desktop 默认只做 RPC 客户端；系统修改能力与被动证据采集分开授权。** 若未来需要 Service 覆盖无人登录阶段，再增加用户会话采集助手，不应指望 Session 0 获得用户前台与空闲状态。

## 二、必须优先修复的确定问题

### R1 · P0：Agent / Service 的上次异常会话恢复不会触发

证据：
- `src/CrashScope.Agent/Program.cs:83-92`
- `src/CrashScope.Service/SessionRecorderWorker.cs:44-54`
- `src/CrashScope.Core/Monitoring/RecorderEngine.cs:102-111,416-432`

两个宿主均先调用 `CheckPreviousUncleanSession()`，后调用 `Start()`。但 `_journal` 只在 `Start()` 内创建；检查方法只查看 `_journal?.PreviousState`，所以启动检查必然返回 null。之后没有再检查，反而写入了新会话状态。

影响：最核心的“强制重启后自动生成 incident”在这两个宿主的启动路径缺失。

建议：将“只读加载旧状态 → 持久化待恢复任务 → 收集并标记完成 → 开始新会话”显式建模。仅交换两行虽能修正常规路径，却不能解决收集失败后旧状态已被覆盖的问题。

验收：预置有有效 heartbeat 的 unclean session，分别启动 Agent/Service，确认产生一次且仅一次 incident；收集中断后可重试。

### R2 · P0：采集发生异常后停止工作，但继续显示 Recording

证据：`src/CrashScope.Core/Monitoring/RecorderEngine.cs:186-212`。

`catch (Exception)` 包围整个 while，而非单次 Tick。任一采集器、磁盘写入或订阅回调抛异常，循环就结束；只写 Trace，既不重启也不把 `IsRecording` 改为 false/faulted。宿主仍无限等待。

影响：仪表盘/RPC 可以持续显示录制中，实际上普通遥测与 heartbeat 已停止。事后分析还可能把录制器故障误认作系统冻结。

建议：区分采集器可降级异常与存储致命异常，增加明确 Faulted/Degraded 状态、持久化错误与最后成功采样时间；避免磁盘持续失败时无退避地重试。

验收：注入采集异常、订阅回调异常、写盘失败，确认状态真实、不会无声永久停录。

### R3 · P1：Desktop 的“检查旧会话”有写状态副作用，且缺少跨宿主单写者约束

证据：
- `src/CrashScope.Desktop/AppService.cs:46-65,75-105`
- `src/CrashScope.Core/Monitoring/SessionJournal.cs:14-30,63-76`
- `src/CrashScope.Desktop/MainWindow.xaml.cs:37-39,77-80`

没有本地 engine 时，Desktop 用新建 `SessionJournal(..., "temp-check")` 来检查。构造函数会立即覆盖 `last-session.json`，不是只读查询。Agent 与 Desktop 默认共享 LocalAppData 数据根，但没有数据根级所有权锁；Desktop 还能自行创建第二个录制器。

影响：仅打开 Desktop 就可能把活跃 Agent 的状态当成异常会话、覆盖其状态；heartbeat 的 `.tmp` 文件也可能竞争。故障窗口内还可能丢失真正的旧会话指针。

建议：检查与创建会话分离；按规范化数据根加跨进程锁，Desktop 检测到 Agent 后只连接。独立录制必须显式选择且不能共享写状态。

### R4 · P1：RPC 停止再开始会复用旧目录；停止超时后仍释放在用资源

证据：`src/CrashScope.Core/Monitoring/RecorderEngine.cs:12,64-71,110-131,366-412`；`src/CrashScope.Core/Ipc/AgentRpcServer.cs:245-268`。

- sessionDirectory 在 engine 构造时固定。RPC Stop → Start 复用同一 engine，创建新 SessionId 却继续 append 旧 JSONL，并覆盖 sensor catalog/driver inventory。不同录制阶段的证据混在同一目录，传感器顺序变化时旧 values 会被新 catalog 误解。
- Stop 只等 3 秒且忽略 Wait 返回值，随后 MarkClean、Dispose hardware/writers、置空 trackers。`_stopping` 只在 Tick 入口检查，不能阻止已进入慢采集的 Tick。下一次 Start 又复用字段，旧任务可能接触新资源。
- Start 中途初始化失败也缺少统一回滚，IsRecording 尚为 false 时 Stop 不会清理半初始化资源。

建议：每次开始创建独立 SessionContext 和唯一目录；异步状态机管理 Starting/Recording/Stopping/Stopped/Faulted，确定任务退出后再释放；超时不得伪称 clean，也不得允许新旧上下文混用。

现有 `test/CrashScope.Core.Tests/RpcTests.cs:128-147` 只覆盖一次 start/stop，没有覆盖第二次 start 的目录与证据隔离。

### R5 · P1：保留策略可能在恢复前删除证据，也未显式保护活跃目录

证据：
- `src/CrashScope.Desktop/MainWindow.xaml.cs:37-39,65-80`
- `src/CrashScope.Core/Infrastructure/RetentionService.cs:63-85,90-125`
- `src/CrashScope.Core/Monitoring/RecorderEngine.cs:333-336,450-458`

Desktop 先执行清理，再检查崩溃；在达到时间/大小条件时，尚未归档的异常会话可能被删除。Retention 按全部目录排序递归删除，未排除当前录制或待恢复 session。

不能依赖“文件被占用会阻止删除”：递归删除不是事务，未锁定的 catalog/元数据可能先被删，之后才因正在写入的 JSONL 失败；具体删除次序与结果需 Windows 实机验证。

建议：显式保护 active/pending-recovery session，恢复成功后才能纳入清理；活跃流使用分段轮转，不靠删除整目录限制体积。

### R6 · P1：系统先变更、日志后落盘，无法保证“每次变更可审计”

证据：`src/CrashScope.Core/Actions/SystemActions.cs:81-104,128-149,174-209`；`src/CrashScope.Core/Actions/ActionJournal.cs:41-47`。

电源计划/进程优先级/回滚都是执行系统修改后才 Append。如果日志目录不可写、磁盘满或该窗口进程退出，系统已经改变，但审计不存在或 RPC 只表现为失败。journal 自己加锁只保护单条写入，不串行化整次系统变更。

建议：执行前写入持久化 intent，执行后记录 applied/failed；崩溃恢复识别 outcome-unknown。对同一目标串行化，并让客户端识别不确定结果，避免超时后盲目重试。

同时，ActionJournal 持有 writer 却未实现 Dispose；ReadRecent 会加载整本日志后才 Take，后续需补资源生命周期和有界读取。

### R7 · P1：回滚只按 PID 定位，且不会检查后续修改冲突

证据：`src/CrashScope.Core/Actions/SystemActions.cs:134-135,162-209`。

- 原记录只保存 `pid:name`，回滚只提取 PID，没有开始时间等实例身份校验。PID 被复用后，可能修改另一个进程。
- 电源回滚不要求当前值仍等于被回滚动作的 NewValue；A→B→C 后回滚 A→B，会直接覆盖 C。
- 不检查已存在 rollback 记录，也无完整动作级并发控制。多个客户端可能读取同一 old value 后先后执行，产生不真实的还原链。

建议：记录进程实例身份，执行 compare-before-restore；冲突时拒绝并解释；对目标操作串行化、回滚幂等化，旧记录超出最近 500 条也不应无说明地丧失追溯能力。

## 三、证据解释与通信/UI 问题

### R8 · P1：跨 incident 的故障率与推荐存在统计和分组错误

证据：
- `src/CrashScope.Core/Analysis/CrossIncidentAnalyzer.cs:331-356,384-396`
- `src/CrashScope.Core/Analysis/RecommendationEngine.cs:49-62`

1. 驱动“活跃天数”实际是第一到最后一次故障的日历跨度，没有正常运行时间或驱动安装区间。MTBF 也是首末故障跨度除以故障数，并非实际观测运行时长。它们不足以支持可靠的版本稳定性比较。
2. modeBreakdown 将每个 incident 的所有候选假设都计为 FailureModeCount；包含有反证的候选，不等于实际故障次数。
3. 驱动 failureModes 对所有 incident 的假设先展平再 Take(1)，整组最多留下一个模式，而非逐 incident 汇总。
4. stableDriver/unstableDriver 在所有适配器之间选，没有要求同一个 GPU；多独显或 unknown 回退清单中，可能建议把另一块卡的版本作为回滚目标。

建议：优先修正按适配器身份分组和故障模式计数；增加正常 session 的有效采集时长。数据不足时只展示“样本内关联/间隔”，不称故障率，不输出确定性驱动回滚结论。

### R9 · P1：部分“硬件事实”仍是推断，不能作为已测量证据

- **DWM**：`src/CrashScope.Core/Monitoring/DwmStateTracker.cs:29-49` 仍把 composition 同时写为 IsHardwareRendering；HRESULT 失败也转换为 false，并产生“GPU fell back to software rendering”描述。`src/CrashScope.Core/Analysis/HypothesisEvaluator.cs:181-182` 继续消费这个信号。注释承认限制不等于修正输出语义。API 失败应为 unknown，不是状态改变；合成开关不证明硬件/软件渲染回退。
- **独显判断**：`src/CrashScope.Core/Diagnostics/DriverInventory.cs:89-100` 用报告显存 >=1 GiB 判独显。测得容量不代表测得显卡类型；预分配显存较大的 iGPU 与小显存 dGPU 是反例。应保留 unknown/heuristic 标记，不能据此硬过滤其他适配器。
- **VRAM**：`src/CrashScope.Core/Analysis/HypothesisEvaluator.cs:159-169,381-423` 用单进程 7/10/12 GiB 固定阈值推断接近上限、耗尽或溢出，未绑定适配器实际预算；专用和总量峰值还可能来自不同时间/进程。小显存多进程耗尽会漏报，大显存正常使用会误报。应同一采样、同一适配器比较 usage/budget，缺少预算时仅报告原始使用量。

当前加权 confidence 有低证据量封顶，是改进；但 `HypothesisEvaluator.cs:52-61` 仍是规则分数归一化，不是经样本校准的根因概率。UI 应明确为启发式支持度。

### R10 · P2：ETW 同一事件会重复通知，停止时还可能重复写盘

证据：
- `src/CrashScope.Core/Monitoring/EtwDxgKrnlMonitor.cs:105-109,129-133`
- `src/CrashScope.Core/Monitoring/EtwRealTimeMonitor.cs:118-125,160-171`
- `src/CrashScope.Core/Monitoring/RecorderEngine.cs:142-143,164-165,339-360`

OnEvent 已写盘并直接通知；事件同时进入队列，RecorderEngine Drain 后又通知。Stop 的 FlushRemaining/FlushAll 再写一遍尚未 drain 的事件。

建议：明确唯一持久化路径和唯一分发路径，buffer 是 UI 缓存就不要再次写盘。新增 event sequence/去重测试。ETW 回调逐事件 FlushToDisk 的吞吐与内核缓冲丢事件情况另需压力测量，本轮未测。

### R11 · P1：RPC 客户端没有完整帧写锁，断线也不及时终结请求

证据：`src/CrashScope.Core/Ipc/AgentRpcClient.cs:40-62,93-124`；`src/CrashScope.Core/Ipc/RpcFraming.cs:42-49`。

帧 header/body 是两个 await 写入；并发 SendRequestAsync 没有串行化。注释说一次一个请求，但 API 并不强制。两次写可形成 header-A/header-B/body-A/body-B，导致协议损坏。独立 UI 操作或刷新重叠即具备触发条件，本轮未做并发复现。

读循环遇 EOF/异常只退出，不立即 fail pending，也不通知 ViewModel 断线；请求等超时，页面可继续显示旧连接/旧数据。CancellationToken 取消还可能被映射为 TimeoutException。

建议：整个帧加异步写锁；统一连接状态机，断线清空并失败所有 pending；保留取消语义；分离事件分发，避免慢事件处理阻塞响应读取。

### R12 · P2：新 ViewModel 的绑定集合更新没有统一回 UI 线程

证据：`src/CrashScope.Desktop/AgentMonitorViewModel.cs:119-127,143-152,214-222`。

事件推送走 `_marshal`，但 RefreshAsync/LoadIncidentsAsync 在 ConfigureAwait(false) 后直接 ApplyCatalog/修改 ObservableCollection。生产中网络等待通常异步完成，因此这些更新可发生在工作线程，造成 WPF 绑定异常或竞态。默认同步 marshal 的单元测试不能证明 UI 线程正确。

建议：数据获取在后台，统一将绑定状态提交到 Dispatcher；覆盖真实异步完成和快速切页/断连场景。

## 四、权限、工程与文档补充

- `PipeOptions.CurrentUserOnly` 是好的基线，但不是完整的动作授权与可信调用者审计。hello clientName 是客户端自报值。未来连接 LocalSystem Service 的用户 UI，也不能假设自动满足当前用户限制；需设计并验证 ACL、令牌/提权级别和用户会话边界。本轮未证明存在提权漏洞。
- `--no-actions` 只关闭 SystemActions；RPC start/stop 仍可用。若产品意图是“完全只读”，应另设能力开关并在握手返回 capability。
- 握手没有单独截止时间，4 个连接均可被不发送 hello 的本地客户端占住；属于本地鲁棒性问题，需连接超时/限流。
- README/ROADMAP 对 DWM、64 MB ring、技术依赖和架构已经落后于代码。先修产品语义再同步文档，不要以版本勾选数量代替完成度。
- 测试基础值得保留，但需从 DTO/规则 happy path 转向宿主启动恢复、Stop→Start、磁盘异常、慢采集退出、并发 RPC、回滚冲突及 UI 线程边界。硬件集成测试与纯单元测试应明确分组，避免普通测试流程无意启动 ETW 或修改系统状态。

## 五、后续规划建议（待讨论，不代表已授权执行）

### 阶段 A：先让黑匣子核心可信

范围：R1–R5。统一恢复与会话状态机，单写者锁、独立 session 目录、真实 fault 状态、清理保护。

退出标准：Agent/Service 可重复恢复 unclean session；采集故障不假绿；重复 start/stop 不混数据；Desktop 不覆盖 Agent 状态；恢复失败可重试。先用隔离数据目录与可注入采集器验证，再在目标机做非破坏性实录。硬断电/强杀由用户另行确认。

### 阶段 B：收紧操作与通信边界

范围：R6、R7、R11、R12。默认被动录制，动作能力显式 opt-in；intent/outcome 审计、实例身份与冲突检查；完善帧写锁、断线/超时/UI marshal。

退出标准：任何操作结果可区分成功/失败/未知；回滚不触碰复用 PID、不覆盖外部变更；断线立即显式显示；并发请求不会破坏帧。

### 阶段 C：让证据与结论一致

范围：R8–R10。修正事件重复、统计口径、适配器身份和 unknown 语义；把启发式支持度与测得事实分开。

退出标准：同一输入可重放产生确定输出；正常场景不凭静态容量阈值高分诊断；不同 GPU 不交叉建议驱动回滚；证据包记录 schema、规则版本与采集缺失情况。

### 阶段 D：再决定产品形态

推荐主线：Desktop 收敛为 Agent 的 RPC 界面，保留 WPF；Service + 用户会话助手作为单独里程碑；系统工具箱为独立的可选能力。增加运行时升级、发布/依赖许可检查、诊断脱敏复审、资源占用与长时间实录验证。

**本轮建议暂停添加新的调优按钮和“智能根因”标签，不暂停项目本身。已有基础可以继续使用，优先把采得到、留得住、说得准这三件事做成可验证的闭环。**
