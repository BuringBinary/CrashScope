# CrashScope 可靠性收敛：阶段 A

日期：2026-09-17

基线：`feature/local-desktop-work` / `ac90e18`。本轮修改留在工作区，未自动提交、发布、安装或替换运行中的程序。

## 范围与结论

本轮落实 `docs/mcp-current-implementation-review.md` 中 R1–R5 的核心生命周期与证据保护修复，保留 C#/.NET、WPF、ETW、LibreHardwareMonitor、JSONL 和 Named Pipe 技术栈。不扩展系统调优功能，不将这次修复宣称为全部评审问题关闭。

**源码已完成本轮收敛，并通过选定回归验证；尚未做真实硬件长期录制、强杀或断电验收，也未部署到现有录制器。**

## 实际修改

### 1. 上次会话恢复不再依赖当前录制器初始化

涉及：
- `src/CrashScope.Core/Monitoring/SessionRecovery.cs`
- `src/CrashScope.Core/Monitoring/SessionJournal.cs`
- `src/CrashScope.Core/Monitoring/RecorderEngine.cs`
- `src/CrashScope.Desktop/AppService.cs`

`SessionJournal.ReadPrevious()` 是只读加载，不创建临时 session、不覆盖状态。Agent/Service 原有的 Start 前检查现在可以工作；即使宿主忘记预检查，RecorderEngine.Start 也会执行恢复流程。

恢复顺序：

1. 获取数据根所有权。
2. 将已有的有效 unclean session 持久化到 `state/recovery-pending/<sessionId>.json`。
3. 在 `state/recovery-work/<sessionId>/` 收集证据，检查并刷新输出文件。
4. 发布目录到 `incidents/recovered-<sessionId>/`。
5. 写 `state/recovery-completed/<sessionId>.json` 收据，再删除待恢复任务。

收集失败时保留 pending；允许新录制开始并显式报告 recoveryError，下次启动重试。完成发布但未写收据的中断可继续完成，不再次采集。新会话覆盖 last-session 前必须已经保留旧状态；旧状态或与之对应的 pending 损坏时拒绝静默覆盖。

恢复收据不会随 incident 配额清理一起删除，防止已处理的旧 last-session 在多次检查时被重复收集。不同失败任务仍按顺序处理；损坏任务的人工修复/队列管理界面不在本轮范围。

### 2. 数据根单写者与 Desktop 无副作用检查

涉及：`src/CrashScope.Core/Infrastructure/DataRootLease.cs`。

录制器、外部恢复和外部清理共用 `state/recorder.lock` 的独占文件句柄。锁文件本身不删除，以免形成“删除旧锁文件后新建另一把锁”的竞争。所有权跟随句柄释放，进程退出时由操作系统关闭。

- 第二个录制器不能同时拥有同一数据根，收到明确错误。
- Desktop 检查遇到活跃 Agent 时不收集、不改写其 journal。
- Desktop 本地录制入口保留，但重复启动受生命周期保护；本轮尚未把整个 Desktop 收敛成纯 RPC 客户端。
- Agent/Desktop 默认 LocalAppData、Service 默认 ProgramData；不同数据根仍可各自录制，锁不是设备级单实例限制。

### 3. 录制状态、异常与停止生命周期

涉及：
- `src/CrashScope.Core/Monitoring/RecorderEngine.cs`
- `src/CrashScope.Agent/Program.cs`
- `src/CrashScope.Service/SessionRecorderWorker.cs`
- `src/CrashScope.Contracts/RpcProtocol.cs`
- `src/CrashScope.Core/Ipc/AgentRpcServer.cs`

增加 `Starting / Recording / Stopping / Stopped / Faulted`。未处理的 Tick 异常明确转 Faulted，普通采样停止，IsRecording=false；不在持续磁盘错误时无退避重试。记录 last error 和最后成功采样时间；日志仍可写时将 RecorderError 写入会话状态。

观察者回调抛异常不会终结采集循环。已知 WPF 录制事件订阅改为异步 Dispatcher 投递，避免 UI 同步等待停止时卡住后台回调。

