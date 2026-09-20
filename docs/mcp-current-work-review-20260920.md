# CrashScope 当前工作评审

日期：2026-09-20（UTC+8）
范围：CrashScope 当前工作树、近期 P0/P1 与 Toolbox T1 改动、现有测试包。文内路径均相对于 CrashScope 仓库根目录。

> 同日实测补充：已重新核验 0.9.1 GUI/Stop-Start、0.9.2 游戏会话和 P1 smoke，历史测试不应归零；旧分析漏读了实际存在的 DWM 基线。详见 `docs/mcp-existing-tests-review-20260920.md`。下文“尚未验收”主要针对最新候选包与新增路径，不表示旧版本没有实测。

## 一、总体结论

**已从功能原型进入可构建、可回归的工程测试版；尚未达到可放心长期常驻或与反作弊游戏同跑的验收状态。**

之前的工作不是只有文档：采集链安全默认值、进程代际/内存证据、事件 2004、GPU adapter 身份、自身开销、页面文件、CPU fallback、GPU heartbeat 和工具箱页面均有实现。独立发布包与自动验证也存在。

但目前不宜继续用增加页面数量衡量进展。优先级应是：**固化源码 → 修正诊断语义与回归遗漏 → 更新发布门禁 → 实机验收**。

本次没有发现构建阻断，但发现一处已用合成数据复现的诊断评分问题，以及两处源码可确认的采集/UI 逻辑问题。不能把 297 项测试全绿等同于上述路径已经覆盖。

## 二、当前状态与已有成果

### 2.1 基线与交付

- HEAD：`bf16486`，2026-09-18 的文档提交。
- 评审开始时 `git status --porcelain` 为 **62 个条目：24 个已跟踪修改、38 个未跟踪条目**。条目包含目录，不等于精确文件总数；不包含本次随后新增的评审报告。
- 已跟踪差异：24 个文件、870 行新增、178 行删除；该统计不包含未跟踪源文件。
- 当前有六个独立测试包目录，最新为 `artifacts/test-packages/20260919-101500/`。
- 最新包版本：`0.9.4-test.20260919101500`。
- 构建身份：`0.9.4-test.20260919101500+worktree.fb9b88c849ff`。
- 本次重算 ZIP SHA-256，与 `verification-result.json` 一致：
  `162621BA7D1226C1645B95BB71FE50DDD46D56A62448040E8EA33F93288E20BC`。
- 包的历史验证记录为 975 文件核验、297 测试通过、54 个受保护文件未变、`sourceUnchanged=true`、`desktopGuiSmokeExecuted=false`。本次只重新核对 ZIP 总哈希，未重新逐一核验那 975/54 个文件，也未证明当前源码与该包构建时完全一致。

工作树指纹有助于辨认产物，但不能替代完整源码归档。仅检出 HEAD 不能重建这些未提交成果；已有本地检查点是否足以恢复全部源文件，本次未验收。

### 2.2 已落地的主要工作

| 方向 | 已核实的实现 | 评价 |
|---|---|---|
| P0 安全采集 | DxgKrnl 默认关闭；90/95/98% commit 压力分级；按压力降载、停止 ETW | 方向正确，避免继续默认启用高流量 GPU ETW |
| P0 事故证据 | PID + 创建时间代际、进程内存快照、事件 2004、GPU LUID/adapter/engine 身份 | 比原先仅有总量和 PID 的证据可解释性更强 |
| P1 可观测性 | 自身 CPU/内存/句柄/线程、ETW 速率与 loss、页面文件快照、CPU fallback | 补齐“采集器自身是否可靠”的重要观测字段 |
| P1 采集健康 | GPU heartbeat、DWM baseline、权限预检 | 已有状态模型，但仍存在下述状态与证据语义漏洞 |
| Toolbox T1 | 原始传感器窗口、硬件概览、磁盘健康、悬浮条、网速测试页 | 已有代码与展示逻辑测试，不能代替实际 GUI 验收 |
| 工程验证 | 独立测试包、回归筛选、源码/旧文件哈希记录 | 值得保留；门禁强度与源码固化仍需完善 |

## 三、评审发现（按优先级）

### F1 · 高：DWM 启动基线被误当成“发生了软件渲染回退”

**位置**：

- `src/CrashScope.Core/Monitoring/DwmStateTracker.cs:62-66`
- `src/CrashScope.Core/Analysis/HypothesisEvaluator.cs:180-181,420-427`
- `src/CrashScope.Core/Analysis/TimelineBuilder.cs:152-160`

