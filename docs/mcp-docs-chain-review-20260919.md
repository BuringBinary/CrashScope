# CrashScope 历史记录深度评阅（mcp-* 文档链审读）

日期：2026-09-19（UTC+8）

评阅对象：`CrashScope/docs/` 下 36 份记录（2026-09-17 至 2026-09-19 三个工作日），对照工作树现场实测（git log / git status / artifacts / verification-result.json）抽查复核。

关联：`docs/mcp-work-review-resume-20260918.md`（上一份横向评审），本评阅在其之后覆盖 09-18 下午 T1 四批与 09-19 全天事故驱动工作。

## 一、已读记录清单与脉络重构

### 阶段 A：到达与 Phase 0（09-17）
- `docs/mcp-project-overview.md`、`docs/mcp-code-review.md`、`docs/mcp-phase0-changes.md`、`docs/mcp-phase0-followup.md`、`docs/mcp-status-assessment.md`、`docs/mcp-development-plan.md`
- 成果：5/5 项目构建绿、测试 0→146、30 项评审发现关闭；修正了“无 .NET SDK”的错误初判（SDK 在用户目录下，只是不在 PATH）。

### 阶段 B：可靠性收敛与打包就绪（09-17 晚）
- `docs/mcp-reliability-convergence.md`（R1-R5：恢复队列、单写者锁、会话生命周期、清理保护）、`docs/mcp-release-readiness.md`、`docs/mcp-collector-architecture` 相关评审（`mcp-current-implementation-review.md`）
- 关键品质：评审推翻了“此前声明全部关闭”的旧结论，以源码为准重新确认仍有核心路径缺陷——不沿用旧结论，这是整个文档链可信度的起点。

### 阶段 C：工具箱 T0/T1 与实机验收（09-18 白天）
- `docs/mcp-toolbox-full-plan.md`（55 页对照、GPL 净室边界）、`docs/mcp-t0-implementation.md`
- T1 四批：`mcp-raw-sensor-window-20260918.md` → `mcp-disk-health-page-20260918.md` → `mcp-floating-bar-20260918.md` → `mcp-speed-test-page-20260918.md`，门禁测试数 237→245→252→261→273 阶梯式上升。
- 实机链：`mcp-real-machine-data-assessment.md`（推翻两条 HardFreeze/100% 过度确诊）→ `mcp-real-machine-fixes.md` → `mcp-test-package-20260918.md` → `mcp-upgrade-acceptance-20260918.md`（5 分 25 秒实录，324+37 样本）。
- UI 修复链：`mcp-titlebar-hittest-fix.md`（WindowChrome 吞按钮）→ `mcp-ui-interaction-audit.md`（同类死控件普查）→ `mcp-tray-settings-fix.md`。这条链的方法论值得点名：一个具体缺陷 → 按缺陷类别全仓普查 → 修复并加回归。

### 阶段 D：横向评审（09-18 午）
- `docs/mcp-work-review-resume-20260918.md`：实测 git 树核对声明、抽查脚本行号与测试语义、列出 W1-W6 工作单；随后记录了 W1 提交完成（`0a423ba`/`f3959cc`/`64c21d0`）与推送（`bf16486`）。

### 阶段 E：事故驱动深修（09-18 夜 → 09-19）
1. 事故：`mcp-delta-force-memory-incident-20260918.md`——提交内存耗尽（非物理 RAM、非显存、非过热）→ 游戏退出 → 旧 PID 8.38 GiB 残留 + 新实例重叠 → 非正常重启 → ACE 静默模式。全文最有价值的判断是区分了“能确认的上界”（CrashScope < 0.663 GiB，非主因）与“不能排除的边际贡献”（余量仅数 MiB 时的最后一根稻草）。
2. 深分析：`mcp-session-deep-analysis-20260918.md`（416 行）——把 23 分钟 JSONL + Windows 事件合并成统一时间线，给出 P0/P1/P2 修复优先级；诚实列出 9 项“现有数据不能回答”。
3. 安全审计：`mcp-crashscope-ace-safety-audit-20260919.md`——逐项核对无读内存/注入/驱动，指出未签名 + DxgKrnl Verbose 全关键字是真实风险面，给出 Game Safe Mode 建议，并明确“不做当前包与游戏同跑”的临时禁令。
4. P0 落地：`mcp-p0-collection-hardening-20260919.md`——五项 P0 全部成码：DxgKrnl 默认关闭、按进程 Private Bytes + PID 代际生命周期、事件 2004 原生捕获、GPU adapter/LUID 分层、90/95/98% 提交压力自保护。324/324 + 288 门禁 + 0.9.3 包。
5. P1 落地：`mcp-p1-diagnostic-reliability-20260919.md`——把此前所有“不可判定点”转为新证据流：自身开销 `collector-overhead.jsonl`、GPU engine 四态 heartbeat（区分 Idle/Unavailable/Error/SuppressedByPressure）、DWM 启动 baseline、`pagefile.jsonl` 现场快照、权限预检（不再先撞 E_ACCESSDENIED 再降级）、PDH CPU fallback。333/333 + 297 门禁 + 15 秒真实 smoke + 0.9.4 包。

## 二、现场抽查复核（本评阅实测）