正常宿主和 RPC 使用 StopAsync：
- 立即公布 Stopping。
- 等待采集任务真正退出后才释放资源。
- 所有清理动作都会尝试执行，初始化中途失败使用同一路径清理已分配资源。
- 无已知采集/清理/持久化错误时，最后才写 clean。
- 已知故障保持 Faulted/unclean，不因调用 Stop 而洗成正常结束。

兼容同步 Stop/Dispose 最多等待 3 秒；超时后停止流程仍在后台继续，**不强行释放仍被 Tick 使用的资源、不写 clean、不释放数据根所有权**。原生调用若永不返回，停止也不能完成；这是有意保留证据/避免并发破坏的保守行为，不是已解决原生挂死问题。

RPC status 增加可选的 recorderState、recorderError、lastSampleAt、recoveryError；保留旧 v1 状态载荷反序列化兼容。停止/新会话时清除旧 telemetry 缓存。Desktop 状态文本和 tooltip 可显示 Faulted/Stopping 与错误详情。

### 4. 每次 Start 都拥有独立证据目录

每轮开始生成新的 SessionId 和唯一目录后缀；RPC stop/start 不再 append 到旧 session，也不再覆盖旧 sensor catalog。停止未完成时拒绝开始新一轮，避免旧 Tick 接触新字段。

第一个成功写入的普通遥测样本就建立有效 heartbeat，不再等到第 5 个 Tick。SessionJournal 先完成持久化，再发布新的内存状态，避免 UI/RPC 先看到尚未提交的 recoverable/clean 标记。

### 5. 清理保护

涉及：
- `src/CrashScope.Core/Infrastructure/RetentionService.cs`
- `src/CrashScope.Desktop/MainWindow.xaml.cs`

Desktop 启动改为先恢复、后清理。外部清理无法取得数据根锁时跳过全部删除；录制器内部使用已有所有权，显式排除当前及 pending session 后才做递归删除。路径按完整路径、大小写无关、移除末尾分隔符进行比较。

保护状态损坏/无法读取时，清理 fail closed。**活跃或待恢复数据可以导致超过配额**；本轮没有用删除活跃目录的方法伪造硬配额，也没有完成活跃 JSONL 分段轮转。最后一个 session 会被保守保留到下一个 session 替换它。

### 6. 故障证据不冒充系统崩溃

`SessionState` 增加可选 RecorderError，保持旧 JSON 兼容。`src/CrashScope.Core/Analysis/ReportGenerator.cs` 在摘要增加明确警告：录制器故障导致的中断和采样空白，不是机器崩溃的直接证明，既有假设排名也可能受影响。

本轮没有改写假设评分、驱动推荐或跨 incident 统计公式；那些仍是阶段 C 的任务。

## 验证

### 构建

使用该机器已有的用户级 .NET SDK 8.0.425，不安装新 SDK、不修改全局 PATH：

- Contracts、Core、Desktop、Agent、Service 均成功构建。
- 测试项目成功构建。
- Agent/Service 最终构建输出为 0 warning、0 error。
- 测试项目保留原有 `test/CrashScope.Core.Tests/RpcTests.cs:151` 的 CS1998（async 方法没有 await）；本轮引入过的 CS4014 已修正。
- 按仓库现有换行配置执行 `git --no-pager diff --check`，退出码为 0；Git 仍提示现有 LF/CRLF 自动转换策略。
- ShunCode 当前诊断查询返回 0 error/warning；这是编辑器诊断，不替代上面的编译结果。

### 测试

新增 `test/CrashScope.Core.Tests/ReliabilityConvergenceTests.cs`，27 项测试使用临时数据根、假采集器/假证据收集器。新增 RPC 测试只启动测试自有命名管道和假录制器，不连接用户正在运行的 Agent。

