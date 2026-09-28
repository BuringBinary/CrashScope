# UI 重构验收问题处理记录（2026-09-21 第三轮）

承接 `docs/mcp-ui-refactor-acceptance-review-20260921.md` 第六节的建议顺序，本轮按严重度依次处理：
冒烟门禁 -> 侧边栏溢出 -> 首页首屏 -> Agent 页残留 -> 版本与文档卫生 -> 复验 -> 出包。
分批提交（第六节第 2 项）未执行，等用户确认。

## 1. 冒烟门禁：`App.Current` 硬转型

### 1.1 事实

- `App.xaml.cs:21`：`public static new App Current => (App)Application.Current;`，22 处非空调用。
- `IncidentsPage`/`DashboardPage`/`TimelinePage` 的 `OnLoaded`/`OnUnloaded` 用 `App.Current?.Theme` 订阅主题切换。
  `?.` 作用在属性结果上，对 getter 内部的硬转型无效；冒烟夹具用裸 `Application` 托管编译页，
  首次 `Loaded` 即抛 `InvalidCastException`，后续所有页级与事件交互检查都没有跑。
- `LocalizationService.cs:301-309` 早已用 `Application.Current as App` 的安全写法，本轮向它看齐。

### 1.2 改动

| 文件 | 改动 |
|---|---|
| `src/CrashScope.Desktop/App.xaml.cs` | 新增 `public static App? TryCurrent => Application.Current as App;`，保留 `Current` 不动（改类型会给 22 处调用点引入可空告警，收益为零） |
| `Pages/IncidentsPage.xaml.cs`、`Pages/DashboardPage.xaml.cs`、`Pages/TimelinePage.xaml.cs` | 6 处 `App.Current?.Theme` -> `App.TryCurrent?.Theme` |
| `scripts/test-ui-smoke.sh` | 证据目录改为 `SMOKE_OUT` 可覆盖、默认 `.mcp-probes/ui-smoke-<时间戳>`，不再固定写 `experience-b-20260921`；透传冒烟退出码 |

## 2. 侧边栏：高级组移到最后并压缩节奏

### 2.1 事实

- 1200x750 下左栏路由 ScrollViewer 可用高约 500px，改前内容约 552px：`网速测试` 被裁掉一半且出现滚动条。
- 900x600 下整个工具组不可见。
- `docs/mcp-navigation-ia-refactor-20260921.md` 第 3 节写明高级组在最后，但 `MainWindow.xaml` 里它夹在诊断与工具之间。
  `NavigationLayoutTests` 只断言首页第一、设置最后，不约束组间顺序，移动 XAML 块不影响测试。

### 2.2 改动

| 文件 | 改动 |
|---|---|
| `MainWindow.xaml` | `AgentNavigationToggle` + `AgentNavigationItems` 整块移到 `NavSpeedTest` 之后；品牌区外边距 `22,22,18,12` -> `22,18,18,8` |
| `Themes/ControlStyles.xaml` `NavButton` | `MinHeight` 40 -> 36，`Margin` `0,2` -> `0,1`，`Padding` `14,10` -> `14,8`（行距 44 -> 38） |
| `Themes/ControlStyles.xaml` `NavGroupButton` | `MinHeight` 34 -> 32，`Margin` `0,10,0,2` -> `0,8,0,1` |
| `Themes/ControlStyles.xaml` `SectionLabel` | `Margin` `14,18,0,6` -> `14,12,0,4`（仅左栏使用） |

结果（冒烟截图 `.mcp-probes/fix-20260921/screenshots/`）：1200x750 中英文、深浅主题下 12 个路由 + 4 个组标题全部可见，
`ScrollableHeight` 为 0；900x600 下工具组露出前两项，`网速测试` 与折叠的高级组在折线下、设置仍钉在外侧。
高级组默认折叠且位于末尾，展开只会把内容推向下方，不再遮挡工具组。

## 3. 首页首屏：1200x750 露出 2 条最近事件

### 3.1 事实

