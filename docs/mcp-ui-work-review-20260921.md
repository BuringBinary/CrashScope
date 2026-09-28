# UI 工作评审与语义色收敛

日期：2026-09-21
范围：CrashScope 工作区分支 feature/local-desktop-work，HEAD 71c0603 之上的未提交改动，以及本轮据评审结论实施的修复。
本文路径相对于 CrashScope 仓库根目录。

## 一、结论

上一轮 UI 工作的方向和质量都成立，不需要返工。它把「改颜色」升级成了「抽presentation 层 + 建样式基础设施」，这是正确的量级。

但它没有完成自己设定的目标。`docs/mcp-ui-refactor-progress-20260921.md` 列出的遗留项里，有两项被报告为已修而实际只是换了位置或引入了新风险。本轮修掉了这两项，并把语义色收敛工作做到全量闭合。

**重要边界：本轮没有构建、没有跑测试、没有启动程序。** 这台机器上找不到 dotnet（详见第四节），所有结论均来自静态核查与 VS Code 语言服务诊断。构建与测试仍是未验证状态。

## 二、上一轮工作的评价

### 做得好的部分

| 项 | 证据 | 评价 |
| --- | --- | --- |
| 主题文件瘦身为纯色板 | `src/CrashScope.Desktop/Themes/DarkTheme.xaml` 与 `LightTheme.xaml` 各 34 行，仅 SolidColorBrush/Color | 职责清晰，两份主题键完全对齐，无 dark-only / light-only 缺口 |
| 样式抽出为独立层 | 新增 `src/CrashScope.Desktop/Themes/ControlStyles.xaml`，370 行 13 个 x:Key | 零硬编码颜色；43 处 DynamicResource 对 8 处 StaticResource，且后者全是 style-to-style 引用，安全 |
| presentation 逻辑脱离 code-behind | 新增 7 个纯类 + 7 个对应测试文件 | 可在无 WPF 宿主的情况下测试 UI 判定逻辑，这是此前缺失的能力 |
| 状态语义正确 | `src/CrashScope.Desktop/OverviewMetricText.cs` | 缺失/停止/越界一律返回破折号，不伪装成健康的 0，与产品计划「无数据不得伪装成健康」一致 |
| 不臆断因果 | `src/CrashScope.Desktop/IncidentPresentation.cs` | 时间标注为「最后记录」并保留原始时间戳，支持度明确标注「不是故障概率」，未把心跳时间改名为故障时间 |
| ID 解析防御 | `IncidentPresentation.Resolve` | 仅解析唯一精确匹配，注释明示不把导航文本当路径 |
| 并发防抖 | `src/CrashScope.Desktop/RecorderOperationGate.cs` | Interlocked 去重，重复点击不会重复启动，对应产品计划的验收条款 |
| 导航分组与滚动 | `src/CrashScope.Desktop/NavigationLayout.cs`、`MainWindow.xaml:96` | 13 项归入 6 组，已加 ScrollViewer，小窗口可达性问题已解决 |
| 本地化闭合 | 两份 Locales 各 447 键，零差异 | `IncidentsPage.xaml.cs` 中此前的英文硬编码已清零 |

### 报告为已修、实际未修的部分

**1. ARGB 解析 bug 没有消失，只是转移了。**

进度文档称已修正 `DarkTheme.xaml` 的 `#6C5CE722`。该字面量确实不在主题文件里了，但 8 位十六进制值仍以 WPF ARGB 语义存在于页面中：

- `src/CrashScope.Desktop/Pages/DashboardPage.xaml:30,86,160,272,322` — `#FF6B6B22`、`#00D68F22`、`#FFD93D22`
- `src/CrashScope.Desktop/FloatingBarWindow.xaml:8,13,16` — `#E610141E`、`#33FFFFFF` 等

这些写法本意多半是「颜色 + 22 透明度」，但 WPF 读作 A=FF/00/FF、R/G/B 顺移，得到的是完全不同的不透明色。原始缺陷类别依然存在。

**2. 语义色硬编码是主要缺口。** 详见第三节。

**3. 主题切换存在字典顺序缺陷（本轮新发现，非上一轮引入但被其放大）。**

`App.xaml` 的合并顺序是 CommonResources → DarkTheme → ControlStyles。而 `ThemeService.ApplyTheme` 原实现是 Remove 旧主题再 Add 新主题，Add 追加到末尾，切换后顺序变成 CommonResources → ControlStyles → LightTheme。

WPF 的 MergedDictionaries 后者覆盖前者，`ControlStyles.xaml` 的 DynamicResource 又是运行时解析，因此这个顺序反转在当前键集合下不会立刻产生可见错误。但它把「样式依赖色板」的层级关系倒置了，一旦后续在 ControlStyles 中定义任何与色板同名的键，切换主题就会产生难以定位的偶发错误。上一轮把 370 行样式集中到 ControlStyles，恰好显著抬高了这个风险。

## 三、本轮实施的修复

### 1. 主题字典顺序（`src/CrashScope.Desktop/Services/ThemeService.cs`）

改为原地替换：定位现有 `*Theme.xaml` 的索引，用 `MergedDictionaries[slot] = replacement` 替换，而不是 Remove + Add。色板始终保持在 ControlStyles 之前。仅当不存在旧主题时才回退到 Add。

### 2. 语义色统一（新增 `src/CrashScope.Desktop/SeverityPalette.cs`）

问题：严重度颜色散布在 6 个 code-behind 文件中，以 `ColorConverter.ConvertFromString("#RRGGBB")` 形式硬编码。后果是主题切到浅色时，列表徽标、趋势箭头、对比面板仍保持深色主题的配色，与 P1 出口条件「深浅切换无残留」直接冲突。

