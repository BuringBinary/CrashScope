# Roadmap

## v0.1 — Flight recorder

- [x] 1 Hz CPU/GPU/RAM telemetry through LibreHardwareMonitor
- [x] crash-oriented write-through JSONL logging
- [x] foreground process and user-idle tracking
- [x] process start/stop events
- [x] periodic process snapshots
- [x] session heartbeat and clean-shutdown marker
- [x] next-boot detection of an unclean session
- [x] capture key Windows System events into an incident bundle
- [x] extract telemetry/app tails around the suspected incident
- [ ] validate on the target AMD machine

## v0.2 — Correlation

- [x] exact timeline reconstruction around the last heartbeat
- [x] distinguish hard freeze / BSOD / TDR / app crash / power loss
- [x] evidence-for / evidence-against scoring
- [x] detect GPU load/clock/power state transitions before failure
- [x] parse Reliability Monitor / WER / LiveKernelReports metadata
- [x] report generator with root-cause ranking and confidence
- [ ] validate on the target AMD machine

## v0.3 — Graphics-stack context (current)

- [x] per-process GPU Engine counters
- [x] display topology and virtual-display changes
- [x] remote-session connect/disconnect events
- [x] sleep/display-off/resume/power-plan transitions
- [x] detect common overlays, launchers, remote-desktop and hardware-accelerated apps
- [x] AMD/NVIDIA/Intel driver inventory and driver-change timeline
- [ ] validate on the target AMD machine

## v0.4 — Productization

- [x] Windows Service collector
- [x] lightweight tray UI
- [x] configurable retention/ring buffer
- [x] incident viewer
- [x] charts (CPU/GPU/RAM history graphs)
- [x] one-click diagnostic export with privacy redaction

## v0.5 — ETW kernel-level GPU tracing (current)

- [x] ETW real-time session for Microsoft-Windows-DxgKrnl provider
- [x] capture TDR stages (timeout → prepare → recover → success/fail)
- [x] capture GPU engine resets and adapter state changes
- [x] capture GPU power component transitions
- [x] JSONL output to etw-gpu.jsonl with write-through safety
- [x] circular in-memory buffer (64 MB ring) for incident tail dump
- [x] integrate ETW events into HypothesisEvaluator scoring
- [x] integrate ETW events into TimelineBuilder
- [x] unit tests for v0.5 (EtwModelJsonRoundtrip / TimelineBuilderEtw / HypothesisEtwSignals)
- [ ] validate on the target AMD machine

## v0.6 — Multi-provider ETW (Kernel-Power + WHEA real-time)

- [x] generalize EtwDxgKrnlMonitor into EtwRealTimeMonitor supporting multiple providers
- [x] Microsoft-Windows-Kernel-Power ETW (sleep/resume, thermal, power transitions)
- [x] Microsoft-Windows-WHEA-Logger ETW (real-time hardware errors)
- [x] generic EtwProviderEvent model for multi-provider JSONL output
- [x] integrate Kernel-Power events into HypothesisEvaluator (hard freeze vs sleep)
- [x] integrate WHEA events into HypothesisEvaluator (real-time vs post-mortem)
- [x] update TimelineBuilder for new ETW categories
- [x] unit tests for v0.6 (EtwRealTimeMonitor multi-provider / HypothesisEtwSystemSignals / TimelineEtwCategories)
- [ ] validate on the target AMD machine

## v0.7 — Incident Intelligence (current)

v0.1-v0.6 解决了"抓什么数据"和"单次如何分析"。v0.7 解决"多次崩溃之间有什么规律"——
把多个 incident 当作一个整体来分析，给出可操作的建议。

### Cross-Incident Pattern Mining

- [x] CrossIncidentAnalyzer: 跨 incident 模式挖掘引擎
- [x] 共同进程模式：多次崩溃前相同进程（启动器、远程软件、覆盖层）在场
- [x] 共同显示拓扑模式：多次崩溃前同一个虚拟显示器驱动活跃
- [x] 共同 GPU 状态模式：崩溃前 GPU Load/Clock/Power 的相似走势
- [x] 共同 ETW 模式：多次出现相同 TDR 阶段序列或 WHEA 错误类型
- [x] 失败模式聚类：统计每种 FailureMode 的频率和条件
- [x] 时间聚类：检测崩溃是否密集发生（同一天/同一时段）

### Driver-Failure Correlation

- [x] 驱动版本 vs 失败模式关联表
- [x] 驱动更新前后失败频率对比（更新后失败率是否飙升）
- [x] 驱动回滚建议：如果旧版本更稳定，推荐回退

### Stability Trend

- [x] MTBF（平均无故障时间）趋势计算
- [x] 按 FailureMode 分类的趋势线
- [x] 稳定性评分（0-100），驱动更新/系统变更时标注

### Recommendation Engine

- [x] 基于累积证据的规则引擎
- [x] 推荐权重 = 模式频率 × 证据强度
- [x] 可操作格式："建议回退驱动到 24.5.1 — 因为升级到 24.8.1 后 5 次 TDR"
- [x] 推荐类型：驱动回退、禁用虚拟显示器驱动、关闭硬件加速 GPU 调度、检查散热、禁用覆盖层

### Desktop: Health Dashboard

- [x] 主页增加稳定性评分卡片（当前分数 + 趋势箭头）
- [x] 最近 N 次 incident 缩略摘要（时间、模式、置信度）
- [x] "跨 incident 分析"按钮 → 生成 CrossIncidentReport
- [x] Incident 对比视图：并排展示两次 incident 的差异/共同点
- [x] 推荐列表面板：按优先级排列的 actionable 建议

### Tests

- [x] CrossIncidentAnalyzerTests：模式挖掘、驱动关联、推荐引擎单元测试

## v0.8 — Storage / IO Telemetry

- [x] `StorageSample` + `DiskMetrics` 数据模型（Records.cs）
- [x] `StorageTracker` 采集器：每 5 秒采集存储传感器 + IO 性能计数器
- [x] NVMe/SSD 温度（LibreHardwareMonitor `IsStorageEnabled`）
- [x] SMART 健康状态（LibreHardwareMonitor Level sensor）
- [x] WMI 查询磁盘型号（`Win32_DiskDrive`）
- [x] `\PhysicalDisk\*` 性能计数器：`% Disk Time`, `Avg. Disk sec/Read`, `Avg. Disk sec/Write`, `Current Disk Queue Length`
- [x] `RecorderEngine` + Agent `Program.cs` 集成
- [x] `CrashEvidenceCollector` 复制 `storage-tail.jsonl`
- [x] `IncidentAnalyzer` 读取 storage events 并传入 `HypothesisEvaluator`
- [x] `HypothesisEvaluator` 新增存储证据维度：
  - 硬盘温度 ≥ 70°C → 过热警告
  - SMART 健康 < 100% → 硬盘劣化
  - % Disk Time ≥ 95% 或 Queue Depth ≥ 8 → IO 饱和导致系统无响应
  - 读/写延迟 ≥ 100ms → 异常高延迟
  - 遥测延迟 > 30s + IO 饱和 → 录制器本身可能被磁盘 IO 阻塞
- [x] `BuildOpenQuestions` 新增存储相关诊断问题
- [x] ROADMAP 更新
- [ ] validate on the target AMD machine