| 文档声明 | 实测 | 结果 |
|---|---|---|
| HEAD = `bf16486`（W1 docs 提交），与 origin 同步 | git log 首行一致，`origin/feature/local-desktop-work` 同点 | 属实 |
| P0/P1 未建提交、工作树保留改动 | git status 实测 60 个条目：T1 四批 8 个新源文件、P0/P1 六个新 Tracker/Options、五个新测试类、九份新文档、`.mcp-probes/` | 属实 |
| P1 新文件存在（SelfOverheadTracker/PageFileTracker/ResourceExhaustionEventTracker/CpuFallbackTracker/GpuCounterInstance/RecorderOptions） | 全部出现在未跟踪清单 | 属实 |
| 0.9.4 包 verification-result.json：SHA-256 = 162621BA…，testsPassed=297，protectedFilesUnchanged=54，sourceUnchanged=true，desktopGuiSmokeExecuted=false | 读取原文件逐项一致 | 属实 |
| 六份独立测试包目录互不覆盖（20260918-102913 … 20260919-101500） | artifacts 实测六目录齐全 | 属实 |
| 15 秒 smoke 证据目录 `artifacts/p1-smoke/20260919-101244/` | 存在 | 属实 |

抽查未发现文档与现场矛盾之处。

## 三、质量评价

### 突出优点
1. **诚实度是这条文档链的核心资产。** 每一份报告都严格区分“已证实 / 有数据支撑的推断 / 不能回答”，把 `Started` 与 `Observed`、N/A 与伪装零值、Unclassified 与确诊分开；这份纪律直接催生了最有价值的修复（health 三态、heartbeat 四态）。
2. **事故闭环质量好。** 现象（游戏弹窗）→ 逐层排除（RAM/显存/温度/磁盘/CPU/驱动/WHEA）→ 归因 commit 耗尽 + PID 代际残留 → 把每个“不可判定点”翻译成采集字段补进代码。这是教科书级的取证驱动开发。
3. **证据保护纪律稳定：** 独立目录、逐文件 SHA-256、旧包零覆盖、只读重评不回写历史分析、探针失败不误报为产品缺陷。
4. **门禁机制自我强化：** 每轮把新测试类钉进发布脚本最低通过数（237→245→252→261→273→288→297），文档声明与脚本可重算值一致。
5. **安全风险意识到位：** ACE 审计没有给出“绝对安全”的承诺，Game Safe Mode 建议具体且按风险排序。

### 问题与风险（按优先级）
1. **R1（高）：未提交风险复发。** 09-18 评审已把“51 个文件未提交”列为最大风险并由 W1 关闭；当前再度累积 60 个未提交条目——T1 四批功能、P0/P1 全部六轮实修与对应文档。0.9.2/0.9.3/0.9.4 三份测试包的构建身份均为 `worktree.<hash>` 指纹而非 commit，任何 checkout/clean/磁盘故障都会使这三份“已验证包”的源码不可重建。建议立即按逻辑分批提交（T1 功能 / 0918 文档 / P0 / P1 / 0919 文档）。
2. **R2（高）：验收缺口仍全部开放。** `desktopGuiSmokeExecuted=false`；30–60 分钟无游戏基线、受控游戏退出/重启周期、2–4 小时稳定性均未执行。当前验证结论是“指标可用”，不是“采集链已验收”。这是唯一阻断 P2/P3 的实体工作。
3. **R3（中）：管理员 ETW 对照未做。** P1 的预检只解决了“标准用户不盲试”，Power/WHEA 在提权下的真实覆盖仍未定性；0.9.4 的验收清单第 4 条已写明方法，只需一次明确授权的短时提权会话。
4. **R4（中）：三个 P1/P2 余项未动。** 统一 Session ID（目录 GUID 与 Journal GUID 不一致仍在）、Perflib 初始化降噪（启动一次写十条系统 provider 错误）、commit 达阈值自动生成事故窗口。
5. **R5（低）：Game Safe Mode 尚未实现**，而审计的临时禁令是“不要用当前未签名全功能包与三角洲同跑”——只要用户还想边游戏边采集，这就是最优先的产品功能。
6. **小瑕疵（不阻断）：** `mcp-delta-force-memory-incident-20260918.md` 文件名用 0918（事件日）、文内日期为 0919，与链内 “文档日期=撰写日” 惯例略有出入；早期固定名文档与 mcp-<topic>-<date> 新风格混存；工作区根遗留 `wpf_test_out.png`、`wpf_test_tb2.png` 两张探针图片未见归档说明。

## 四、下一步工作单（建议顺序）
1. **提交基线**：分批提交当前 60 个文件（功能四批可合并为两块：T1 功能+测试 / P0+P1+文档；同文件交叉时按可构建性合块），推送 origin。
2. **0.9.4 实机验收**：按 `docs/mcp-p1-diagnostic-reliability-20260919.md` 第 6 节六步执行，先 Desktop GUI smoke，再 30–60 分钟基线。
3. **授权管理员对照**（单项授权、短时会话）：定性 Power/WHEA ETW coverage，关闭 R3。
4. **受控游戏周期前**先实现 Game Safe Mode 最小子集（检测 ACE 进程→关 DxgKrnl 实时 ETW 与按进程计数器），否则按审计建议游戏期间不运行 CrashScope。
5. P2 余项：统一 Session ID、Perflib 降噪、自动事故窗口；最后 2–4 小时稳定性。

## 五、总体判定
文档链真实、连续、自我一致，抽查全部属实；工作质量高于常见水准，尤其诚实边界与证据纪律。当前最大的风险不在技术而在流程：**已验证的成果再次处于未提交状态，且三份测试包只有工作树指纹**；最有价值的下一步不是新功能，而是提交固化 + 0.9.4 实机验收。