方案：两份主题各新增 6 个严重度键（SeverityCritical / High / Medium / Low / Info / Neutral，浅色版使用降低明度、提高对比的对应色），由 `SeverityPalette` 通过 `TryFindResource` 解析。

- 提供 `KeyForMode` / `KeyForCategory` / `KeyForPriority` 三个纯映射函数，可脱离 WPF 测试。
- 资源缺失时回退到冻结的中性画刷，不抛异常。
- 未知 mode 与未知 category 一律降级为 Neutral，不会把未识别项误报成 Critical。

改造范围（全部 code-behind 硬编码色已清零）：

| 文件 | 改造点 |
| --- | --- |
| `src/CrashScope.Desktop/Pages/IncidentsPage.xaml.cs` | GetModeBrush、建议优先级、对比面板 GPU/ETW-GPU/ETW-WHEA/虚拟显示器、稳定性评分、趋势箭头 |
| `src/CrashScope.Desktop/Pages/TimelinePage.xaml.cs` | GetCategoryBrush、崩溃点标记 |
| `src/CrashScope.Desktop/Pages/DashboardPage.xaml.cs` | GPU 跃迁标记、健康卡评分与趋势（含无数据分支） |
| `src/CrashScope.Desktop/Pages/RecorderStatusPage.xaml.cs` | 录制状态点改用 SetResourceReference("SuccessColor")，上次会话非正常结束提示 |

### 3. 优先级标签本地化

建议列表的优先级标签此前是代码里写死的英文 CRITICAL / HIGH / MEDIUM / LOW。已改为走 `eventUi.priority.*`，两份 Locales 同步新增 4 键，总数 447 → 451，仍然零差异。

### 4. 类型与清理

- `IncidentListItem.ModeBackground` 由 `SolidColorBrush` 放宽为 `Brush`。这是必须的：`GetModeBrush` 现在返回资源查出的 `Brush`，不改会编译失败。注意 VS Code 诊断当时报 0 条，该错误是靠人工类型核查发现的，不能把空诊断当作构建通过的证据。
- 移除 4 个文件中随之失效的 `Color` / `ColorConverter` using。
- 删除本轮产生的 `_review_*.txt` 探针文件，按仓库规约不进 docs/。

### 5. 新增测试（`test/CrashScope.Core.Tests/SeverityPaletteTests.cs`）

覆盖：全部 mode 与 category 映射到已声明键、优先级分档边界值（100/90/89/75/74/55/54/0/-5）、未知值降级为 Neutral 而非 Critical、硬故障严重度高于应用级故障、资源缺失时回退画刷不抛异常且已冻结。

## 四、未验证项与阻塞（重要）

**构建和测试本轮没有执行，状态未知。** 通过 `run_command` 在目标机上尝试 `dotnet build` 与 `dotnet test`，返回 `dotnet: command not found`（exit 127）。进一步核查：

- `/c/Program Files/dotnet` 不存在。
- PATH 中仅有 `/c/Users/BuringBinary/.dotnet/tools`（工具目录），无 SDK 本体。
- `powershell.exe -Command "(Get-Command dotnet).Source"` 无输出。

所以以下全部为未验证状态，不能据本文声称通过：

- 编译是否成功（VS Code 诊断返回 0 条 error/warning，但该信号不可靠——第三节第 4 点的类型错误就未被它捕获）。
- 现有 237 项回归测试是否仍然全绿。
- 新增的 `SeverityPaletteTests` 是否通过。
- 主题切换的实际视觉效果、深浅两套严重度色在真实窗口下的对比度。

需要在具备 .NET SDK 的机器上补做：`dotnet build` → `dotnet test` → 手工切换深浅主题走查事件页、时间线页、仪表盘、录制页。

## 五、仍然开放的问题

按建议处理顺序排列：

1. **XAML 中的 8 位 ARGB 字面量**（`DashboardPage.xaml` 5 处、`FloatingBarWindow.xaml` 3 处）。需要逐处确认原始设计意图是不透明色还是半透明叠加，再决定改为主题键还是显式 `Opacity`。不宜机械替换。
2. **OxyPlot 图表颜色**（`DashboardPage.xaml.cs` 9 处 `OxyColor.FromRgb` 静态字段）。这是进度文档已列出的遗留项，本轮未动。图表需要在主题切换时重建或重新着色，属于独立改动，不应混进本轮。
3. **页面级 `#0F0F1A` 等深色面板底色**（RecorderStatusPage、SettingsPage、TimelinePage、LiveMonitorPage、AgentIncidentsPage、DashboardPage 共约 20 处）。这些在浅色主题下会保持深色，是 P2「按用户价值迁移页面」的范围。
4. **ThemeService 仍只有 Dark/Light**，无跟随系统主题。注意不要与已完成的「跟随系统语言」混淆。
5. **FloatingBarWindow 是否固定深色**需要产品决策，进度文档已标注为待单独确认。

## 六、对下一步的建议

不建议立刻开始 P2 的页面批量迁移。当前更高价值的是先关闭验证缺口：

1. 在有 SDK 的环境跑通构建与全量测试，确认本轮改动没有回归。
2. 手工验收深浅主题切换，重点看严重度徽标、趋势箭头、对比面板——这正是本轮改动的作用面。
3. 之后再处理第五节第 1、2 项（ARGB 字面量与图表颜色），它们同属「颜色正确性」范畴，适合一批处理。
4. 页面底色迁移（第 3 项）放到最后，量大但风险低，且应配合 P0 的样板批准再动。

## 七、第二批：颜色正确性收敛（同日追加）

范围：`docs/mcp-ui-work-review-20260921.md` 第五节第 1、2 项。同样未构建、未测试，原因见第四节。

### 1. XAML 的 8 位 ARGB 字面量

先做了一次甄别，结论是这批字面量分两类，不能一起处理：