P1 新增了启动基线：第一次探测合成关闭时，会生成 `IsHardwareRendering=false / IsCompositionEnabled=false` 的 baseline。但分析器只检查是否存在这种布尔组合，不判断它是基线还是从开启转为关闭，也未在该判断处限定其接近故障的时间。

**本次已复现**：隔离探针注入 `(HRESULT=0, enabled=false)`，连续两次探测状态不变，只有首次基线；配合同一条合成 Kernel-Power 41 证据，HardFreeze 分数由 **2 增为 5**。新增证据权重为 3，描述却是“DWM fell back to software rendering before the end”。

这不是本机发生了新的 GPU 故障，而是纯合成数据对现有分析代码的复现。基线的存在并不能证明发生过回退。另外，源码自己已注明 `IsHardwareRendering` 并非独立测量，合成状态不应直接包装成硬件/软件渲染模式的确证。

**建议**：显式区分 Baseline/Transition；只有时间窗口内有真实转变才生成转变证据。将渲染模式的未知状态保留下来，修改时间线和证据文案。新增“仅关闭基线”“真实开启→关闭”“先关闭后恢复”“远早于故障”的端到端评分测试。

证据：`.mcp-probes/review-20260920/DwmProbe/Program.cs`、`.mcp-probes/review-20260920/dwm-probe.log`。

### F2 · 中：GPU 计数器全失败仍可能标记为 Idle

**位置**：`src/CrashScope.Core/Monitoring/GpuEngineTracker.cs:60-94,130-143`。

计数器构造失败被吞掉；`NextValue()` 失败也直接跳过。最后只要 `byProcess.Count == 0` 就返回 `Idle`。因此“枚举成功，但所有计数器创建/读取失败”与“成功采样且全部低于 0.5%”没有区分。

这违背了 P1 heartbeat 想解决的核心问题：**没采到不等于空闲**。这里是明确的控制流问题；本次没有通过破坏真实 Windows 计数器来触发现场故障。

现有 `test/CrashScope.Core.Tests/P1DiagnosticReliabilityTests.cs:116-125` 只直接构造两个 `GpuEngineSample` 并检查字段，没有执行 `GpuEngineTracker.Capture()` 的失败路径。

**建议**：记录成功采样数、失败数、预热数；没有有效读取时标记 Error/Unavailable/AwaitingSample，不能直接 Idle；部分成功应表达降级。为采样器增加可注入接口，覆盖全失败、部分失败、首次预热、真实空闲和非有限数值。

### F3 · 中：先打开悬浮条再开始录制，传感器映射可能一直为空

**位置**：

- `src/CrashScope.Desktop/FloatingBarWindow.xaml.cs:49,58-65,105-118`
- `src/CrashScope.Desktop/AppService.cs:44,56-81`
- `src/CrashScope.Desktop/DesktopSensorMap.cs:12-17`

悬浮条创建时从当前目录构建 `_map`；未开始录制时目录可以为空，所有索引为 -1。收到采样时，它用 `values.Count != _service.SensorCatalog.Count` 决定是否重建映射。但正常新会话中，采样长度恰好等于当前新目录长度，因此条件不成立，旧空映射被继续使用。

同样，重启后目录内容或顺序改变、但采样与当前目录长度仍相等时，也不能让旧映射自动失效。

**影响**：先开悬浮条后开始录制的路径可能持续显示 `--`；重启或换目录时可能沿用错误索引。本项为源码路径确认，未运行 WPF GUI 实机复现。

**建议**：比较缓存的目录身份/版本，而非采样长度与当前目录长度；在新会话/目录变更时重建。补上“录制前开窗”“Stop/Start”“等长不同顺序目录”测试。

### F4 · 中：发布门禁仍允许实际测试集缩水

**位置**：`scripts/publish-test-package.ps1:111-123`。

当前筛选实际通过 297 项，但脚本下限是 **282**。筛选使用类名字符串；如果一个测试类意外丢失，并不会仅因该类零匹配而失败。比如全部 9 项 P1 测试丢失后，288 仍高于 282，有机会通过现有数量门禁。

**建议**：不要只提高一个总数；同时检查必需测试类/关键用例存在、执行数符合预期、无失败/跳过。明确哪些含真实采集的集成测试被排除及原因。代码评审与测试门禁要覆盖实际控制流，而不仅是模型字段。

## 四、仍然开放的验收与产品边界

### 4.1 高优先级：实机验收尚未闭环

最新包记录明确写着 `desktopGuiSmokeExecuted=false`。P1 报告也明确未完成：