产品方案 `docs/mcp-product-experience-plan-20260921.md` 第 5.1 节要求首屏同时看到状态、指标与至少 2 条记录。
改前首屏内容约 700px（视口约 634px），事件列表整体在折线之下，0 条可见。
主要开销：页头 28px 标题 + 三段外边距、录制卡内三行计数把卡高推到约 250px、事件卡里「重新读取」独占一行、
两个常为空的 `AvailabilityText`/`IncidentReadNotice` 仍占一行、事件行内分类与时间分两行。

### 3.2 改动（`Pages/HomePage.xaml`，全部 `x:Name`、Click 处理器与本地化键保持不变）

| 区块 | 改动 |
|---|---|
| 页头 | 标题 28 -> 22px，外边距收紧，副标题 12px |
| 录制卡 | `Padding` 16；状态字 20px；运行时间/采样数/会话三项从卡底移到右侧 `Auto` 列（不再增加卡高）；会话 ID `MaxWidth=150` + 省略号 |
| 指标卡 | `Padding` 12；`MetricValue` 样式 30 -> 26px、外边距 `0,10,0,8` -> `0,4,0,2`（该样式仅首页使用） |
| 事件卡 | 表头改为三列 `标题 | 重新读取 | 查看全部`，删掉独占一行的 WrapPanel；事件行 `Padding` `12,8`，分类与「最后记录」并为一行 |
| 新样式 `AutoHideText` | `Text==""` 时 `Visibility=Collapsed`；用于三处 `AvailabilityText` 与 `IncidentReadNotice`。**未**用于 `ErrorText`/`RecoveryText`，它们由 code-behind 自行设置 `Visibility` |

结果：1200x750 首屏为状态卡 + 三张指标卡 + 2 条完整事件行 + 「进一步排查」标题，尚有余量；900x600 露出 1 条完整事件行。

### 3.3 冒烟新增断言（`test/CrashScope.Ui.Smoke/Program.cs`）

在 1200x750 / 100% 的每个语言 x 主题组合下新增 3 项：第 2 条记录容器已生成；其底边在 `HomeScroll` 视口内
（`TransformToAncestor(home)` 底边 <= `home.ActualHeight`）；`NavHardware` 所在 ScrollViewer 的 `ScrollableHeight < 0.5`。
共 +18 项，368 -> 386。以后任何给首页或左栏加高度的改动都会在此失败，而不是只靠肉眼看截图。

## 4. Agent 页：旧色板与英文硬编码

| 文件 | 改动 |
|---|---|
| `Pages/AgentMonitorPage.xaml.cs` | 连接状态改用 `Loc.F("common.connected")` / `Loc.F("common.notConnectedDetail")`；录制中前景 `#00D68F` -> `SeverityPalette.Resolve("SuccessColor")`（未录制 `TextSecondary`）；「最近更新」改用新键 `agentMonitor.lastUpdate` |
| `Locales/en-US.json`、`Locales/zh-CN.json` | 新增 `agentMonitor.lastUpdate`（`Last update: {0}` / `最近更新: {0}`），parity 443 = 443，双向差集为空 |

**明确推迟**：`AgentActionsViewModel.cs` 的状态栏英文字符串（`journal updated` 等 5 处）未改。
该 VM 与 UI 无关，`test/CrashScope.Core.Tests/ActionsTests.cs:312` 断言其英文内容，应连同测试一起在独立小批次处理，
不与本轮布局改动混在一起。人工清单已把它标为已知项。

## 5. 卫生项

| 项 | 改动 |
|---|---|
| `scripts/publish-test-package.ps1` | 默认 `-BaseVersion` `0.9.2` -> `0.9.10`，并加注释说明为何必须逐包递增（09-21 三个包曾沿用 0.9.2，与真实 0.9.2 游戏会话日志同版本） |
| `docs/mcp-manual-acceptance-checklist.md` | 第 1 节清单改为当前顺序的 12 路由 + 2 窗口，去掉已退役的 RecorderStatus；新增导航/首屏 5 项人工确认；记录 AgentActions 英文为已知项 |
| `docs/mcp-navigation-ia-refactor-20260921.md` | 追加第 9 节勘误：第 6.1 节「冒烟失败与重构无关」不成立 |
| `docs/mcp-ui-refactor-acceptance-review-20260921.md` | 追加第八节处理记录 |