| 位置 | 字面量 | 判定 |
| --- | --- | --- |
| `Pages/DashboardPage.xaml` 5 处 | `#FF6B6B22`、`#00D68F22`、`#FFD93D22` | **缺陷**。写法是「旧色板 + 22」，意图是 13% 透明的图标底色，但 WPF 读作 `#AARRGGBB`，得到 A=FF/00 的不透明色。GPU 卡那处 A=00 实际完全透明，RAM/告警那处则是刺眼的不透明黄。 |
| `FloatingBarWindow.xaml` 3 处 | `#E610141E`、`#33FFFFFF`、`#F0E8ECF4`、`#88E8ECF4` | **不是缺陷**。alpha 在首字节，语义正确，是悬浮窗刻意的半透明与描边。本轮不动。 |

对第一类：两套主题各新增 4 个 tint 键（`CpuTint` / `GpuTint` / `RamTint` / `WarningTint`），深色用 `#26` alpha、浅色用 `#1F`，alpha 写在正确位置，色相跟随各自主题的语义色。5 处 Border 改为 `DynamicResource`。

`FloatingBarWindow` 是否统一为主题化深色仍是待产品确认项，维持原状。

### 2. OxyPlot 图表主题化

核查时发现一个比「颜色写死」更关键的事实：`ThemeService.ThemeChanged` 事件**此前没有任何订阅者**。图表颜色即便改成资源引用也不会在切换时更新，因为 OxyPlot 不参与 WPF 的 DynamicResource 机制。所以这一项必须同时解决取色和重着色两件事。

新增 `src/CrashScope.Desktop/ChartPalette.cs`：

- 9 个图表键映射到既有语义色（`CpuColor`/`GpuColor`/`RamColor`/`AccentColor`/`WarningColor`/`DangerColor`/`SuccessColor`/`BorderColor`/`TextSecondary`），不新造平行色板。
- `Resolve` 经 `TryFindResource` 取色，缺失时回退中性灰，不抛异常也不返回透明。
- `ToOxy` 保留 alpha 通道。
- 暴露 `RequiredKeys` 供测试与主题审计使用。

`Pages/DashboardPage.xaml.cs` 改造：

- 9 个 `static readonly OxyColor` 字段改为按主题解析的属性。
- `OnLoaded` 订阅 `ThemeChanged`、`OnUnloaded` 退订，沿用该文件既有的 `-=` 后 `+=` 防重复模式。
- 新增 `ApplyChartTheme()`：重解析线条色、散点标记、坐标轴刻度/轴线/文字/标题色，双轴图按 `ClockAxis`/`PowerAxis` 分别着色，最后 `InvalidatePlot(false)`。
- **`InvalidatePlot(false)` 的参数是刻意的**：`false` 表示不更新数据，只重绘。主题切换因此不会清空图表历史，符合 P3「主题切换不清空历史图表」的约束。
- 顺带移除硬编码的 `OxyColors.White` 散点描边，改为主题中性色；删除随改造失效的 `TransitionSurgeColor`。

### 3. 测试

新增 `test/CrashScope.Core.Tests/ChartPaletteTests.cs`，全部为非可视测试：

- 两套主题分别包含全部 9 个图表键。
- 两套主题键集合完全一致（当前 41 = 41）。
- 严重度 6 键与 tint 4 键在两套主题中均声明。
- `RequiredKeys` 无重复、无空值。
- 资源缺失时回退色 alpha 大于 0，即不会退化成隐形图表。
- `ToOxy` 的 alpha 保真。

定位主题文件采用从 `AppContext.BaseDirectory` 逐级上溯找 `src/CrashScope.Desktop/Themes` 的方式，已按真实目录结构验证可解析到 `D:\workspace\workspace\CrashScope\src\CrashScope.Desktop\Themes`；找不到时回退到输出目录下的 `Themes`（csproj 有 PreserveNewest 拷贝）。

### 4. 测试工程配置

`test/CrashScope.Core.Tests/CrashScope.Core.Tests.csproj` 显式加 `<UseWPF>true</UseWPF>`。原因：本轮两个新测试是该工程**首次**直接引用 WPF 类型（`SolidColorBrush`、`Color`），此前全部测试都只用 Core 的纯模型。依赖 Desktop 传递 FrameworkReference 虽然通常有效，但属于隐式行为，显式声明更稳妥。

### 5. 本批次后的状态

- code-behind 硬编码 hex：**0 处**。
- XAML 中语义错误的 ARGB：**0 处**（FloatingBar 的 3 处为有意半透明，保留）。
- 两套主题键：41 = 41，完全对齐。
- VS Code 诊断：0 error / 0 warning。同样提醒：上一批的类型错误未被诊断捕获，此信号不能替代构建。

### 6. 仍未关闭

- **构建与测试依然未执行**，阻塞原因不变（目标机无 .NET SDK）。本批新增 1 个源文件、1 个测试文件、1 处 csproj 变更，均需在有 SDK 的环境验证。
- 页面级 `#0F0F1A` 等固定深色面板底色约 20 处，仍属 P2 范围，未动。
- `ThemeService` 仍只有 Dark/Light，无跟随系统主题。
- `FloatingBarWindow` 是否固定深色待产品确认。

建议下一步仍是先补构建与测试验证，再动 P2 的页面底色迁移——后者量大，在没有回归保护的情况下批量改动风险不对等。

## 八、构建与测试验证（同日，纠正第四节）

### 8.1 第四节的结论是错的

第四节称「这台机器上找不到 dotnet」，据此把构建与测试全部标为不可验证。**该结论不成立。**

当时只查了 `/c/Program Files/dotnet` 和 PATH，就下了判断。实际 SDK 装在用户目录：

