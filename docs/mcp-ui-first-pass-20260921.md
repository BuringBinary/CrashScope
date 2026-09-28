# CrashScope UI 首轮实施与测试记录

日期：2026-09-21
基线：`feature/local-desktop-work`，HEAD `71c0603`。
方向：用户确认的“精致专业”。本文路径相对于 CrashScope 仓库根目录。

## 1. 结论

主窗口、首页和共享主题基础的第一轮实现及本轮自动化验证已完成，等待用户确认视觉方向与真实应用交互验收。不是全部页面重构完成，也不是发布验收完成。

前一轮因 Cloudflare Tunnel 1033 / HTTP 530 中断。恢复后重新查询工作树、重新读取受影响文件并携带 sha256 版本校验应用补丁；没有盲目重放历史补丁。本轮补齐了滚动条主题、中性空态文案、初始停止状态颜色，并重新执行测试。未进行 Git 提交。

## 2. 已落地范围

| 文件 | 改动 |
| --- | --- |
| `src/CrashScope.Desktop/App.xaml` | 挂载共享控件字典 |
| `src/CrashScope.Desktop/Themes/ControlStyles.xaml` | 标题栏、导航、按钮和 ComboBox 模板从色板抽离，接入动态语义色；卡片、分区标签、指标样式；按钮/导航焦点、按下、禁用状态；垂直/水平滚动条跟随主题 |
| `src/CrashScope.Desktop/Themes/DarkTheme.xaml`、`LightTheme.xaml` | 相同键集合的纯色板，移除重复模板与错误 ARGB 导航背景；石墨灰/浅灰底色与克制紫色强调 |
| `src/CrashScope.Desktop/MainWindow.xaml` | 保留全部 13 个导航入口，按工作空间/实时监控/故障诊断/工具与设置分组；导航可滚动；调整留白与品牌；去掉过时固定版本文案；初始停止状态不标红 |
| `src/CrashScope.Desktop/MainWindow.xaml.cs` | 初始化选择首页与显示页面一致；状态色动态跟随主题；非故障停止不一律标红 |
| `src/CrashScope.Desktop/Pages/HomePage.xaml` | 录制状态主卡、CPU/GPU/内存指标卡、最近事件与精简排查入口；会话/路径/环境折叠展示；保留事件处理入口与绑定 |
| `src/CrashScope.Desktop/Pages/HomePage.xaml.cs` | 使用现有 DesktopSensorMap 与采样事件更新指标；录制状态本地化；主按钮停止操作使用危险样式 |
| `src/CrashScope.Desktop/OverviewMetricText.cs` | 纯指标格式化；停止、缺失、非法数据不伪装成健康零值 |
| `src/CrashScope.Desktop/Locales/en-US.json`、`zh-CN.json` | 新增分组、首屏与录制状态翻译；没有事件时使用中性文案，不暗示录制必然运行 |
| `test/CrashScope.Core.Tests/OverviewMetricTextTests.cs` | 新增 14 个指标展示用例 |
| `test/CrashScope.Ui.Smoke/CrashScope.Ui.Smoke.csproj`、`Program.cs` | 隔离 WPF 源 XAML 加载、资源/对比度/切换/布局检查及截图 |
| `scripts/test-ui-smoke.sh` | Windows Git Bash 下的可复跑入口，支持 DOTNET 环境变量、PATH 或用户目录 SDK |

未更换框架，未修改 Core 采集/诊断代码，未安装或发布，未覆盖既有测试包，未重启用户正在运行的应用，未启停真实录制，未改写用户主题/语言配置。测试输出位于独立 artifacts 目录。

## 3. 验证结果

| 检查 | 本轮结果 |
| --- | --- |
| UI Smoke Release build（含 Desktop 及引用项目） | 成功，0 warning、0 error |
| 发布脚本现有筛选回归 + OverviewMetricTextTests | **311/311 通过，0 失败、0 跳过**；不是全仓库所有测试 |
| 隔离 WPF Smoke | **303 项检查通过，26 张截图** |
| 编辑器诊断 | 在 CrashScope 范围查询 error/warning，返回 0 条；仍以真实构建结果为准 |
| `git --no-pager diff --check` | exit 0；只有 Git 的 LF→CRLF 提示，没有空白错误；该命令不覆盖未跟踪文件 |

回归覆盖原发布筛选集及新增指标用例。新增用例包括停止后的旧值、无采样、越界索引、NaN/Infinity、负数、超范围百分比、真实 0/100% 和内存不受百分比范围限制。

上一轮首次回归构建曾报告未修改的 `test/CrashScope.Core.Tests/RpcTests.cs:151` 的 CS1998；本轮增量回归日志未再次输出该警告，不宣称该旧警告已经修复。

UI Smoke 检查内容：

