# CrashScope 只读功能审计与 MCP 续工准备（2026-09-27）

> 范围：`/d/workspace/workspace/CrashScope`。按用户授权，只读查看现存源码、测试包元数据、程序数据与相关 Windows 事件；没有启动 CrashScope、游戏或录制，没有改动应用源码、配置、原始会话/事故文件，没有运行新的构建和测试。本文是审计记录而非现版本真实窗口验收；只新增本文档。证据分别标明“已发生”“代码可直接证明”和“尚待运行验证”。不收录用户目录、会话 ID、原始日志、端点或凭据。

## 连接与工具规则

MCP `initialize` / `notifications/initialized` 成功；协议 `2025-06-18`，`shuncode-bridge 0.7.6`，15 个工具。当前工作区 `/d/workspace/workspace` 包含 **CrashScope** 与另一独立项目，不能混读；工作区文件工具按根目录限界，同机目录外的定点调查需 `run_command`。用 `list_directory/find_files/search_files` 定位、`read_files`/`lsp` 查源码，改动前读、改动优先 `apply_patch`，事后 `get_diagnostics`，命令测试走 `run_command`。多步工作从同一 MCP 会话的 `set_todos(new_task=true)` 开始并用其任务修订号更新，结束前取 terminal acknowledgement；可并行独立读取，不并发写同一文件。审计文档放 `docs/mcp-*.md`，不放 URL/令牌/会话 ID。本轮不做任何高风险现场操作。

## 当前包与现场基线

- 工作树：`feature/local-desktop-work`，HEAD `71c0603`（2026-09-20），相对远端 ahead 10，存在大量未提交 UI、契约、测试和文档。最新目录 `artifacts/test-packages/20260921-183401/` 的 `0.9.10-test.20260921183401` 显示 `status=verified`、包内选定测试 408 通过、976 文件哈希校验、`dirtyWorkingTree=true`、**`desktopGuiSmokeExecuted=false`**。它是测试包，不是完成真实 GUI 验收的可重建稳定发布。
- 9 月 21 日保留的中期日志显示 Desktop 构建 0 warning、Core 495 通过、隔离 WPF smoke 386 PASS；`test/CrashScope.Ui.Smoke/Program.cs:11-12` 直接写明其源 XAML 渲染移除事件处理器，不是 MainWindow 端到端测试。`docs/mcp-manual-acceptance-checklist.md` 的 12 路由 + 2 窗口尚无本候选包逐页勾验记录。本轮没有重跑这些测试。
- 此机 `%LOCALAPPDATA%/CrashScope` 中现存 10 个 session、2 个恢复 incident；最新 session 属于 **9 月 20 日**。`logs/startup.log` 最后启动记录为 **9 月 21 日 16:34 的旧 0.9.2 测试包**，早于 18:34 形成的 0.9.10 包。此数据根**没有证据证明 0.9.10 的真实 GUI 曾成功运行**；但也不能据此断言其他数据根或机器一定没运行过。只读检查时无 CrashScope 进程、同名 Windows 服务或计划任务；高级 Agent 页因此不能按“已连接 Agent”验收。
- 旧 `crash.log` 记录 9 月 20 日 `IconTextTemplate` 缺失导致真实启动失败；后续代码/文档已修此旧问题，**不要把旧栈当成 0.9.10 当下仍会启动失败的证据**。9 月 18 日启动日志还有旧版本多次未观察任务异常，不能泛化为现包故障。

## 与用户真实使用相关的已发生事实