```text
/c/Users/BuringBinary/.dotnet/dotnet.exe
SDK 8.0.425
Microsoft.WindowsDesktop.App 8.0.31
```

PATH 里只有 `~/.dotnet/tools`（工具目录），本体目录未加入 PATH，所以 `dotnet` 命令不可直接调用——但 SDK 一直都在。正确做法是显式 `export PATH="/c/Users/BuringBinary/.dotnet:$PATH"`。

教训：把「命令不在 PATH」当成「SDK 不存在」是过早结论，本可以在第一批就完成验证。

### 8.2 实测发现的编译错误

在真正跑构建之前，先按第三节第 4 点的教训做了一次类型歧义审计，发现**前两批引入了真实的 CS0104 编译错误**。

根因：`src/CrashScope.Desktop/CrashScope.Desktop.csproj` 同时启用 `UseWPF` 和 `UseWindowsForms`，生成的隐式 global using 同时包含 `System.Drawing` 与 `System.Windows.Forms`。于是裸写的 `Color`、`Brush`、`Application` 全部二义。这正是该项目每个 code-behind 顶部都挂着一排别名的原因，而我新增的文件一个别名都没有。

| 文件 | 问题 | 处理 |
| --- | --- | --- |
| `src/CrashScope.Desktop/SeverityPalette.cs` | 裸用 `Application` / `Brush` / `Color` | 补三个 WPF 别名 |
| `src/CrashScope.Desktop/ChartPalette.cs` | 裸用 `Application` / `Color` | 补两个 WPF 别名 |
| `src/CrashScope.Desktop/Pages/IncidentsPage.xaml.cs` | 上一批删 `Color`/`ColorConverter` 别名时，新引入的裸 `Brush` 无别名可依 | 补 `Brush` 别名 |

同时把 `SeverityPalette.Brush(...)` 更名为 `SeverityPalette.Resolve(...)`（43 处调用同步更新）。方法名与返回类型同名虽然合法（C# 的 color color 规则），但在本项目这种别名密集、类型二义的上下文里过于脆弱，改掉更稳妥。

### 8.3 我自己引入的回归：UseWPF

第七节第 4 点给测试工程加 `<UseWPF>true</UseWPF>`，理由是「显式优于隐式」。**这个改动是错的，已回滚。**

在 .NET SDK 中，`UseWPF` 会把隐式 using 集合切换为 WPF 变体，其中**不含 `System.IO`**。测试工程原本依赖隐式 `System.IO` 使用 `Path` / `Directory` / `File`，加上该属性后，`ActionsTests`、`CrashSafeJsonlWriterTests`、`DiagnosticExportRedactionTests`、`AgentMonitorViewModelTests` 等多个既有测试文件集体报 CS0103，我自己的 `ChartPaletteTests` 也一并报错。

回滚后，测试工程通过 `CrashScope.Desktop` 的 ProjectReference 传递获得 WPF FrameworkReference，`SolidColorBrush`、`Color` 等类型正常可用——即第七节担心的「隐式传递不可靠」在此并不成立，原状本就是对的。

这条值得记录：**「显式声明更稳妥」不是无条件成立的直觉，改动构建属性前必须实测。**

### 8.4 实测结果

环境：`export PATH="/c/Users/BuringBinary/.dotnet:$PATH"`，Debug 配置。仓库无 `.sln`，按项目文件分别构建。

| 项目 | 命令 | 结果 |
| --- | --- | --- |
| 测试工程（含 Desktop/Core/Contracts 传递构建） | `dotnet build test/CrashScope.Core.Tests/CrashScope.Core.Tests.csproj -c Debug` | **EXIT=0**，0 error |
| 桌面应用（含 XAML 编译） | `dotnet build src/CrashScope.Desktop/CrashScope.Desktop.csproj -c Debug` | **已成功生成**，0 error |
| 全量测试 | `dotnet test ... --no-build` | **479 通过 / 0 失败 / 0 跳过**，6 秒 |
| 本轮新增测试 | `--filter "FullyQualifiedName~ChartPalette|FullyQualifiedName~SeverityPalette"` | **37 通过 / 0 失败**，确认已执行而非被跳过 |

唯一警告是 `test/CrashScope.Core.Tests/RpcTests.cs:151` 的 CS1998（async 方法缺 await），**属既有问题，与本轮改动无关**，本轮未动。

基线对照：评审起点文档记录的回归门槛是 237 项，当前 479 项全绿，其中本轮三批共新增 37 项调色板相关测试。

### 8.5 状态订正

第四节、第七节第 6 点中所有「未验证」表述，以本节实测结果为准：

- 编译：**通过**（桌面应用与测试工程均 0 error）。
- 回归：**479/479 通过**。
- 新增测试：**37 项全部执行并通过**。
- 第三节第 4 点关于「VS Code 诊断返回 0 条但存在编译错误」的判断，本节再次得到证实：诊断在 CS0104 歧义上同样报 0 条，而实际构建报错。**该信号不可作为构建通过的证据**，这一点现在有了两次独立佐证。

### 8.6 仍未完成

- **真实窗口视觉验收未做**：深浅主题切换下的严重度徽标、趋势箭头、对比面板、图表重着色，均只有静态与单元层面的保证，没有人工走查过实际界面。这需要启动应用，超出本轮只读与静态改动的范围。
- 页面级 `#0F0F1A` 等固定深色底色约 20 处，P2 范围，未动。**（已于第九节完成）**
- `ThemeService` 无跟随系统主题。
- `FloatingBarWindow` 是否固定深色待产品确认。

现在构建与回归已经有保护，继续做 P2 的页面底色迁移的前提条件已经具备。

## 九、P2：页面级硬编码底色迁移（同日，续第八节）

第八节 8.6 遗留的「页面级固定深色底色约 20 处」已完成。本节记录范围、决策与验证。

