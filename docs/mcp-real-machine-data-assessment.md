# CrashScope 实机数据价值评估

日期：2026-09-18（UTC+08:00）

## 结论

有用：现有数据证明基础遥测、进程上下文、异常会话恢复和证据包生成已经在目标机运行，并提供了具体的产品缺陷样本。但两条 `HardFreeze / 100%` 不能作为整机死机或显存耗尽的确诊结论。当前更适合作为采集验收、缺陷排查与回归样本。

本轮只读检查原始运行数据和源代码；未启动/停止录制、未提升权限、未修改源代码或原始证据。仅保存评估报告及仓库根目录外层 `.mcp-probes/` 下的调查摘录。没有重新运行程序或测试，不能宣称已修复。

## 检查范围

数据来源为用户截图所示的 `%LOCALAPPDATA%/CrashScope`，包括 `sessions/`、`incidents/`、`logs/startup.log`。以下证据文件名均相对于该数据根，而不是仓库文件。源代码引用相对于 CrashScope 仓库根。

| 项目 | 较长会话 | 较短会话 |
|---|---|---|
| 恢复包 | `incidents/recovered-d118b296401c4294a704a52b5a0ad80c` | `incidents/recovered-bf0ada058c2e4ef68cf19cf9b8376bd4` |
| 会话开始 | 08:39:40 | 09:16:44 |
| 最后心跳 | 09:07:06 | 09:16:54 |
| 开始至最后心跳 | 约 27 分 27 秒 | 约 10 秒 |
| 原始遥测样本 | 1649 | 12 |
| 包内遥测尾部 | 300 条，09:02:11–09:07:10 | 12 条，09:16:45–09:16:56 |
| 包内 GPU 显存/内存压力样本 | 各 60 条 | 各 2 条 |
| 包内进程快照 | 20 条 | 0 条 |
| 传感器目录 | 117 项 | 117 项 |
| 已检查尾部样本数组长度 | 均为 117 | 均为 117 |
| 权限 | 非管理员 | 非管理员 |

最后遥测晚于最后心跳数秒，因此心跳不是精确故障时刻。短会话不到 30 秒，没有进程快照与文档的 30 秒采集周期相符，不能仅据此判定采集损坏。

## 关键发现

### 1. 显存证据彼此冲突，自动诊断过度确定

两包 `summary.md` 均将 HardFreeze 排第一，分数 +3、置信度 100%。支持证据是单进程约 30.4/30.8 GiB 专用显存和所谓共享显存溢出；反证则是没有非正常系统关机证据。

原始 `gpu-memory-tail.jsonl` 中最高进程是 `GameViewerServer`（PID 20572）：

- 较长包峰值 dedicatedBytes=32629047296，约 30.4 GiB。
- 较短包峰值 dedicatedBytes=33109295104，约 30.8 GiB。
- 对应 sharedBytes=12582912，即仅 12 MiB。

而同时间的独显传感器表明：AMD Radeon RX 9070 XT 的 GPU Memory Total 为 16304 MiB，GPU Memory Used 在较长包为 2731–2899 MiB，在短包为 2738–2748 MiB；集显单独列出，GPU Memory Used 为 0。不能把进程计数器的 30 GiB 直接等同于独显物理驻留显存，更不能据 12 MiB 的共享使用断定显存耗尽。

`src/CrashScope.Core/Monitoring/GpuMemoryTracker.cs:55–110` 按 PID 汇总多个性能计数器实例，输出没有保留 adapter/LUID。数据口径、驱动计数器、虚拟显示和多适配器聚合需要进一步验证；本轮没有证明是哪一种原因，也没有证明 GameViewer 导致故障。

`src/CrashScope.Core/Analysis/HypothesisEvaluator.cs:387–423` 使用固定 7/10/12 GiB 阈值；total 大于 dedicated 即可增加溢出证据，没有充分核实容量和实际共享压力。`52–61` 行将正分归一化，并以支持证据权重封顶，因此本例 3+2-2=3，唯一正分即可显示 100%。这是启发式分数，不是经过统计校准的故障概率。

### 2. 实机日志明确记录了图表越界缺陷

