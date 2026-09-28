# 批次 D 样板页：事件详情重排与图表语言（2026-09-21）

状态：**样板，待确认**。本轮只新增本文与 `.mcp-probes/d-sample-20260921/` 下的样板资源，未修改 `src/`、`test/`、locale 或主题文件，
未启动产品或录制，未提交。样板用真实 WPF 渲染（真实 `MainWindow.xaml` 外壳 + 真实主题字典 + OxyPlot 2.2），不是设计稿拼图。
依据 `docs/mcp-product-experience-plan-20260921.md` 第五节第 4、5 条与第六节批次 D 的完成判定：样板页先确认，再实施。
本文路径相对于 CrashScope 仓库根目录。

## 一、看什么

| 文件（`.mcp-probes/d-sample-20260921/out/`） | 内容 |
| --- | --- |
| `incident-zh-CN-Dark.png`、`incident-zh-CN-Light.png`、`incident-en-US-Dark.png`、`incident-en-US-Light.png` | 事件页 1200x750 默认窗口首屏 |
| `incident-*-full.png` | 同一页 1200x1250 高窗口，右侧详情不滚动即可看全 |
| `chart-zh-CN-Dark.png`、`chart-zh-CN-Light.png`、`chart-en-US-Dark.png`、`chart-en-US-Light.png` | 仪表盘"历史趋势"区块的图表语言样板，含无数据态 |

重新渲染：`SampleRender/` 是独立小工程，引用 `src/CrashScope.Desktop`，输出隔离在同目录 `artifacts/`：

```
D=.mcp-probes/d-sample-20260921
~/.dotnet/dotnet.exe build $D/SampleRender/SampleRender.csproj -c Release --artifacts-path "$(pwd -W)/$D/artifacts"
~/.dotnet/dotnet.exe $D/artifacts/bin/SampleRender/release/SampleRender.dll "$(pwd -W)" "$(pwd -W)/$D" "$(pwd -W)/$D/out"
```

样板文案在 `strings.zh-CN.json` / `strings.en-US.json`（前缀 `S.`），不进产品 locale；数据是合成的一条"驱动超时/恢复"候选记录，
字段全部对应 `src/CrashScope.Core/Models/Analysis.cs` 里现有的 `IncidentAnalysis` 成员，没有臆造模型里不存在的信息。

## 二、事件详情：改了什么、为什么

现状（`src/CrashScope.Desktop/Pages/IncidentsPage.xaml:140-227`）：标题 → 时间 → 技术详情 Expander → 假设（只有名称和评分）→ 待确认问题 → 导出。
`IncidentAnalysis.Timeline`、`GpuTransitions`、`Graphics`、`FailureHypothesis.Evidence` 这些已经算出来的证据在详情里**完全没有展示**，
用户只能看到"评分 7"却看不到 7 分从哪来。

样板顺序（计划第 5.4 节）：

| 区块 | 内容 | 数据来源 | 备注 |
| --- | --- | --- | --- |
| 1 记录概览 | 标题 + 分类徽标 + 支持度徽标；最后记录时间与"距最后遥测 N 秒"；4 个事实小卡：会话时长 / 结束前空闲 / 结束时 GPU 负载 / 结束时仍在运行 | `PreviousSession`、`LastTelemetrySampleAt`、`TelemetryLagSeconds`、`FinalIdleSeconds`、`FinalGpuLoadPercent`、`Graphics.GraphicsApps` 中 `RunningAtEnd` 为真的项 | 缺失字段显示"—"，不省略小卡 |
| 2 观察到的事实 | 时间 · 类别徽标 · 描述，默认最近 5 条 + "显示全部 (N)" | `Timeline`、`GpuTransitions.Description`、`Graphics.DriverChangeEvents` 等合并后按时间排序 | 类别只做四类：传感器 / 遥测 / Windows 事件 / 会话 |
| 3 候选解释 | 副标"按证据支持度排序，不是确诊"；首个候选展开：支持 N 条（✓）与反证 N 条（✗）各带权重；其余候选折叠成一行 | `Hypotheses[].Evidence` 的 `Supports` / `Weight` / `Description` | 评分为 0 的候选显示"证据不足"，不显示 0% |
| 4 尚缺信息 | `OpenQuestions` 列表；为空时显示"本次记录没有生成待确认问题" | `OpenQuestions` | 补上现状缺失的空态 |
| 5 下一步 | 三个只读/导出动作，每个带一句"依据"：对比另一条记录 / 在时间线中查看该时段 / 导出脱敏诊断包 | 由已有记录数与事实时间分布生成 | 不出现驱动更新、提权、服务安装等系统更改动作 |
| 6 技术详情 | 折叠：记录 ID + 复制、原始分类、原始时间戳 | 现有字段 | 从原来的第 3 位移到最后 |
| 导出栏 | 不变：脱敏勾选 + 导出按钮，仅选中恰好一条时启用 | 现有逻辑 | — |

其他一并对齐的点：页标题改为与首页相同的 22 / SemiBold；左侧记录列表模板不动；对比与跨事件分析面板不动。

## 三、图表语言：统一规则