### 9.1 盘点

`Pages/` 加顶层窗口共 28 处硬编码 hex：

| 文件 | 处数 | 说明 |
| --- | --- | --- |
| RecorderStatusPage.xaml | 8 | 全部 `Background="#0F0F1A"` |
| SettingsPage.xaml | 8 | 7 处 Background，1 处 `Foreground="#FFD93D"` |
| DashboardPage.xaml | 3 | 全部 Background |
| TimelinePage.xaml | 3 | 2 处 `#0F0F1A`，1 处 `#1E1F3A` |
| LiveMonitorPage.xaml | 1 | Background |
| AgentIncidentsPage.xaml | 1 | Background |
| AgentActionsPage.xaml | 1 | `Foreground="#00D68F"` |
| FloatingBarWindow.xaml | 3 | 8 位 hex，刻意半透明，**不改** |

其中 22 处 `#0F0F1A` 语义完全一致：都是嵌在 `CardBg` 卡片内部、带 `CornerRadius` 与 `Padding` 的凹陷面板底色。

### 9.2 决策：复用既有键，不新造

`CardBgAlt`（Dark `#11161E` / Light `#F4F6FA`）语义即「卡片内凹陷面板」，`IncidentsPage.xaml` 已用 9 处、`HomePage.xaml` 1 处，是仓库里的既定 pattern。因此 22 处统一迁移到 `{DynamicResource CardBgAlt}`，而不是新增 `PanelSunkenBg` 之类的同义键——语义重复的资源键是下一轮漂移的温床。

三处独立语义单独处理：

- TimelinePage `IncidentSelector` 的 `Background="#1E1F3A"` 改为 `CardBg`。理由：`ControlStyles.xaml` 的 ComboBox 模板其 `Bd` Border 用的就是 `CardBg`，且其余 ComboBox 均不内联 Background，这处内联是偏离。
- AgentActionsPage `Foreground="#00D68F"` 改为 `SuccessColor`。
- SettingsPage `Foreground="#FFD93D"` 改为 `WarningColor`。

### 9.3 纯 hex 扫描漏掉的浅色主题隐患

只扫 `#` 会漏掉字面色名。补扫 `Foreground="[^{]` 后发现 `IncidentsPage.xaml` 两处 `Foreground="White"`（第 342、408 行），分别落在 `AccentColor` 填充和 `{Binding PriorityBackground}` 严重度填充之上。

这两处在两套主题下白字都成立——因为背景是饱和色块而非主题表面色，浅色主题的严重度填充同样足够深。但裸字面量无法表达这层意图，下一个人很容易误判为 bug 并「修」成 `TextPrimary`，那才会真的坏掉。

故在两个主题文件各新增语义键 `OnAccentText = #FFFFFF`（两主题取值相同），并附注释说明它用于「饱和填充之上，而非表面之上」，把这 2 处替换掉。取值相同但键名承载意图，这是它存在的全部理由。

### 9.4 验证

静态：

- `Pages/` 下硬编码 hex **0 处**
- 字面色名（White/Black/Gray/Red）**0 处**
- `Background` 除 `Transparent` 外全为 `DynamicResource`；`Foreground` 全为 `DynamicResource`
- 主题键 parity：Dark 42 = Light 42，双向差集为空
- 当前 `Pages/` 键分布：`CardBg` 53、`CardBgAlt` 32

构建与测试：

| 项目 | 结果 |
| --- | --- |
| `dotnet build src/CrashScope.Desktop` | 已成功生成，0 error |
| `dotnet test test/CrashScope.Core.Tests` | 479 通过 / 0 失败 / 0 跳过 |

### 9.5 一条必须记住的验证局限

`DynamicResource` 键名拼写错误**不会**产生编译错误，只在运行时静默回退为无值。所以「Desktop 工程构建通过」证明的是 XAML 语法与类型正确，**不**证明资源键名正确。键名正确性在本轮由两件事保证：一是所有目标键都取自既有键集合（非新造，已在别处生效），二是两主题 parity 检查确保不存在单边缺失。`OnAccentText` 是本轮唯一新键，两侧同时新增并已计入 parity。

### 9.6 更新后的遗留项

- **真实窗口视觉验收未做**——这是现在唯一的主要缺口，需要启动应用人工走查深浅切换。
- `ThemeService` 无跟随系统主题。
- `FloatingBarWindow` 是否固定深色待产品确认（其 4 个 8 位 hex 为刻意半透明，不要当 bug 修）。

## 十、自动化验收（同日，订正第九节 9.3）

用户要求"先自动测试验收，再给手动验收方法"。为此写了 `scripts/audit-theme-resources.py`，
做三件构建和单元测试都做不到的事：资源键闭环、两主题 parity、WCAG 对比度计算。
**它找出了两个真实缺陷，其中一个推翻了第九节的结论。**

### 10.1 缺陷一：深色主题徽标白字对比度不合格（第九节判断错误）

第九节 9.3 我写了「两套主题下白字都成立」，并据此把 `OnAccentText` 在两个主题里都设成 `#FFFFFF`。
**这个判断是错的**，而且错在我只做了语义推理、没有算数值。

实际算出来的对比度（白字落在各填充之上）：

| 填充 | 深色主题取值 | 白字对比度 | 浅色主题取值 | 白字对比度 |
| --- | --- | --- | --- | --- |
| AccentColor | `#A5A0FF` | **2.31** | `#5744B8` | 7.18 |
| SuccessColor | `#52D6AE` | **1.81** | `#087859` | 5.47 |
| WarningColor | `#E5BE75` | **1.76** | `#835A09` | 6.12 |
| DangerColor | `#FF929B` | **2.13** | `#B72F45` | 5.99 |
| SeverityLow | `#8B95A7` | **3.02** | `#526075` | 6.39 |

