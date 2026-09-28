# 分批提交方案（2026-09-28，待用户批准后执行）

> 现状：`feature/local-desktop-work`，HEAD `71c0603`（9-20），领先 origin 10 个提交；工作树 **91 条改动**（39 修改、2 删除、50 未跟踪，其中 25 个为 docs）。本文只是方案，**尚未执行任何 `git add/commit/reset/clean`**。独立备份见 `docs/mcp-stage0-baseline-backup-20260927.md`（9-28 三轮修复晚于该备份，回退时以工作树为准）。

## 执行前决策点（需用户确认）

1. **行尾**：本轮 MCP 编辑的 9 个文件为 LF，Git 提示下次触碰会按现配置转 CRLF。建议提交时执行一次 `git add --renormalize .`（或先接受警告原样提交），二选一。
2. **杂物**：仓库根 `_o.txt` 为探针残留，建议直接删除，不入库。
3. **推送**：全部提交完成后是否 `push origin`（含此前领先的 10 个提交）由用户决定；本方案不含推送。

## 推荐切分（方案 A：每批提交点均可独立构建 + Core 测试通过）

| 批次 | 主题 | 文件 | 提交后验证 |
| --- | --- | --- | --- |
| **1** | 数据/服务契约 | `Core/Monitoring/RecorderEngine.cs`（暴露 SampleInterval）、`AppService.cs`、`ILiveMonitorEventSource.cs`*、`IncidentCatalogReader.cs`*、`DiagnosticExportService.cs`、`Services/UiSettingsService.cs`、`RecorderOperationGate.cs`*（被 AppService 引用，须同批）＋测试 `IncidentCatalogReaderTests`*、`DiagnosticExportServiceTests`*、`RecorderOperationGateTests`* | build + Core |
| **2** | 主题与展示基础（纯类/资源，无页面） | `Themes/ControlStyles.xaml`*、`DarkTheme/LightTheme/CommonResources.xaml`、`Services/ThemeService.cs`、`SeverityPalette.cs`*、`ChartPalette.cs`*、`IncidentPresentation.cs`*、`LiveSamplePresentation.cs`*、`OverviewMetricText.cs`*、`NavigationLayout.cs`*、`SidebarRecorderPresentation.cs`*＋对应 10 个测试文件* | build + Core |
| **3** | UI 外壳、页面与文案（体验 A/B/C/C1 + 9-28 三轮修复） | `App.xaml{,.cs}`、`MainWindow.xaml{,.cs}`、`FloatingBarWindow.xaml`、`RecorderStatusPage.xaml{,.cs}` **删除**、`Pages/` 全部修改（Dashboard/Home/Incidents/LiveMonitor/Timeline/Settings/DiskHealth/AgentActions/AgentIncidents/AgentMonitorPage）、`RawSensorWindow.xaml.cs`、`AgentActionsViewModel.cs`、`AgentMonitorViewModel.cs`、`Locales/zh-CN.json`、`Locales/en-US.json`、`ActionsTests.cs` | build + Core |
| **4** | 冒烟与工程脚本 | `test/CrashScope.Ui.Smoke/`* 全目录、`scripts/test-ui-smoke.sh`*、`scripts/audit-theme-resources.py`*、`scripts/publish-test-package.ps1` | build + Core + **UI smoke 440** |
| **5** | 文档 | `docs/mcp-*.md` 共 25 个（含本文） | 无需构建 |

\* = 新增文件。外壳与页面合为批次 3 的原因：`MainWindow` 新版引用页面新成员（如 `HomePage.CaptureNavigationState`）、页面引用新 locale 键，拆开则中间提交点不可独立构建；牺牲粒度换取每个提交都能检出、构建、回退和 bisect。

> 若接受“中间提交不保证独立可构建、仅分支末态验证”，批次 3 可再细分为 外壳（App/MainWindow/Floating）→ 页面 A/B → 页面 C/C1+VM+locale 三小批（方案 B）；不推荐，回退价值低于成本。

## 建议提交信息（zh，可改英文）

1. `core+desktop: 数据契约——采样间隔暴露、事件读取结果化(IncidentReadResult)、导出路径与 UI 设置持久化`
2. `desktop: 主题重构(纯色板+ControlStyles)与展示逻辑抽纯类(含10组单测)`
3. `desktop: 导航IA/页面体验A-B-C-C1 + P0/P1/P2修复(LiveMonitor退订、时间锚点、读取失败可见、脱敏持久化、另存为、文案双语)`
4. `test+scripts: WPF隔离冒烟(440项)与主题审计/出包脚本`
5. `docs: MCP 工作记录与交接文档(20260918-20260928)`

## 执行序列（获批后照做）

```
rm _o.txt                                # 决策点 2
git add --renormalize <批次文件…>         # 决策点 1（每批执行）
git add <批次文件…> && git commit
<每批验证：dotnet build -c Release；dotnet test Core.Tests；批4后加跑 UI smoke>
git status --porcelain                   # 全部批次后应为空
```

收尾：用 `scripts/publish-test-package.ps1` 出 **0.9.11 干净基线包**，核对 `build-info.json` 的 `dirtyWorkingTree=false` 与提交哈希，再进入 12 路由 + 2 窗口人工验收。

## 边界

本轮只读 Git 与文件、写本方案文档；未提交、未改源码、未运行构建。批次映射基于 2026-09-28 16:10 的 91 条 `git status` 快照，执行前应重新核对清单是否有新增改动。
