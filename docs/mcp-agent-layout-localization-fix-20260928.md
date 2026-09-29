# Agent 窄窗布局、诊断提示与剩余本地化修复（2026-09-28）

## 范围与结论

用户明确选择继续修剩余问题，先不录制、不替换旧包。本轮在此前空态/图标修复基础上继续修改源码，处理上一轮验收的 Agent 按钮裁切、错误提示截断、RawSensor 的 All 和打包说明不一致。

这些问题已完成源码修复和限定范围回归；**没有重新打包或在新应用进程中完成实机复验**。正在运行的旧 0.9.11 不会自动获得本次修改。

## 变更

### 1. Agent 操作页 900×600 按钮裁切

文件：`src/CrashScope.Desktop/Pages/AgentActionsPage.xaml`。

- 电源计划输入/应用行改为自适应 Grid，输入框使用剩余宽度。
- 优先级的下拉和应用按钮位于第一行，原因输入框独占第二行，不再横排挤占按钮空间。
- 回滚原因与按钮使用自适应 Grid；关键按钮命名，便于布局回归。
- 操作区域有独立纵向滚动视口，内部面板保持最低 360 高度，避免连接错误展开后继续压缩主要操作。
- 内容较高时允许纵向滚动，不承诺所有按钮同时出现在第一屏；测试验证按钮逐一滚动后可达且横向不裁切。

### 2. 三个 Agent 页的连接栏与长错误

文件：`Pages/AgentMonitorPage.xaml`、`Pages/AgentIncidentsPage.xaml`、`Pages/AgentActionsPage.xaml`（均位于 `src/CrashScope.Desktop/`）。

- 连接控件改用 WrapPanel，窄窗时换行。
- 按钮固定宽度改为最小宽度，让英文标签能按内容扩展。
- 状态详情移至独立行，使用只读 TextBox；文本可选中，支持标准复制操作，自动换行，最大高度 64 并可纵向滚动。
- Tooltip 保留完整文本，不再将长路径挤进连接按钮右侧剩余的一小块区域。
- 页面副标题支持换行。
- `AgentMonitorViewModel.cs` 与 `AgentActionsViewModel.cs` 的发现失败分支增加本地化说明，同时原样保留底层错误详情。英文原始诊断有意保留，未做脆弱的字符串替换，也未修改 RPC 协议或连接成功行为。

### 3. 原始传感器筛选项

文件：`src/CrashScope.Desktop/RawSensorWindow.xaml`、两份 `Locales/*.json`。

- 用 ItemTemplate 把内部字符串 `All` 显示为中文“全部”或英文“All”。
- 内部筛选值仍是 `All`，不把中文值传给筛选逻辑，也不改传感器硬件类型。
- 本轮没有修改 RawSensorWindow 的采集/刷新行为。

### 4. 打包 README

文件：`scripts/test-package-readme.zh-CN.txt`。

- 去掉“此包来自包含未提交修复的工作树”这一不随构建状态变化的断言。
- 是否有未提交修改，明确指向 `build-info.json` 的 `dirtyWorkingTree`。
- 去掉要求从当前首页确认版本的过时描述，保留程序集版本占位符和真实元数据/日志位置。
- 未录制检查说明改为中文，并明确实际开始录制需要确认。
- 既有包目录内的 README 没有回写；下一次发布才会使用新模板。
- 收尾发现模板被 `.gitignore` 的 `test-*.txt` 误忽略；已增加仅针对该模板的例外规则，避免后续提交漏掉发布输入。模板现在会出现在未跟踪文件列表中，未执行暂存或提交。

## 回归证据

### UI 与资源/布局

命令：

```bash
SMOKE_OUT=.mcp-probes/agent-layout-fix-20260928-v2 bash scripts/test-ui-smoke.sh
```

结果：构建 0 warning / 0 error；**1205 项检查通过**。

新增 `test/CrashScope.Ui.Smoke/AgentLayoutChecks.cs`：

- 中文/英文 × 深色/浅色 × 900×600 / 1200×750，覆盖三个 Agent 页。
- 每个语言/主题组合 93 项，新增布局部分共 372 项。
- 验证长诊断原文末尾保留、只读/换行/滚动属性、边界不越出视口。
- 验证连接、开始/停止、应用、回滚等按钮的横向边界、滚动后纵向可达性和标签内容宽度；**不点击这些业务按钮**。
- 验证优先级原因行有足够宽度、`Normal` 协议 Tag 不变。
- 验证 All 的本地化展示和内部值不变，以及 CPU 筛选项未被改写。
- 增加 README 元数据指向及四个受支持占位符的检查。

输出共 59 张图：26 张既有源 XAML/状态样板图、24 张 Agent 页布局图、9 张编译后交互图。复核了 900×600 中文浅色 AgentActions 和英文深色 AgentMonitor；优先级应用按钮完整显示，连接栏换行及长错误滚动有效。

**新 Agent/RawSensor 检查解析源 XAML并移除业务事件，不构造页面 ViewModel，不连接真实 Agent。** 之前的 LiveMonitor/Incident 编译后合成交互检查也继续通过。不能把这些截图称为新发布包的真实窗口验收。

主要证据：`.mcp-probes/agent-layout-fix-20260928-v2/ui-smoke-build.log`、`ui-smoke.log`、`screenshots/`。v1 是初次回归，最终 UI 结果以 v2 为准。

### 两个定向 ViewModel 测试

只运行：

- `AgentMonitorViewModelTests.Connect_MissingEndpoint_ReportsFriendlyError`
- `AgentActionsViewModelTests.ConnectMissingEndpoint_KeepsDiagnosticDetail`

结果：**2 passed / 0 failed / 0 skipped**。测试在临时空目录上调用发现失败路径，验证友好提示与 `rpc-endpoint`、`not running` 原始细节同时保留，不创建录制引擎或运行系统操作。

证据：`.mcp-probes/agent-layout-fix-20260928-v1/core-results/agent-missing-endpoint.trx`。

构建整个测试程序集时出现未修改的 `test/CrashScope.Core.Tests/RpcTests.cs:151` 的 CS1998 警告；定向测试通过，但不能把这次测试程序集构建称为零警告。

没有运行完整 Core 套件，因为其中包含启动录制器、修改测试进程优先级等超出本轮边界的用例。

## 收尾与仍待验证

- Desktop 编辑器错误/警告诊断为 0；最终文件与旧包完整性核验见本轮 `final-verification.json`。
- 未触碰现有用户录制数据，未对正在运行的旧窗口发出业务操作，未提权、安装服务或修改系统主题。
- 保留前两轮未提交修复和报告；本轮也未提交、推送或重新打包。
- 原验收报告中的发现属于旧包事实；本记录表示源码处理情况，不追改旧包验收结论。
- 仍需独立新包的真实窗口复验，尤其是已连接 Agent、真实大列表、键盘/剪贴板交互、真实 DPI 与端到端采样。尚未证明真实录制数据链路正常。