`logs/startup.log` 在 09:16:46–09:16:56 多次记录 `TaskScheduler.UnobservedTaskException`，内层为 `ArgumentOutOfRangeException`，调用链包含：

`SensorHistoryBuffer.GetSeries` → `DashboardPage.UpdateCharts`。

当前源码 `src/CrashScope.Desktop/SensorHistoryBuffer.cs:44–48` 仅检查 `sensorIndex < Count`，没有检查 `sensorIndex >= 0`；`src/CrashScope.Desktop/Pages/DashboardPage.xaml.cs:21–34` 使用 -1 作为未找到传感器的索引，`388–414` 附近直接请求序列。存在明确的负索引缺陷路径，与日志一致。

仍需复现并验证具体触发的传感器索引。不能从未观察任务异常本身推断进程一定崩溃，更不能证明它导致了整机死机。

### 3. 有数据不等于每个采集项都有效

- 两个包的 CPU Tctl/Tdie 温度、Package 功耗、平均时钟在已检查尾部样本中持续为 0；不能拿这些值证明 CPU 温度/功耗正常，应标记不可用或可疑。两会话非管理员，权限/底层驱动兼容性是待查方向，尚未确认原因。
- 两包所有已检查 storage-tail 记录的 disks 均为空；有文件不代表磁盘性能取证成功。
- 三类 ETW 尾部文件均为 0 条。不能区分没有匹配事件、权限不足或采集启动失败，应记录采集器健康状态。
- 两包 windows-events.json 与 external-evidence.json 均为 `[]`。这表示包内没有此类证据，不表示本轮直接查询并排除了所有 Windows 历史日志或系统故障。
- 显卡温度、负载与内存压力等有实测内容：较长包独显核心温度 57–58°C、热点 58–59°C、负载 2–14%；两包所抽查内存压力首尾样本提交占比约 66–69%，可用内存约 10–11 GiB。这些片段没有直接显示独显过热或系统内存耗尽，不能据此排除未采到的瞬时异常。

### 4. 截图状态需要正确解释

`STOPPED / Samples: 0` 是当前录制状态，不代表历史没有数据。`src/CrashScope.Desktop/Pages/HomePage.xaml.cs:117–124` 在当前 SensorCatalog 为空时显示 Detecting；历史包已识别 Ryzen 7 9700X、RX 9070 XT 和集显，不能仅根据首页占位文字断言硬件从未识别成功。

`src/CrashScope.Core/Monitoring/SessionRecovery.cs:17–23, 64–78` 按未正常结束会话恢复证据包。`recovered-*` 证明恢复流程运行，不等于确认整机 HardFreeze。

版本呈现也不一致：首页 Ver 8.0.31 来自 `Environment.Version`（运行时）；截图侧栏为 v0.5.0-dev，而实机启动日志记录应用 0.9.0+f540c0ff7ca361dfc937344d8a82db796759ed2c。应分开显示应用版本、构建和运行时。当前源码与发布二进制没有进行逐项一致性校验。

## 建议优先级与验收

1. P0：修复图表负索引和缺失传感器处理；增加负索引、空目录、缺 GPU 功耗以及录制重启的回归测试，并用发布 EXE 验证。
2. P0：限制 HardFreeze 推断。证据不足时保留“会话非正常结束/原因未确定”，不要将归一化分数展示为确诊概率。
3. P1：GPU 计数器保留适配器标识，区分进程记账与适配器驻留量；使用实际容量/预算和同时间窗多源数据核对，不根据固定绝对阈值定性。
4. P1：采集器输出 supported/started/permission-denied/error/no-events 等状态；无效温度、时钟不得默认为正常零值。
5. P1：改善停止态硬件摘要、版本标签以及采集健康提示。
6. 保留原始包，修复后先做 5–10 分钟正常录制→停止→退出→重开的验收。管理员对照录制应由用户决定；不要自动强杀应用、断电或诱发蓝屏。

## 仍需用户确认

09:07 与 09:16 前后究竟发生了整机无响应/强制重启，还是只关闭、强退、重启了 CrashScope？两者决定恢复记录应如何解释。现有证据不足以代替这一现场事实。