覆盖：
- 只读加载不改状态、坏状态不被覆盖。
- 未完成恢复跨新会话保留、失败重试、发布后中断续办、重复检查不重复采集。
- live owner 下恢复不触碰状态、第二 owner 被拒绝。
- 宿主 Start 前检查和 Start 自带恢复。
- 重复 start/stop 的目录与 SessionId 隔离。
- 第一条有效样本、写入失败不发布内存状态。
- Tick 异常、回调异常、初始化和清理异常。
- 超过同步 Stop 的 3 秒窗口后，仍不提前释放资源/锁、不写 clean；并立即公布 Stopping。
- 活跃、待恢复、坏状态下的保留策略。
- RPC 故障状态、旧协议载荷兼容、摘要的录制器故障警告。

**最终扩展回归：176/176 通过，0 失败、0 跳过。** 其中 39 项核心收敛测试又连续重复运行两轮，均 39/39 通过。 这是显式过滤的相关测试集合，不是全仓库全量测试；排除了会启动真实采集器、改变进程优先级等本轮不需要的测试。

验证过程中确实发现并修复了“内存状态先于磁盘提交”的问题。新增文件锁故障测试最初只接受 IOException，而本机 Windows 返回 UnauthorizedAccessException；改为准确接受这两种文件替换失败类型，仍要求状态不变，未放宽为任意异常。

### 可复现命令

在仓库根目录的 PowerShell 中，使用可用的 .NET 8 SDK；本机 SDK 位于用户目录：

```powershell
$dotnet = Join-Path $env:USERPROFILE '.dotnet\dotnet.exe'
& $dotnet build src/CrashScope.Agent/CrashScope.Agent.csproj --no-restore
& $dotnet build src/CrashScope.Service/CrashScope.Service.csproj --no-restore

# 39 项核心收敛回归：27 项新测试 + 既有 session/retention 测试
& $dotnet test test/CrashScope.Core.Tests/CrashScope.Core.Tests.csproj --no-restore `
  --filter 'FullyQualifiedName~ReliabilityConvergenceTests|FullyQualifiedName~SessionJournalTests|FullyQualifiedName~RetentionServiceTests'
```

176 项扩展集合是在上述三个类之外加入以下类，使用相同的 `FullyQualifiedName~<类名>` OR 过滤：
`CrashSafeJsonlWriterTests`、`CrossIncidentAnalyzerTests`、`DiagnosticExportRedactionTests`、`DisplayTopologyTrackerTests`、`EtwModelJsonRoundtripTests`、`GpuTransitionDetectorTests`、`GraphicsContextBuilderTests`、`HypothesisEvaluatorEtwTests`、`ModelsJsonRoundtripTests`、`RpcFramingTests`、`SensorHistoryBufferTests`、`SensorIndicesTests`、`StorageEvidenceTests`、`TimelineBuilderEtwTests`、`TimelineTelemetryTests`。

新环境尚未还原依赖时应先正常 restore；这里使用 --no-restore 是因为本机已有还原结果。临时验证日志留在仓库外，不放入 docs。

## 未执行与升级注意

- 未发布、安装 Service/计划任务、重启/停止用户已有 Agent、运行 probe --exercise、修改电源计划或进程优先级。
- 未做实际磁盘填满、断电、硬锁死、强制结束真实进程或长时间硬件压力测试。
- **升级前必须正常停止旧版宿主**。旧版不会参与 recorder.lock 协议；新代码无法靠这把锁阻止旧版继续写相同状态。建议备份现有数据后统一升级宿主，首次使用隔离数据根验证。
- WriteThrough/Flush 与目录发布提高可恢复性，不是硬断电零丢失保证。
- 原生硬件调用超时/进程隔离、每个采集器的 degraded 能力状态、长期资源开销和活跃流轮转仍待后续实施。
- Desktop 还不是纯 RPC 客户端，Service 的 Session 0 局限不变。
- R6/R7 系统动作的审计与回滚，R8/R9 分析口径，R10 ETW 重复路径，R11/R12 RPC 并发/断线与新 ViewModel 全面线程收敛，未在此轮宣称关闭。

## 下一步建议

先评审这批源码并在隔离数据根做人工非破坏性短时实录；确认无问题后，再安排统一升级与长期采集验收。开发主线随后进入阶段 B：默认被动采集、操作 intent/outcome 审计、回滚冲突检查，以及 RPC 断线/并发与 UI 状态提交收敛。