## 6. 复验结果

全部输出在 `.mcp-probes/fix-20260921/`（`--artifacts-path` 隔离，未触碰 `dist/`、旧包与既有证据目录）。

| 项 | 结果 |
|---|---|
| `dotnet build` Desktop Release | exit 0，0 warning（`build-desktop-warnings.txt` 为空） |
| `dotnet build` Ui.Smoke Release | exit 0 |
| Ui.Smoke 运行 | **exit 0，386 PASS**（原 368 + 新增 18），27 张截图（`ui-smoke.log`） |
| `dotnet test` Core.Tests | **495 通过 / 0 失败 / 0 跳过**（`test-core.log`；唯一告警 `RpcTests.cs(151)` CS1998 为既有） |
| `audit-theme-resources.py` | PASS，无未闭环键、主题 parity 完好、无事件泄漏 |
| locale parity | en 443 = zh 443（`locale-parity.txt`） |
| `get_diagnostics` | Desktop 与 Ui.Smoke 均 0 条 |
| 截图人工复核 | `zh-CN-Dark-1200x750`、`en-US-Light-1200x750`、`zh-CN-Dark-900x600`：左栏不溢出、高级组在末尾、首屏 2 条事件；英文长文案无换行 |

冒烟仍是隔离渲染：DPI 为渲染分辨率而非真实 per-monitor 切换；未启动真实窗口、未启停录制。

## 7. 测试包 0.9.10

`scripts/publish-test-package.ps1` 默认参数直接运行（`publish.log`），**PACKAGE VERIFIED**：

| 项 | 值 |
|---|---|
| 目录 | `artifacts/test-packages/20260921-183401/` |
| 版本 | `0.9.10-test.20260921183401`，程序集 `0.9.10.0`，InformationalVersion `+worktree.31f9eab84d67` |
| 压缩包 | `CrashScope.Toolbox.0.9.10-test.20260921183401.win-x64.zip`，158,041,384 字节 |
| SHA-256 | `1864BD328F48F9F88EC1C6B1E51458EB283D32AE3848DAF6E5CA236353F0BE65`（同目录 `.sha256` 文件） |
| 包内回归 | 408 通过（脚本筛选的 34 个测试类，含 NavigationLayout 14、Sidebar 9） |
| 校验 | 976 个文件哈希与清单一致；发布期间源码指纹未变；54 个受保护旧包/事件文件未变 |
| 基线 | `71c0603` + 脏工作树（`dirtyWorkingTree: true`，与前几包一致，提交后应重新出包） |

安装与启动步骤与 `docs/mcp-test-package-guide-20260921-155748.md` 相同，仅目录和版本号不同；
启动器 `Start-CrashScope.cmd` 检测到旧实例在跑会拒绝启动，不会替用户停止录制。

## 8. 未完成与下一步

1. **提交**：工作树 28 M / 2 D / 40 ??，零提交。建议按 主题与样式 / 导航与首页 / Agent 页与 locale / 冒烟与脚本 / 文档 五批提交，需用户确认后执行。
2. `AgentActionsViewModel` 英文字符串 + `ActionsTests.cs:312`，独立小批次。
3. 真实窗口人工验收：按更新后的 `docs/mcp-manual-acceptance-checklist.md` 第 1 节走一遍，重点是导航/首屏 5 项与真实 DPI。
4. 提交后用同一脚本再出一个干净基线的 0.9.11 包替代本 worktree 包。

## 9. 边界

未启动产品或测试包、未启停录制、未提交、未覆盖任何旧包或旧证据；`experience-b-20260921/ui-smoke.log` 已被上一轮失败运行覆盖的事实无法恢复，
仅通过脚本改造保证不再发生。所有源码改动都限于上文列出的文件。