现状（`src/CrashScope.Desktop/Pages/DashboardPage.xaml.cs:69-83, 209-248`）：横轴是 `Seconds ago`（−300…0），系列标题与轴标题为英文硬编码
（`CPU Load %`、`Seconds ago`、`RAM Used (GB)`），四张图各自带纵轴标题，缺失样本会被相邻点连成一条线，没有选中读数。

样板规则（四张卡一致）：

1. 卡头承担元信息：`标题（按指标着色）· 单位徽标 · 当前值 · 最后样本时间`；纵轴不再写标题，腾出绘图区。
2. 横轴用本机时间 `HH:mm:ss`，每分钟一格；与事件页、时间线的时间语义一致，方便对照"09:15:01 Display 4101"这类事实。
3. 只画水平网格线（`BorderColor` 63% 透明），无竖网格、无绘图区边框；字号 9.5，文字色 `TextSecondary`。
4. **缺失样本不连线**：数据里写 `double.NaN` 断开，并用 `TextSecondary` 13% 透明的竖带标注"无样本 12 秒"。
5. **过期样本**：最后样本超过阈值时，卡头"最后样本 09:15:24 · 已过期 40 秒"改为 `WarningColor`，当前值显示"—"，曲线尾部留空，不延长。
6. 状态转换用 `DangerColor` 竖线 + 左侧小标签（"98% → 0%"），代替现在的菱形散点。
7. 选中时间点：虚线十字 + 圆点 + 读数标签（"09:15:40 · 61%"）；实施时用 OxyPlot tracker 的自定义模板实现，样板里用注解模拟。
8. 双轴图：左轴文字色 = 左系列色，右轴 = 右系列色，图例放绘图区左上角、横向、无底色。
9. **未录制态**：不画空坐标系，只有一句解释 + "开始录制"按钮；"等待首个样本"与"传感器不可用"沿用同一卡壳，只换文案。
10. 主题切换只重着色，不重建系列（现有 `ApplyChartTheme` 已满足）。

## 四、需要你拍板的点

1. 事件详情的六段顺序与"首屏到候选解释首卡"的密度是否可接受？（1200x750 下首屏能看到概览 + 5 条事实 + 首个候选的标题行。）
2. 候选解释的证据要不要默认展开？样板是首个候选展开、其余折叠。
3. "下一步"三个动作是否足够克制？是否需要第四个"在设置里启用 WHEA 采集器"（它会改设置，样板故意没放）。
4. 横轴从"Seconds ago"改为本机时间 `HH:mm:ss`——这是行为变化，需要确认。
5. 状态转换从菱形散点改为竖线标签——现有 `_gpuTransitionMarkers` 会被替换。
6. 未录制态卡片里的"开始录制"按钮：接首页同一个 `RecorderOperationGate`，还是只做跳转首页？
7. 首页事件行是否也同步显示"分类徽标 + 支持度"（本轮未画，属于 A 的延伸）。

## 五、确认后的实施范围（预估）

| 项 | 文件 | 规模 |
| --- | --- | --- |
| 展示模型 | 新增 `IncidentDetailPresentation.cs`（事实合并排序、候选/证据投影、下一步生成、空态文案）+ 单测 | M |
| 事件详情 XAML | `Pages/IncidentsPage.xaml:140-227` 重排；`ControlStyles.xaml` 新增 `Chip`、`DetailSection`、`FactRow` 等样式与 `AccentTint` 主题键（深浅各一） | M |
| code-behind | `IncidentsPage.xaml.cs` 绑定新模型；`No data` / `N/A` 英文一并清掉 | S |
| 图表 | `DashboardPage.xaml.cs` 的 `CreatePlotModel`/`CreateDualAxisPlotModel`/`CreateLineSeries` 改为共享 `ChartStyle` 帮助类；`SensorHistoryBuffer` 在采样间隔 > 2 倍 `SampleInterval` 时插入 NaN；tracker 模板 | M |
| 图表卡头 | `DashboardPage.xaml:400-470` 四张卡加单位徽标 / 当前值 / 最后样本 | S |
| 无数据态 | 四张卡增加未录制 / 等待样本 / 不可用三态外壳，复用首页的状态判定 | S |
| locale | 新增约 40 个键，双语 parity | S |
| 验证 | 冒烟新增编译事件页详情断言（六段顺序、空态、导出栏）；Core 测试新增展示模型与 NaN 断线用例；构建 0 警告；主题审计 | M |

不做：不改分析算法与评分、不新增采样器、不改磁盘上的 `analysis.json`、不把支持度写成概率。

实施前提：按 `docs/mcp-ui-midpoint-review-20260921.md` 第六节，建议先完成分批提交（或至少提交前几批），
D 会大面积改 `IncidentsPage` 与 `DashboardPage`，在零提交基线上开工会让回退困难。

## 六、边界

样板是隔离渲染，DPI 为渲染比例；ListBox 选中、Expander 展开等交互未接线；OxyPlot tracker 用注解模拟。
所有输出在 `.mcp-probes/d-sample-20260921/`，未触碰 `dist/`、旧包、旧证据与源码。