- 9 月 18 日《三角洲行动》会话 **21:08–21:31** 确实写出 1,390 条遥测、278 条内存压力样本，并正常收尾。第一条五秒样本系统提交占比已 **97.58%**；21:31:14 峰值 **99.922%**、提交余量约 31 MiB，而物理可用内存最低仍约 **3.10 GiB**。278 样本中 250 条 ≥95%。System 日志在 21:30:48、21:32:13、21:34:50 有 Resource-Exhaustion-Detector **2004**，首条将三角洲进程列为主要提交占用者（约 **14.85 GiB**）；录制器约 21:31:30 正常停止，此后仍有资源耗尽事件。既有 `docs/mcp-delta-force-memory-incident-20260918.md` 对照完整时间线：这支持“当时 Windows **提交/虚拟内存不足**，非 GPU 显存耗尽”；**没有证据说 CrashScope 是主因**，也不能由这一次对照证明其任何版本在所有游戏负载下零开销。
- 这条游戏级报错发生在正常录制会话中。`AppService.CheckPreviousUncleanSession`（`:137-145`）仅走异常会话恢复；事件列表只读 `incidents` 根（`:154-157`），实际现存 2 条 incident 均为 9 月 18 日更早的恢复记录。因此**记录到内存压力 ≠ 在产品事件列表生成这次游戏内存报错的事件**；最新核心虽有事件 2004 watcher（`RecorderEngine.cs:485-489`），但它写进当前会话，不自动为正常结束的游戏报错建 incident。这是确实存在的产品闭环缺口。
- 9 月 20 日另有一份实际落盘的长时分析 `_longrun_analysis.json`：约 **3.682 小时**、13,247 个遥测点、3 处 >2.5 倍采样间隔缺口（最大 5.96 s）；CrashScope 自身 Private Bytes 前/后 10 分钟均值约 **141.54→146.54 MiB**，CPU p95 约 0.82%，句柄/线程未见同向持续增长；系统提交占比前/后 10 分钟约 **63.43→77.55%**，峰值 78.76%，会话文件约 **177.4 MiB / 48.2 MiB·h⁻¹**。该会话是 **0.9.4-soak**，并非 9 月 18 日游戏会话、并非 0.9.10，也不足整四小时；不能用它证明游戏事件的内存归因、最新包长稳或所有采集器可用。先前“只有长测准备文档、没有任何收尾数据”的判断需以此修正。
- 目标机健康记录显示 CPU 温度来源不可用/时钟和功率部分缺失，存储 `LHM disks=0` 因而 SMART 温度/健康不可用；9 月 20 日 GPU ETW **安全默认关闭**、Power/WHEA 实时 ETW 因普通权限未启用。9 月 18 日旧会话 GPU ETW 接收计数很大但匹配/写入为 0、Power ETW 权限拒绝。不能把“页面列有 ETW / SMART”当作现场已收集到相应证据；“不可用、未授权、无匹配事件”应分开展示。

## 源码可直接证明的功能缺陷（尚未在 0.9.10 实窗复现）