WCAG 正文要求 4.5。深色主题五项全部不合格，最差 1.76，几乎看不清。

根因是我把「深色主题」等同于「所有颜色都深」。实际上这个深色主题的强调色是**柔和色系**，
填充本身很亮，白字压上去自然糊。浅色主题反而用了深饱和填充，白字没问题。
**两个主题在这个键上的正确取值是相反的。**

修复：深色主题 `OnAccentText` 改为 `#14161F`，对比度提升到 7.79 至 10.27；浅色主题保持 `#FFFFFF`。
两个文件都加了注释写明为什么它们故意不同，以免后人"统一"回去。

修复后全部 8 个填充在两个主题下都达标（深色 5.97 至 10.27，浅色 5.47 至 7.18）。

### 10.2 缺陷二：`BoolToVis` 未定义，会让页面崩溃

`AgentActionsPage.xaml:71` 引用了 `{StaticResource BoolToVis}`，但这个键**在整个仓库里从未定义过**。

严重度高于第一个：`StaticResource` 解析失败会抛 `XamlParseException`，AgentActions 页的
Plans 列表一渲染就崩。这不是颜色不好看，是功能不可用。

它躲过了所有既有防线：构建通过（XAML 编译不解析资源引用）、479 个单元测试通过
（没有一个加载 XAML）。这正好是第八节那条教训的延伸：**构建通过不代表 XAML 能加载，
单元测试全绿不代表界面打得开。**

而且它在 HEAD 提交 `71c0603` 里就存在，不是本批改动引入，也就是说这个页面在当前分支上
一直是坏的，只是没人点开过。

修复：在 `Themes/CommonResources.xaml` 定义 `<BooleanToVisibilityConverter x:Key="BoolToVis"/>`，
该字典已在 `App.xaml` 全局合并，所有页面共用一个实例。

### 10.3 顺带确认没问题的部分

- **本地化完整性**：XAML 与代码共引用 244 个 `L.*` 键，`en-US.json` 与 `zh-CN.json` 各 451 个键，
  **两个语言文件缺失均为 0，且双向 parity 为空**。审计报告里那 244 条「未定义」是预期的，
  `L.*` 由 `LocalizationService` 在运行时注入，本就不在 XAML 里定义。
- **主题 parity**：Dark 42 = Light 42，双向差集空。
- **正文文字对比度**：`TextPrimary` / `TextSecondary` / `NavCheckedText` 在四个表面色上，
  两个主题全部不低于 5.59，最高 17.03。
- **死键**：11 个定义了但无人引用（`AccentHover`、`SeverityCritical` 等）。非阻塞，
  `Severity*` 系列是 `SeverityPalette.cs` 按字符串动态取的，静态扫描看不到，不要误删。

### 10.4 审计脚本本身经过反向验证

一个从没失败过的检查是不可信的。所以对脚本做了故障注入测试：

| 注入的故障 | 期望 | 实际 |
| --- | --- | --- |
| 基线（无故障） | exit 0 | exit 0 PASS |
| 单边主题键（只在 Dark 定义且被引用） | exit 1 | exit 1，同时报出 parity 与单边两条 |
| 引用一个完全不存在的键 | exit 1 | exit 1，精确报出键名 |
| 还原后 | exit 0 | exit 0 PASS |

注入与还原后确认：DarkTheme 42 个键完好、无 `ZZ` 残留、行尾仍为纯 LF。

第一次写的负向测试是无效的：`sed` 用 `#` 作分隔符，撞上颜色值里的 `#` 直接语法错误，
故障压根没注入进去，脚本「通过」了一个空测试。改用 Python 字符串替换重做才真正验证。
教训：**负向测试自己也要先确认故障确实注入成功了。**

脚本只在真缺陷上返回 1，`L.*` 已按前缀豁免，可以直接进 CI。

### 10.5 最终状态

| 项 | 结果 |
| --- | --- |
| `dotnet build src/CrashScope.Desktop` | 已成功生成，0 error |
| `dotnet test` | 479 通过 / 0 失败 |
| `scripts/audit-theme-resources.py` | exit 0 PASS |
| 非本地化未定义键 | 0（修复前 1） |
| 徽标对比度达标数 | 16/16（修复前 8/16） |

### 10.6 自动化到此为止，剩下的必须人工

已经写入 `docs/mcp-manual-acceptance-checklist.md`。核心是四件自动化查不到的事：

1. **逐页冒烟**。`BoolToVis` 那个缺陷证明了页面能崩而所有自动检查全绿。15 个页面两个主题各点一遍。
2. **图表重着色**。OxyPlot 颜色是代码里显式设的，主题切换时要手动重刷。网格线、轴标签、
   图例最容易漏，静态扫描完全看不到这块。
3. **切换时的「孤岛」**。对比度算的是静态搭配，算不出「切换后某个元素没跟着变」。
4. **布局**。中英文切换下的截断与撑破，以及高 DPI 缩放。

第九节说的「真实窗口目视验收是唯一缺口」在本节之后依然成立，但缺口已经收窄：
能算的都算过了，剩下的确实只有人眼能判断。

## 十一、第二轮自动验收：切换孤岛与代码内硬编码（同日）

第十节的审计只扫了 XAML 里的资源键和颜色值，有两个盲区没覆盖：`StaticResource` 引用主题键
（加载时解析一次，切换不更新），以及 C# 代码里写死的颜色。补扫这两类后**又找出三个缺陷**，
其中两个正是第十节 10.6 里我自己列为「必须人工验收」的那类问题 —— 说明那部分其实还能再自动化一些。

### 11.1 缺陷三：SettingsPage 状态提示在浅色主题下几乎不可见

