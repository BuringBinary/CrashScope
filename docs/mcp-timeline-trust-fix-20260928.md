# P1 信任链修复：时间锚点与事件读取失败可见化（2026-09-28）

> 范围：`TimelinePage`、`DashboardPage` 健康卡、`AppService` 兼容方法删除、两份 locale。未启动真实桌面、未触碰原始会话与 incident 数据、未提交 Git。依据 `docs/mcp-crashscope-readonly-functional-audit-20260927.md` P1 两行。

## 修复内容

1. **最后心跳不再冒充故障点**（TimelinePage）
   - 时间线 T+0 标记原硬编码 `Crash Point` / `System failure — {Mode}`，但其时间实为 `PreviousSession.LastHeartbeatAt`。现改为本地化的“最后记录”标记，描述明示“最后心跳记录于此；精确故障时间未知 — 主要假设：{0}”。
   - 图例键 `timeline.legend.crashPoint` 更名为 `timeline.legend.lastRecord`，zh/en 文案同步（“最后记录”/“Last record”）；新增 `timeline.marker.lastRecord{,Desc}` 两键。与事件页“最后记录”口径一致。

2. **读取失败可见 + 异步加载**（TimelinePage）
   - `OnLoaded` 原在 UI 线程同步调 `GetIncidents()`（丢弃读取问题）。现走 `ReadIncidentsAsync()`，带加载版本守卫与 `_loading` 防重入；新增 `TimelineReadNotice` 文本块显示 `IncidentReadResult.Summary`（空态/部分失败/全部失败/目录不可用），失败时用 WarningColor 且 ToolTip 展开前 20 条明细。

3. **仪表盘健康卡同口径**（DashboardPage / DashboardHealth）
   - `Refresh` 改 `RefreshAsync`：后台读取 + `IsLoaded`/版本双守卫。空结果分两种：无问题→原“暂无事件”文案；有读取问题→分数 “--” 且摘要/明细可见，**不再把读取失败显示成健康无事件**。部分读取时保留分数，但把缺失记录摘要前置到健康总结。

4. **收口**：`AppService.GetIncidents()` 兼容方法删除，全部调用方（Home/Incidents/Timeline/Dashboard）现消费完整 `IncidentReadResult`。

## 验证（2026-09-28 本机实跑）

| 项 | 结果 |
| --- | --- |
| Desktop Release 构建 | 0 警告 0 错误 |
| Core.Tests | 495 通过 / 0 失败 |
| UI smoke | 440 项 PASS（含 Timeline 源 XAML 渲染快照）；证据 `.mcp-probes/ui-smoke-20260928-155010/` |
| get_diagnostics | 0 项 |

边界：隔离 smoke 未对 Timeline/Dashboard 的新读取通知做交互断言——两页直接取 `App.Current.Service`，在无 App 的 smoke 宿主中不可注入；如需覆盖，须先给这两页做与 `ILiveMonitorEventSource` 同样的抽象（建议列入后续小批次）。真实窗口下的空态/失败态展示仍待人工验收。

## 后续

- 下一批（沿用审计顺序）：Settings“导出默认脱敏”未持久化、导出另存为文件名被忽略、遥测过滤装饰性与英文文案尾巴。
- 游戏内报错“干净结束不生成事件”的产品闭环（人工时间标记/只读时间窗）需要产品决策后另行实施。
- 工作树未提交改动继续累积（本轮 +7 文件），分批提交方案仍待用户确认。
