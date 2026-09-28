# Settings/导出批次修复：默认脱敏持久化与另存为文件名（2026-09-28）

> 范围：`UiSettingsService`、`SettingsPage`、`IncidentsPage`、`DiagnosticExportService` 与新增测试。未启动真实桌面、未触碰原始会话与 incident 数据、未提交 Git。依据 `docs/mcp-crashscope-readonly-functional-audit-20260927.md` P1/P2 两行。

## 修复内容

1. **“导出时默认脱敏”真正生效**
   - `UiSettingsService` 新增 `redactExportsByDefault` 键（默认 true，隐私安全默认值），与托盘设置同文件 `app_settings.json`、同读改写合并、损坏降级策略。
   - `SettingsPage` 加载时显示持久化值（原硬编码 `IsChecked = true`）；“保存设置”按钮现同时持久化该项（setter 仅在值变化时写盘）。
   - `IncidentsPage` 导出复选框首次加载时用持久化默认值播种（`_redactSeeded` 防止后续 reload 覆盖用户本次会话的手动选择）；走 `App.TryCurrent`，UI smoke 的无 App 宿主自动回退 XAML 默认，不破坏隔离夹具。

2. **另存为文件名不再被忽略**
   - `DiagnosticExportService.ExportAsync` 参数 `outputDirectory` 改为 `destinationZipPath`：调用方在保存对话框选定的完整路径原样使用，删除固定 `CrashScope-{id}.zip` 重拼与已无引用的 `SanitizeFileName`。
   - `ZipArchiveMode.Create` 拒绝覆盖已存在文件，而保存对话框已确认过覆盖——导出前先 `File.Delete`。
   - `IncidentsPage` 传 `dialog.FileName` 全路径。

## 验证（2026-09-28 本机实跑）

| 项 | 结果 |
| --- | --- |
| Desktop Release 构建 | 0 警告 0 错误 |
| Core.Tests | **497 通过 / 0 失败**（新增 `DiagnosticExportServiceTests` 2 条：自定义文件名被尊重、对话框确认过的覆盖能替换旧文件） |
| UI smoke | 440 项 PASS；证据 `.mcp-probes/ui-smoke-20260928-155629/` |
| get_diagnostics | 0 项 |

边界：设置页“保存→重开→复选框保持”的端到端持久化路径未在真实窗口验证（SettingsPage 直接依赖 `App.Current`，隔离 smoke 无法承载）；列入人工验收清单。真实导出（含脱敏内容正确性）本轮未运行。

## 后续

- 剩余审计尾巴（P2）：`DiskHealthPage` 等 code-behind 英文状态文本、`AgentActionsViewModel` 状态文字、页面标题/空态一致性。
- 游戏内报错闭环（人工时间标记）与 Timeline/Dashboard 服务抽象仍在待办。
- 工作树未提交改动累积中（本轮 +6 文件），分批提交待用户拍板。