- Desktop Start/Stop/Start、页面切换、托盘正常退出；
- 30–60 分钟无游戏基线；
- 管理员 Power/WHEA ETW 的真实覆盖；
- 受控游戏退出/等待/再次启动周期；
- 2–4 小时常驻稳定性、内存/句柄趋势与日志增长率。

已有 15 秒 smoke 证明了部分指标能产生数据，不证明长期可靠性。本次没有补做这些有实际运行影响的验收。

### 4.2 不应将 P0 安全默认值等同于完整 Game Safe Mode

已确认 `EnableDxgKrnlEtw=false`，但进程枚举、按进程 GPU 计数器、硬件监控等仍然存在。当前没有足够依据承诺与 ACE 游戏兼容，也不能因关闭 DxgKrnl 就声称游戏模式已经完成。

历史 ACE 审计是特定时间点的记录，其中“当前开启 DxgKrnl”等表述已不适用于最新默认值。应保留历史文档，同时增加一个明确的当前支持矩阵，避免把旧审计与新默认值混读。

### 4.3 文档与产品接线需要收敛

- `README.md` 仍描述 Agent 默认启动三个 ETW 会话、旧进程快照频率等，与当前 RecorderOptions/RecorderEngine 不一致。
- `ROADMAP.md:207-211` 仍将 0.9.2 包写作 W3 部署目标，而最新记录已是 0.9.4。
- CPU fallback 已写文件和健康详情，但本次搜索未发现 Desktop 消费 `CpuFallbackSample` 的数据接线；`RecorderEngine.cs:415-448` 仍向 UI 通知原始硬件传感器数组。应区分“取证 fallback 已落盘”与“桌面 CPU 数值已全面接入 fallback”，不要混称完成。
- Session ID 统一、Perflib 初始化降噪、commit 触发自动事故窗口是旧报告中的后续项；本次没有对其完成情况作新的全链路验收。

## 五、本次实际验证

| 项目 | 本次结果 |
|---|---|
| SDK | 找到用户目录中的 .NET SDK 8.0.425；初始 Bash PATH 无 dotnet，改用绝对路径后正常 |
| 发布筛选对应回归集 | **297/297，通过；失败 0，跳过 0** |
| Core / Contracts / Desktop / Tests | 在上述 `dotnet test -c Release --no-restore` 中完成构建 |
| Agent Release build | 通过，0 warning / 0 error |
| Service Release build | 通过，0 warning / 0 error |
| 测试项目编译警告 | `test/CrashScope.Core.Tests/RpcTests.cs:151` 存在 CS1998，不应把本次全套输出称为完全零警告 |
| `git diff --check` | exit 0；不覆盖所有未跟踪文件 |
| 编辑器 diagnostics | 返回 0；LSP 对 RecorderEngine 的空结果标记为 inconclusive，不视为完整语义检查证明 |
| 0.9.4 ZIP 总 SHA-256 | 本次重算与历史记录一致 |
| DWM 基线误加分 | 合成输入复现，HardFreeze 2 → 5，新增错误回退证据 weight=3 |

本次没有重跑历史报告中的完整 333 项测试。选择发布筛选集，避免把包含真实 RecorderEngine 启动的其他测试混入这次只做评审的任务。

日志与隔离探针位于 `.mcp-probes/review-20260920/`，不放进 docs。没有修改业务源代码、创建提交、覆盖测试包、安装服务、提权启动采集器或运行网速/游戏测试。生成了常规构建输出、测试日志、一个合成数据探针及本报告。

## 六、建议的下一轮顺序

1. **固化当前成果**：检查未跟踪内容并归档有价值证据；排除 NUL、生成目录和临时探针；按可构建边界提交。不要用 reset/clean 清理现有未提交工作。
2. **先修 F1–F3 并补真正的路径测试**：DWM 基线/转变、GPU 全失败/部分失败、悬浮条录制前开窗与跨会话映射。
3. **修 F4、同步当前状态文档**：必需用例检查、发布目标、采样频率、权限/ETW 默认行为、CPU fallback 接入边界。
4. **再产出新的独立候选包**：固定源码身份，保留旧包与历史 incident，不覆盖原目录。
5. **分层实机验收**：GUI smoke → 无游戏 30–60 分钟基线 → 单独授权的管理员 ETW 对照 → 明确兼容性策略后的受控游戏周期 → 2–4 小时常驻。

**最终判断：之前工作有实质进展，尤其是采集安全与证据可观测性；当前瓶颈是结论可信度和验收，而不是缺少更多功能。**