`SettingsPage.xaml.cs` 的 `ShowStatus()` 接收 hex 字符串，直接 `new SolidColorBrush` 赋给
`Foreground`，7 个调用点分别传 `"#00D68F"`（成功）和 `"#FF6B6B"`（失败）。这两个值
**不在任何主题文件里**，是 P2 迁移前的旧色值残留 —— XAML 里的同名值我上一轮清掉了，代码里的漏了。

落在 SettingsPage 卡片表面上的实测对比度：

| 表面 | 硬编码绿 `#00D68F` | 硬编码红 `#FF6B6B` |
| --- | --- | --- |
| Dark CardBg | 8.68 | 5.96 |
| **Light CardBg** | **1.91** | **2.78** |
| **Light CardBgAlt** | **1.76** | **2.56** |

浅色主题下 1.76 到 2.78，基本读不出来。而且它既不跟随主题切换，也绕过了整套主题机制。

修复：`ShowStatus(string message, string resourceKey)`，改走 `SeverityPalette.Resolve`，
调用点传 `StatusSuccessKey` / `StatusErrorKey` 两个常量（映射到 `SuccessColor` / `DangerColor`）。
修复后浅色 5.47 与 5.99、深色 9.13 与 7.75，全部达标。顺带移除了不再使用的
`Color` 与 `ColorConverter` 别名。

### 11.2 缺陷四与五：IncidentsPage 与 TimelinePage 切主题后徽标保留旧配色

这正是第十节 10.6 第 3 点说的「孤岛」，而且它**能被静态检查抓到**，不必等人工走查。

`SeverityPalette.Resolve` 是在列表构建时调用的，解析结果作为具体 `Brush` 对象存进 ViewModel：

```
IncidentsPage.xaml.cs:454   PriorityBackground = SeverityPalette.Resolve(...)
TimelinePage.xaml.cs:69     Color = SeverityPalette.Resolve(SeverityPalette.Critical)
```

画刷一旦解析就定死了。这两个页面**都没有订阅 `ThemeChanged`**，切主题时列表不重建，
徽标与时间轴标记会一直保留旧主题的颜色。

叠加第十节的修复后果更明显：深色主题的 `OnAccentText` 现在是近黑字，如果徽标底色卡在浅色主题的
深紫上，就是近黑字压深紫底 —— 比修复前更难读。**两个正确的改动叠在一起产生了新的坏结果**，
这类耦合单看任何一处都发现不了。

修复：两页按 `DashboardPage` 的既定范式接线 —— `Loaded` 里先 `-=` 再 `+=`（防重复订阅），
`Unloaded` 里 `-=`，`OnThemeChanged` 用 `Dispatcher.InvokeAsync` 加 `IsLoaded` 守卫后重建列表。
`IncidentsPage` 复用已有的 `RefreshList()`（`RefreshLocale()` 走的就是它），
`TimelinePage` 重建当前选中项的时间轴。

### 11.3 顺带确认没问题的

- **`StaticResource` 引用主题键：0 处**。全仓库的主题键引用都是 `DynamicResource`，
  这类孤岛不存在。
- **图表重着色是完整的**。我在第十节把它标为「最可疑」，实测下来
  `DashboardPage.ApplyChartTheme()` 覆盖了系列颜色、标记填充与描边、网格线
  （`TicklineColor` / `AxislineColor`）、轴文字与标题，双轴图还按轴 Key 分别着色，
  最后 `InvalidatePlot(false)` 保留历史数据。**图例不适用** —— 没有创建 `Legend` 对象。
  这块之前担心错了，不需要人工重点查。
- C# 硬编码颜色从 15 处降到 8 处，剩下的是 `OxyColors.Transparent`
  与 `new SolidColorBrush(Colors.Gray)` 兜底默认值，都是合理的。

### 11.4 审计脚本新增两项检查，并再次反向验证

新增 E 节：检测「持有 item-level 画刷或 PlotModel 但未订阅 `ThemeChanged`」的页面，
以及「`+=` 多于 `-=`」的事件泄漏。两项都接入退出码。

第一版写得太宽，把 `SettingsPage`（3 秒自动消失的 toast）和 `RecorderStatusPage`
（每次更新都重新赋值的状态文字）也报了出来。两者都是**瞬态赋值**，下次更新即刷新，不是孤岛。
收紧为只看「画刷存进 ViewModel 属性」（`public Brush X { get; set; }`）或「页面持有 `PlotModel`」
两种情况后，误报消失。

故障注入验证：

| 注入 | 期望 | 实际 |
| --- | --- | --- |
| 基线 | exit 0 | exit 0 PASS |
| TimelinePage 完全不订阅 ThemeChanged | exit 1 | exit 1，精确点名该文件 |
| 真实事件泄漏（`+=1 -=0`） | exit 1 | exit 1，报出 `(+=1 -=0)` |
| 还原 | exit 0 | exit 0 PASS |

第一次做泄漏注入时只删了 `Unloaded` 里的 `-=`，但 `Loaded` 里「先减后加」那个 `-=` 还在，
计数仍然平衡，注入等于没生效（exit 0 被我误读成检查失灵）。加了注入自检
（打印实际的 `+=` / `-=` 计数，确认为 `+=1 -=0`）之后才真正验证。
**这是同一个教训第二次出现：负向测试必须先证明故障确实注入成功。**

### 11.5 最终状态

| 项 | 结果 |
| --- | --- |
| `dotnet build src/CrashScope.Desktop` | 已成功生成，0 error |
| `dotnet test` | 479 通过 / 0 失败 |
| `scripts/audit-theme-resources.py` | exit 0 PASS |
| `StaticResource` 引用主题键 | 0 |
| C# 硬编码颜色 | 8（全部合理），修复前 15 |
| 未订阅 ThemeChanged 的持久画刷页面 | 0，修复前 2 |
| 事件订阅泄漏 | 0 |