- 深浅色板键一致；关键主次文本、选中导航、强调文字及焦点对比度。
- 主窗口、首页和共享样式引用的资源键；中英文使用到的翻译键。
- 13 个导航入口保留；最小窗口宽度下首页仍有可用布局。
- 已实例化的按钮和选中导航随主题字典替换更新。
- 无采样时指标显示破折号。
- 最小窗口中底部设置入口通过 BringIntoView 能滚动到达；主题滚动条具有 Thumb。这里是程序化检查，不是鼠标拖拽测试。

### 证据路径

- `.mcp-probes/ui-20260921/build.log`：上一轮独立 Desktop 构建。
- `.mcp-probes/ui-20260921/regression.log`、`regression.trx`：上一轮回归。
- `.mcp-probes/ui-20260921/regression-final.log`、`regression-final.trx`：本轮最终 311 项回归。
- `.mcp-probes/ui-20260921/ui-smoke-build.log`：本轮 UI Smoke/引用项目构建。
- `.mcp-probes/ui-20260921/ui-smoke.log`：303 项检查、26 张截图。
- `.mcp-probes/ui-20260921/diff-check.log`：空白检查输出。
- `.mcp-probes/ui-20260921/screenshots/`：隔离渲染 PNG。
- `.mcp-probes/ui-20260921/artifacts/`：隔离构建输出。

## 4. 视觉审查与测试边界

截图矩阵：中英文 × 深浅主题 × 1200×750/900×600 × 1.00/1.25/1.50 位图渲染比例，共 24 张；另加中文深色的 recording/faulted 两张合成状态示例，共 26 张。

本轮人工查看中文深色默认窗口与故障示例；上轮查看中文深色默认窗口与英文浅色最小窗口，发现的系统浅色滚动条和误导性空态文案已在本轮修正。没有声称人工逐张验收全部 26 张。

探针使用实际源 XAML、共享主题和语言资源，但删除 x:Class 与业务事件绑定，创建普通 WPF Application，而非产品 AppService。只创建屏幕外、不激活、不显示任务栏的测试窗口，支持布局和真实应用资源失效传播；退出时关闭。不会启动采集器、托盘或读取真实会话数据。合成状态截图明确标注为隔离示例。

1.25/1.50 是位图渲染分辨率，不是实际跨显示器 DPI 切换测试。未验证真实按钮点击、键盘焦点流、标题栏/托盘、采集启停、实际数据更新链或常驻性能。共享样式影响其他页面，不能用首页渲染代替其全部布局回归。

历史探针修正：第一版使用未显示窗口导致布局宽度为零；第二版使用脱离窗口的视觉树导致主题失效传播检查失败。最终使用屏幕外连接窗口并推动 Dispatcher 后通过。这是测试宿主设计修正，不应描述为真实产品缺陷修复。

## 5. 复跑方式

在 Windows Git Bash、仓库根目录：

```bash
# 需要 .NET 8 SDK；PATH 没有 dotnet 时脚本尝试 $HOME/.dotnet/dotnet.exe。
# 也可显式设置 DOTNET 为本机 SDK 可执行文件。
bash scripts/test-ui-smoke.sh
```

回归方式：读取 `scripts/publish-test-package.ps1` 的 `$classes` 现有筛选，追加 `OverviewMetricTextTests`，组合 `FullyQualifiedName~类名` 的 OR 筛选；执行 `dotnet test test/CrashScope.Core.Tests/CrashScope.Core.Tests.csproj -c Release --artifacts-path <隔离目录> --filter <筛选> --results-directory <日志目录> --logger 'trx;LogFileName=regression-final.trx'`。本轮仅使用筛选，没有运行发布脚本。

Smoke 脚本复跑会更新其日志与截图，需保留历史证据时先使用新的输出目录或复制旧证据；不要把覆盖后的输出当成不同版本的独立历史证据。

## 6. 后续顺序与验收门槛

1. **视觉确认**：先由用户确认主窗口/首页方向，不立即把审美尚未确认的布局推广到全部页面。
2. **真实窗口 smoke**：在不影响现有录制的前提下，确认独立候选版本运行方案；检查导航、标题栏、长英文文案、键盘操作、下拉列表、滚动、深浅切换和实际 DPI。需要重启/切换现有应用时先确认。
3. **下一批迁移**：仪表盘（图表轴线/文字/网格的主题热切换，保留历史数据）→ 故障列表/详情（证据、置信度、下一步操作）→ 硬件/磁盘/工具/设置 → 辅助窗口。不是重新修改采集和诊断模型。
4. **后续交付门槛**：每批都有前后截图、正常/空态/错误态、双语言和深浅主题检查、相关自动回归；完成后再生成新的独立候选包，不覆盖旧包。

本轮状态：实现与所列自动检查完成，真实 GUI/用户视觉确认仍待进行。不得把此记录解读为全 UI 重构或可发布版本已经验收。
