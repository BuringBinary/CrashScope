# CrashScope 实机问题修复与验证

日期：2026-09-18（UTC+08:00）

## 范围与状态

本轮根据 `docs/mcp-real-machine-data-assessment.md` 修复图表异常、证据不足时过度确定的诊断、采集缺失提示及相关展示问题。代码已落地并通过选定回归与 Release 构建；没有发布安装包、覆盖 dist、重启用户应用或改变系统配置。工作区原有的标题栏、托盘、设置和主题等修改均保留，未创建 Git 提交。

原始实机 sessions、incidents、日志没有被修改。新代码需要重新打包部署后，才会在用户当前运行的发布版中生效。

## 已修复

### 图表及传感器映射

- `src/CrashScope.Desktop/SensorHistoryBuffer.cs`：负索引返回空序列；跨样本长度不一致、null、NaN、Infinity 安全跳过；输入数组复制保存；新增 Clear；拒绝非正容量。
- 新增 `src/CrashScope.Desktop/DesktopSensorMap.cs`，与 `src/CrashScope.Core/Analysis/SensorIndices.cs` 共享主 GPU 选择规则。缺少独显功耗时不再借用集显功耗；物理内存与 Virtual Memory 分开；支持 AMD Tctl/Tdie 名称。
- `src/CrashScope.Desktop/Pages/DashboardPage.xaml.cs`：在 catalog 或 session 变化时重新映射并清空历史；订阅与 Loaded/Unloaded 对应；已排队的旧 session 传感器回调被丢弃。图表及主读数在 UI 线程更新，缺失值显示 N/A。
- `src/CrashScope.Desktop/Pages/HomePage.xaml.cs`：恢复页面事件订阅，刷新传感器映射；停止时显示 Not recording，不再无限 Detecting；RAM 使用 GiB，首页将 App 与 Runtime 版本分开。

### 保守诊断与旧记录只读重评

- `src/CrashScope.Core/Analysis/HypothesisEvaluator.cs`：撤除未经 adapter/capacity/residency 验证的 GPU Process Memory 自动显存耗尽加分。较大计数仍作为待核实问题保留，不删除原始值。
- 没有非正常系统终止证据，也没有未恢复内核 GPU timeout 时，资源压力、显示切换等上下文不能单独把 HardFreeze 推为正分；仍保留支持和反对证据用于调查。
- 不再用唯一正分候选归一化出 100%。Confidence 字段为兼容旧协议保留，但改为带先验权重和反证折扣的启发式支持指标；界面和新报告明确不是概率或确诊。
- `src/CrashScope.Core/Analysis/IncidentAnalyzer.cs` 新增只读重评；`src/CrashScope.Desktop/AppService.cs` 在读取本地历史记录时按当前规则重评并缓存，不回写旧 analysis.json、summary.md 或 timeline.md。无法读取必要证据数组时不沿用旧版确诊排名。
- 托盘、报告、诊断导出及 Agent/Service 的相关措辞改为候选/启发式证据支持。Agent Incidents 列表标记为 saved heuristic support；其 RPC 历史摘要仍读取存档，不在本轮进行全量迁移。
- 导出说明明确：界面重评结果和保存的旧报告可能不同，原始文件保留作为来源记录。

### 采集状态及读数质量

- 新增 `src/CrashScope.Core/Monitoring/CollectorHealthTracker.cs`，向每个新 session 写入 collector-health.json。按状态变化原子落盘，落盘成功后才发布内存状态，避免虚报持久化成功。
- `src/CrashScope.Core/Monitoring/RecorderEngine.cs` 记录 Sensors、Storage、ETW GPU、ETW System 状态，区分 AwaitingSample、Collecting、NoData、Unavailable、Degraded、PermissionDenied、Error、Started。
- Started 仅说明会话启动，不表示观察到了事件或覆盖完整。ETW provider 启用、处理和写入异常开始可见；空磁盘样本显示 NoData。
- `src/CrashScope.Core/Diagnostics/CrashEvidenceCollector.cs` 将健康元数据带入新证据包；分析报告对旧包缺少元数据明确提示“空文件不能证明采集成功”。
- 新增 `src/CrashScope.Core/Analysis/SensorValueQuality.cs`：CPU 温度、Package 功耗、平均频率的非正可疑值及非有限数在仪表盘显示不可用，并提示检查权限/驱动支持；原始遥测保持不变。正常的零负载、停转风扇、睡眠 GPU 时钟及 CPU 单核 effective clock 等没有被一律丢弃。

## 验证结果