### 11.6 对人工验收范围的修订

第十节列的四项里，有两项现在已被自动化覆盖，人工可以少花力气：

- **图表重着色**：已确认代码层面完整（网格线、轴标签都覆盖了，无图例）。人工只需确认
  视觉效果，不必逐项排查是否漏刷。
- **切换孤岛**：三个已知孤岛已修复，且脚本能防止再出现。人工仍需看一遍，
  因为脚本只覆盖 `SeverityPalette` / `ChartPalette` 这条路径。

仍然完全依赖人工的是：**逐页冒烟**（`BoolToVis` 那类 XAML 加载期崩溃）、
**布局问题**（中英文截断、高 DPI）、以及**主观视觉判断**。

## 十二、产品决策落地：悬浮条定性与跟随系统主题（同日）

用户就第十一节遗留的两项产品决策拍板：悬浮条**保持固定深色**，**加入「跟随系统主题」**。

### 12.1 悬浮条：确认为刻意设计，不是缺陷

查证后确认 `FloatingBarWindow` 是 `AllowsTransparency=True` + `Topmost` + `ShowInTaskbar=False`
的**半透明置顶叠层**，浮在任意桌面内容之上，而不是应用窗口内的表面。深色半透明底板配浅色文字
能在任何壁纸或游戏画面上保持可读，这是 OSD 的通用处理；跟随浅色主题反而会在明亮背景上糊掉。

四个 8 位值全部是 `#AARRGGBB`，首字节是**有意的 alpha**：`E6` 底板 90% 不透明、
`33` 发丝边框 20%、`F0` / `88` 两级文字透明度。

已在 XAML 里写入注释，明确标注为产品决策并写明「不要改成主题键」，避免后人当 bug 修。

### 12.2 跟随系统主题

语言设置里已有 `"languageSystem": "Follow system"`，所以这是**对齐既有交互模式**，不是发明新概念。

**核心设计：把「偏好」与「生效值」分开。**

```
CurrentTheme     用户存储的偏好，可能是 "System"
EffectiveTheme   实际应用的调色板，永远是 "Dark" 或 "Light"
```

这个区分是必须的：`"System"` 不是调色板名，`ApplyTheme("System")` 找不到对应字典。
混用两者会在加载旧配置时静默回退到深色。

映射逻辑提成**纯静态函数**以便单测（`ThemeService` 构造需要真实 WPF `Application`）：

```csharp
public static string ResolveEffectiveTheme(string? preference, bool systemUsesLight) =>
    preference switch
    {
        Light => Light,
        Dark => Dark,
        System => systemUsesLight ? Light : Dark,
        _ => Dark
    };
```

其余实现要点：

- **注册表**：读 `HKCU\...\Themes\Personalize` 的 `AppsUseLightTheme`，读不到时返回 false（深色），
  对齐本应用默认值而非 Windows 默认值。
- **只在需要时挂钩**：`SystemEvents` 持有**静态**处理器列表，无条件订阅会让服务活到进程结束。
  只有偏好为 `System` 时才挂，切走即摘（`UpdateSystemWatch`），`App.OnExit` 里 `Theme?.Shutdown()` 兜底。
- **线程**：`UserPreferenceChanged` 在系统线程触发，资源字典必须在 UI 线程改，故走 `Dispatcher.BeginInvoke`。
- **避免冗余切换**：从 Dark 切到 System 而系统本就是深色时，偏好变了但调色板没变，
  跳过字典替换与事件触发。
- **下拉显示**：「跟随系统 (浅色)」括号内显示当前解析结果，与语言下拉一致。系统翻转时
  `SettingsPage` 订阅 `ThemeChanged` 重建下拉以更新括号内容，重建期间临时置 `_initialized = false`
  防止 `IsSelected` 赋值反向触发 `SetTheme` 递归。

### 12.3 验证

| 项 | 结果 |
| --- | --- |
| `dotnet build src/CrashScope.Desktop` | 已成功生成，0 error |
| `dotnet test` | **493 通过** / 0 失败（原 479，新增 14） |
| `scripts/audit-theme-resources.py` | exit 0 PASS |
| 本地化 parity | en 452 = zh 452，双向差集空 |
| 四页 ThemeChanged 订阅对称性 | Dashboard / Incidents / Timeline / Settings 均 `+=1 -=2` |
| `App.OnExit` 释放 OS 钩子 | 已接线 |

单元测试覆盖：显式偏好忽略系统标志、System 跟随标志、未知值回退深色（含大小写敏感）、
`SupportedThemes` 内容、**解析结果永不返回 `"System"` 哨兵值**。

另加了两项**真实环境**测试：本机注册表读取与解析结果自洽、多次调用稳定。
它们断言的是自洽性而非固定值，所以开发机主题设成哪样都不会误红。
实测本机 `AppsUseLightTheme = 1`（系统为浅色），选「跟随系统」将解析为 Light。

### 12.4 过程中的一个错误

给 `PopulateThemeComboBox` 加辅助方法时，补丁把新方法插进了原方法体内部，多留一个 `}`，
导致 `CS1022`。**构建立刻报错，一次就定位修掉了** —— 这类语法错误正是编译器的强项，
和前面几轮那些「构建全绿但运行时才炸」的问题性质完全不同，不需要额外防线。

### 12.5 剩余开放项

产品决策已全部拍板，代码层面没有已知未修缺陷。仍然只能人工完成的是：

- **逐页冒烟**（`BoolToVis` 那类 XAML 加载期崩溃，自动检查全绿也照崩）
- **「跟随系统」的端到端验证**：需要在 Windows 设置里实际翻转系统主题，
  确认应用无需重启即刻跟随、下拉括号内容同步更新
- **布局**（中英文截断、高 DPI 缩放）
- **主观视觉判断**