| 严重度 | 位置与触发 | 判断/后果 |
| --- | --- | --- |
| **P0** | `Pages/LiveMonitorPage.xaml.cs:45-80`：事件经过勾选过滤后，未判断列表是否为空就执行 `EventListBox.Items[0]`；`LiveMonitorPage.xaml:26-37` 允许关闭相关过滤器。 | 若新事件属于被关闭的类别且过滤后无可见项，UI 调度回调会越界抛异常。`App.xaml.cs:49-59` 对未处理 Dispatcher 异常弹窗并 `Shutdown(1)`。这是**可由代码路径推出的潜在桌面退出**，但本轮遵守授权未启动程序复现。 |
| **P0/P1** | `LiveMonitorPage.xaml.cs:19-24` 在构造时订阅六个 `AppService` 事件，整文件没有退订；`MainWindow.xaml.cs:347-380` 每次切回均新建 `LiveMonitorPage`。 | 离开后的旧页仍被事件持有，可能持续处理事件/占用内存；反复导航越积越多，与“低开销记录”目标冲突。需要改在 Loaded/Unloaded 成对管理并加测试。 |
| **P1 信任** | `IncidentCatalogReader.cs:62` 将事件时间设为 `PreviousSession.LastHeartbeatAt`；`TimelinePage.xaml.cs:68,85-91` 却在该时间标 `Crash Point` / `System failure`。 | 把**最后心跳**误写成精确故障点，也无可靠证据时称系统故障；与事件页“最后记录”口径相矛盾，应改时间锚点与文案。 |
| **P1** | `AppService.cs:154-157` 的简化 `GetIncidents()` 丢弃结构化读取问题；`TimelinePage.xaml.cs:34`、`DashboardPage.xaml.cs:607` 在 UI 线程同步调用。 | 次级页面可能把读取失败表现为空/无事件；首次重评估较大的历史 analysis 时也可能卡顿（性能影响尚未实测）。首页/事件页已经采用异步且保留错误信息。 |
| **P1** | `Pages/SettingsPage.xaml:191-194` 的“导出时默认脱敏”可勾选；`SettingsPage.xaml.cs:48` 只把它设为 true，保存按钮 `:252-261` 仅保存保留策略；`IncidentsPage.xaml:212-215` 导出勾选另有硬编码 true。 | 设置页该选项**没有持久化/传递到导出默认值**，用户改变它并保存不会生效。导出页自身默认脱敏为 true，尚未证明会因此泄漏，但设置语义失真。 |
| **P2** | `IncidentsPage.xaml.cs:205-225` 用 SaveFileDialog 取得所选文件名却只把**目录**传给服务；`DiagnosticExportService.cs:17-18` 重新拼固定文件名。 | 用户在“另存为”中自定义的 ZIP 文件名被忽略。 |
| **P2** | `LiveMonitorPage.xaml.cs:59` 读取 `FilterTelemetry`，但订阅和类别映射中无 Telemetry 事件；`DiskHealthPage.xaml.cs:57-63` 等仍有英文状态文本。 | “遥测”筛选当前近似装饰性；部分中文界面仍出现英文。磁盘 SMART 另外受目标机传感器不支持限制，不宜全算 UI bug。 |

测试盲区：上述 `LiveMonitorPage` 事件过滤、退订、设置脱敏开关、时间线“故障点”和保存文件名没有在 `test/` 中找到直接的交互断言；现有 Core 495 与 UI 386 不会自动覆盖这些真实页面路径。`get_diagnostics` 对 `CrashScope/src` 返回 0 项，但静态诊断不是运行验收。

## 建议处理顺序与下一步授权点

1. **先保护现场，再动代码**：9 月 18 日原始会话和 2 个 incident 不覆盖、不清理；程序启动和设置保存会触发 `RetentionService.Enforce()`，默认 30 天 / 512 MiB、sessions/incident 各半配额（`RetentionService.cs:37-71,171-175`），未来若需实机操作先获许可并备份/使用隔离数据根。本轮没有触发保留清理。
2. **最先修 P0 的 LiveMonitor 两处确定性代码缺陷**，在隔离夹具中加“类别被过滤后列表为空”与“反复进出页面的订阅次数”测试；随后复核构建、Core/UI 检查，仍须用户批准才启动真实桌面。
3. **再修信任链**：内存/游戏级异常即使干净停止也要能手动标记或生成只读时间窗；Timeline 时间锚改“最后记录、故障时间未知”；Dashboard/Timeline 对读取失败显示明确状态、必要时异步。重点把 commit、物理 RAM、VRAM 和采集权限分别讲清楚。
4. **随后收 UI/交付尾巴**：Settings 默认脱敏、另存为名称、遥测过滤/文案、Agent 入口的部署前提；在正确功能的基础上完成 12 路由 + 2 窗口真实人工验收，最后分批提交未提交工作树并构建干净来源的包。未经用户授权不安装服务、不修改注册表或现场设置、不启动录制。
5. 仍需用户给出当前觉得“不正常”的 **2–3 个页面/操作/现象** 才能把这份全局审计与其亲身问题一一对应；现有证据不允许宣称最新包全部失败或已全部正常。

**本轮连接状态**：端点已正常连通、工具与工作区规则已核实；结束前已更新 Bridge 任务卡。下一轮若连接临时会话失效，应重新连接用户提供的当前 MCP URL，不可把凭据写入仓库文档。
