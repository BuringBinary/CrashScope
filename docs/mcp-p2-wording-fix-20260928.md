# P2 文案批次：code-behind 可见英文清零与空态补齐（2026-09-28）

> 范围：两份 locale、两个 Agent ViewModel、Dashboard/Incidents/DiskHealth/AgentActions 页面、RawSensorWindow、MainWindow 托盘、smoke/Core 断言同步。未启动真实桌面、未触碰原始数据、未提交 Git。清单来源：`docs/mcp-ui-midpoint-review-20260921.md` 第四节第 3 项 + `docs/mcp-crashscope-readonly-functional-audit-20260927.md` P2 行。

## 修复内容

1. **AgentActionsViewModel**：全部 7 处状态文案（未连接/已连接/连接失败/电源计划/优先级/回滚/拒绝/失败）改走 `Loc.T/F` 键（`agentActions.status.*`、复用 `common.notConnected/connected`）。`ActionsTests` 同批改为断言资源键（无 App 时 Loc 回退键名）。
2. **AgentMonitorViewModel**：5 处 `"Not connected."` 异常消息改 `Loc.T("common.notConnected")`（会拼入状态栏 Failed 文案）。
3. **DashboardPage**：指标/传感器表 6 处 `N/A`、`GiB total`、`N/A (unavailable or suspect)`、GPU 引擎 `Unknown` 改键（`common.notAvailable`＝“--”破折号口径、`dashboard.ramTotal/sensorUnavailable`、`common.unknown`）。
4. **IncidentsPage 对比面板**：9 处 `N/A`/`No data` 改键；顺带修真 bug——`FinalGpuLoadPercent?.ToString()+"%" ?? "N/A"` 因字符串拼接永不为 null，缺数据时显示孤立 `%`，现为显式条件。
5. **IncidentsPage 详情空态**：假设/待确认问题为空时显示“本次记录没有假设数据。/本次记录未生成待确认问题。”，不再只剩标题。
6. **DiskHealthPage**：4 条状态行 + “No ready volumes.” 全部键化（`diskHealth.status.*`）。
7. **RawSensorWindow**：4 条状态行键化（`rawSensors.*`）。
8. **MainWindow 托盘气泡**：候选分类改走 `IncidentPresentation.Mode` 本地化映射，原始枚举名不再出现在气泡中。
9. **AgentActionsPage 优先级下拉**：Content 本地化（`agentActions.priorityLevels.*`），RPC 协议值移到 `Tag`，code-behind 读 `Tag`。
10. **“未知分类”→“分类信息缺失”**（en：Classification data missing）：与“未分类”语义区分（未知枚举回退 vs 已知未分类）；smoke 两处字面量断言同步。

## 验证（2026-09-28 本机实跑）

| 项 | 结果 |
| --- | --- |
| locale JSON | zh/en 解析通过，新增键双语对齐 |
| Desktop Release 构建 | 0 警告 0 错误 |
| Core.Tests | 497 通过 / 0 失败（含更新后的 ActionsTests 断言） |
| UI smoke | 440 项 PASS（首轮抓到 modes.Unknown 断言不匹配，更新断言后全过）；证据 `.mcp-probes/ui-smoke-20260928-160520/` |
| get_diagnostics | 0 项 |

边界：`Loc` 无 App 时回退键名，因此单测里状态字符串是键而非成品文案——真实窗口中的双语渲染仍属人工验收范围。ViewModel 状态在语言切换后不重算（与既有行为一致）。

## 未入本批的展示细节（待决策）

- 页面标题排版统一（首页 22/SemiBold vs 其余 24/Bold，建议抽 `PageTitle` 样式）——视觉决策，建议与批次 E 一起做。
- 首页事件行标题同质化——依赖字段评估，midpoint 评审建议“没有可靠字段则保持现状”。

## 后续

审计既列缺陷至此全部处理完毕（P0×2、P1×2、P1/P2 设置导出×2、P2 文案批）。剩余大项：游戏内报错闭环（产品决策）、体验批次 D 确认、12 路由+2 窗口真实 GUI 人工验收、分批提交与干净基线包。
