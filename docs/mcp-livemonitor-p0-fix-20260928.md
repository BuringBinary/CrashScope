# LiveMonitor P0 修复收尾与验证（2026-09-28）

> 范围：仅 `src/CrashScope.Desktop/Pages/LiveMonitorPage.xaml.cs` 一处守卫补强，以及构建/测试验证。未启动真实桌面、未触碰原始会话与 incident 数据、未提交 Git。

## 背景

2026-09-27 审计（`docs/mcp-crashscope-readonly-functional-audit-20260927.md`）列出的两处 LiveMonitor P0：

1. 过滤后空列表仍访问 `Items[0]`，可致 Dispatcher 异常并触发 `App.xaml.cs` 的 `Shutdown(1)`；
2. 构造时订阅六个 `AppService` 事件且无退订，配合 `MainWindow` 每次导航新建页面导致泄漏。

本轮开工时发现工作树中已存在未记录的修复：`ILiveMonitorEventSource` 抽象、Loaded/Unloaded 成对订阅、`_listening` 守卫、`RefreshEvents` 判空，以及 `test/CrashScope.Ui.Smoke/LiveMonitorInteractionChecks.cs` 回归检查。该轮工作没有留下验证记录，本文补齐。

## 本轮发现并修复的残余缺陷

首次重跑 UI smoke 失败于 “queued events do not update the detached page”：事件回调在导航前已通过 `Dispatcher.InvokeAsync`（Normal 优先级）入队，而 `Unloaded` 广播只在 Loaded 优先级运行，`_listening` 复位晚于回调执行，旧页仍被更新。

修复：`AddEvent` 回调守卫增加 `Parent is null` 判断。`MainWindow` 切页用 `ContentArea.Children.Clear()`，会同步清空 `Parent`，因此该检查能在 `Unloaded` 尚未派发的窗口期内可靠拦截；原 `_listening`/`IsLoaded` 检查保留。

## 验证（2026-09-28 本机重跑）

| 项 | 结果 |
| --- | --- |
| `dotnet build src/CrashScope.Desktop -c Release` | 成功，0 警告 0 错误 |
| `dotnet test test/CrashScope.Core.Tests -c Release` | 495 通过 / 0 失败 / 0 跳过 |
| `scripts/test-ui-smoke.sh` | 440 项检查全部 PASS，其中 live-monitor 54 项（含：过滤后空列表安全、六事件订阅在导航离开后归零、20 次反复导航无重复订阅、排队事件不更新已分离页面）；证据目录 `.mcp-probes/ui-smoke-20260928-144454/` |

边界：隔离 smoke 使用合成事件与离屏宿主，不代表真实桌面端到端验收；12 路由 + 2 窗口人工清单仍未对当前工作树执行。

## 状态与后续

- 两处 P0 至此在源码与自动化层面闭环；工作树仍有大量未提交改动（含本修复），提交切分方案见 `docs/mcp-stage0-baseline-backup-20260927.md` 第 4 节，需用户决定。
- 下一优先级（沿用审计顺序）：P1 信任链——Timeline 把最后心跳标成 Crash Point 的锚点/文案、`GetIncidents()` 丢失读取错误；随后 Settings 脱敏持久化、另存为文件名、遥测过滤与文案尾巴。