环境：Windows 10.0.26200，.NET SDK 8.0.425 / Runtime 8.0.31。

- 第一轮相关测试：127 通过、0 失败、0 跳过。
- 最终扩展回归：200 通过、0 失败、0 跳过。
- Desktop Release 随测试项目构建成功；Agent 与 Service 分别 Release 构建成功，各 0 错误、0 警告。
- 测试项目有一条既存 `RpcTests.cs:151` 的 CS1998 警告，本轮未改该文件。
- MCP 结构化诊断未返回错误/警告；这不替代编译日志中的 CS1998。
- git diff --check 通过（exit 0）；Git 另有工作区 LF/CRLF 转换提示，无空白错误。

新增/更新测试涉及负索引、越界、数组长度变化、输入数组复用、清空历史、缺独显功耗、多 GPU/物理 RAM 映射、无系统证据的组合资源压力、保留真实 timeout 候选、30.8 GiB 进程计数不确诊、可疑 CPU 零值与合法闲置零值、采集状态落盘失败、旧包只读重评和文件内容不变。

### 实机证据回放

在只读模式下重新分析截图中的两个原始证据包：

| 恢复包 ID 后缀 | 保存的旧排名 | 当前规则 | 原始文件校验 |
|---|---|---|---|
| d118b296401c4294a704a52b5a0ad80c | HardFreeze / 100% | Unclassified，支持指标 0 | 23 个文件 SHA-256 全部不变 |
| bf0ada058c2e4ef68cf19cf9b8376bd4 | HardFreeze / 100% | Unclassified，支持指标 0 | 23 个文件 SHA-256 全部不变 |

两份包现在会提示未经验证的 GPU 进程显存计数、缺少磁盘指标、旧包缺少采集器健康元数据、可疑 CPU 零读数。Unclassified 表示证据不足，不表示电脑一定没有故障。

临时验证程序和日志保存在 ShunCode 工作区根的 `.mcp-probes/`，不放入 docs；回归 TRX 位于 `test/CrashScope.Core.Tests/TestResults/real-machine-fix-expanded.trx`。

### 可复现的相关测试集合

使用 `test/CrashScope.Core.Tests/CrashScope.Core.Tests.csproj`，Release 配置，以下类名以 `FullyQualifiedName~<类名>` 用 OR 连接作为 filter：

- SensorHistoryBufferTests、RealMachineFixTests、HypothesisEvaluatorEtwTests、StorageEvidenceTests、SensorIndicesTests
- ReliabilityConvergenceTests、TimelineTelemetryTests、CrossIncidentAnalyzerTests、IncidentAccessTests
- SessionJournalTests、RetentionServiceTests、CrashSafeJsonlWriterTests、DiagnosticExportRedactionTests
- DisplayTopologyTrackerTests、EtwModelJsonRoundtripTests、GpuTransitionDetectorTests、GraphicsContextBuilderTests
- ModelsJsonRoundtripTests、RpcFramingTests、TimelineBuilderEtwTests

本机 dotnet 在用户 `.dotnet` 目录而非默认 PATH；首次缺少 project.assets.json，已正常还原依赖后再验证。不把无 assets 时 dotnet test 的空输出/零退出码算作通过，以实际测试计数和 TRX 为准。

## 尚未验证或未解决的边界

- 不是全仓库全量测试；排除了真实采集、系统操作等不需要的测试。没有启动应用窗口进行发布 EXE 的手动交互验收，没有长时录制、管理员对照、真实死机/断电或蓝屏实验。
- 没有修正 Windows/驱动实际返回的 30 GiB 进程计数，也没有实现按 adapter/LUID、驻留量和预算的完整显存校验；本轮先阻止其误导诊断。
- 没有保证目标机 CPU 原始传感器/磁盘指标已经恢复。新状态会揭示缺口，后续需要实机权限与底层驱动支持验证。
- CPU 可疑值过滤针对本次观察到的已知类型，不是完整传感器质量模型；原始取证数据仍保留。
- Confidence 是兼容字段，不是经过统计标定的概率。未将所有旧存档或远端 Agent 历史索引批量迁移。
- 首页版本标签已分离，但全应用其他硬编码版本文案不在本轮全面整理范围。

## 下一步验收建议

正常停止旧版录制，保留/备份数据后重新打包部署；先做 5–10 分钟正常录制、切换 Dashboard/Home、停止/重启、再次进入页面的验收。确认无图表异常，缺失传感器显示 N/A，采集器失败原因可见，旧异常列表变为证据不足，而不是为了复现故障主动强杀或断电。
