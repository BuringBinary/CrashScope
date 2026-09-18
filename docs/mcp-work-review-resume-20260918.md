# CrashScope 阶段工作评审与续接方案

日期：2026-09-18。本文是对本工作区此前全部会话产物的横向评审，并给出续接工作单。
评审方法：通读 docs/ 下全部阶段报告，重新测量 git 树状态，并对关键声明做抽查复核。

## 一、工作史重构

| 阶段 | 日期 | 交付 | 验证 | 报告 |
|---|---|---|---|---|
| Phase 0 代码评审与修复 | 09-17 | 30 项评审发现全部关闭；测试 128→189 | 189/189，提交 b445ccb/40a0985/8572b2d | docs/mcp-code-review.md、docs/mcp-phase0-changes.md、docs/mcp-phase0-followup.md、docs/mcp-status-assessment.md |
| 可靠性收敛（R1-R5） | 09-17 | 恢复队列、单写者锁、停止生命周期、独立会话目录、清理保护 | 176/176；随 f2d5e06 提交 | docs/mcp-reliability-convergence.md |
| 打包就绪评估 | 09-17 | 发布门槛与产物形态建议 | 无代码变更 | docs/mcp-release-readiness.md |
| 实机数据评估 | 09-18 | 判定两条 HardFreeze/100% 为过度确定；显存 30 GiB 计数与独显传感器冲突；日志证实图表负索引缺陷 | 只读，未改代码 | docs/mcp-real-machine-data-assessment.md |
| 实机修复 | 09-18 | 图表负索引、保守诊断、只读重评、CollectorHealthTracker、可疑零值过滤 | 200/200 | docs/mcp-real-machine-fixes.md |
| 独立测试包 | 09-18 | 0.9.1-test.20260918102913（ZIP 158 MB + 哈希 + 清单） | 200/200 + 975 文件逐项核验 | docs/mcp-test-package-20260918.md |
| 新版切换与实机验收 | 09-18 | 切换到新版并完成 5 分 25 秒实录（324 样本）+ Stop/Start（37 样本） | 备份 137 文件哈希复核；会话 cleanShutdown=true | docs/mcp-upgrade-acceptance-20260918.md |
| 后续规划 | 09-18 | P0-P3 分阶段计划 | 计划本身 | docs/mcp-next-steps-20260918.md |
| 采集链路首轮修复 | 09-18 | 磁盘归属修正、ETW 覆盖语义与生命周期、CPU 健康评估 | 37 项新测试 + 237/237 回归 | docs/mcp-collection-chain-20260918.md |

## 二、评审结论

**总体判定：工作链真实、连续、自我一致，质量高于常见水准；主要缺口不在已完成的部分，而在"已修复但未部署验证"的最后一段。**

核实通过的关键点：

1. 文档与树一致：git status 实测 31 个修改 + 20 个未跟踪文件，与 docs/mcp-real-machine-fixes.md、docs/mcp-collection-chain-20260918.md 及 UI 修复各轮声称的变更集合完全对得上（8 个新源文件、2 个新测试文件、1 个新脚本、9 份新文档）。
2. 抽查属实：scripts/publish-test-package.ps1 第 115 行回归集合确实包含 CollectionChainTests，第 121 行门禁确为 total<237 即失败；test/CrashScope.Core.Tests/CollectionChainTests.cs 实测存在且语义与文档一致（Started≠Observed、drain 恰好一次、权限错误不被覆盖、磁盘归属不猜测）。
3. 诚实度突出：全部报告明确区分"已证实/未验证/不可能知道"，把 Started 与事件覆盖分开、把 N/A 与伪装零值分开、把 Unclassified 与确诊分开，并一致标注每轮"未做"的边界。
4. 证据保护纪律良好：备份 + 逐文件 SHA256、独立 BuildId 目录、旧包与历史证据零覆盖、数据根锁。

## 三、当前状态快照（本次实测）

| 项 | 状态 |
|---|---|
| 分支 / HEAD | feature/local-desktop-work / f540c0f |
| 未提交变更 | 31 M + 20 ??（覆盖 09-18 全部三轮修复与文档） |
| 正在运行的实例 | 仍是已验收的 0.9.1.0 独立包（不含采集链路修复） |
| 数据保护 | 备份 artifacts/data-backups/20260918-105829-before-0.9.1 完好；191 个数据根文件此前已核验不变 |
| 采集能力 | 磁盘修复已过单元+只读探针；ETW 新代码未做实时端到端；ETW System 权限错误未解；CPU 温度/部分频率功耗、SMART 仍缺 |
| 遗留警告 | 测试工程既存 RpcTests.cs:151 CS1998；推送 origin 需本机代理 127.0.0.1:7897 |

## 四、风险与缺口（按优先级）

1. R1（高）：约 51 个文件的已完成、已验证工作全部未提交。这是 09-17 就被标记过的最大风险，现在规模翻倍。任何误操作（checkout/clean/磁盘故障）都会损失两轮实机修复。
2. R2（高）：采集链路新代码只有单元测试和一个只读磁盘探针支撑，没有经过真实 ETW 会话与磁盘落盘的端到端验收。当前实机运行的仍是不含修复的 0.9.1.0。
3. R3（中）：ETW System 的 E_ACCESSDENIED 未定位（普通权限限制 vs 可修复配置），要么做一次授权的管理员对照，要么正式记录为普通权限下的已知不可用。
4. R4（中）：两条历史 incident 仍是 Unclassified，P0 的现象确认（09:07/09:16 到底发生了什么）只有用户能补。
5. R5（低）：GPU 进程显存的 adapter/LUID 口径问题只是被"去因果化"挡住，修正未做；OS 侧 ETW 丢失量未测量。
6. R6（低）：P2 中时稳定性（30-60 分钟 → 2-4 小时）与 P3 正式发布全部未启动。

## 五、续接工作单（建议顺序）

### W1 提交基线（先行，零风险，约 15 分钟）

按逻辑分块提交现有树：(a) 实机修复轮（图表/诊断/健康跟踪 + 测试）；(b) 采集链路轮（ETW/存储/CPU + 37 测试 + 门禁）；(c) 打包脚本；(d) docs 全部报告。分块或单提交皆可，以 docs 为评审记录。推送 origin 需要走本机 Clash 代理，另行确认。

### W2 发布新独立测试包

运行 scripts/publish-test-package.ps1 生成新 BuildId 目录（不覆盖 20260918-102913），自动执行 237 项门禁回归，产出 ZIP + 哈希 + 清单。预计 10-20 分钟。

### W3 新包实机切换与短时验收（P1 收口）

正常停止当前 0.9.1.0 Desktop → 备份 → 启动新包 → 5-10 分钟录制（含 Home/Dashboard 切换与 Stop/Start）→ 核对：storage.jsonl 出现按盘归属的有效指标、collector-health.json 中 ETW 呈现 Started/Receiving/Observed 三态、CPU 缺失项仍为 N/A、旧 evidence 不被改写。验收标准沿用 docs/mcp-next-steps-20260918.md 的 P1。

### W4 P0 现象确认（需要用户输入，可与 W2/W3 并行）

用户补充 09:07 与 09:16 当时的实际现象（整机无响应？黑屏恢复？仅应用退出？是否长按电源/出现蓝屏？），据此把两条 Unclassified 记录标注为可解释状态或保持未分类。

### W5 ETW 权限对照（需单独授权）

仅在用户明确授权后，做一次短时、范围明确的管理员对照录制，验证 Kernel-Power/WHEA 在提权下能否启用与收到事件；不做常驻提权、不装服务。若普通权限下确不可用，则在健康语义中正式标注。

### W6 后续（W3 通过后另行排期）

GPU 显存 adapter/LUID 口径修正；OS ETW 丢失量；P2 的 30-60 分钟与 2-4 小时稳定性观察；P3 发布事项。

## 六、决策点

1. W1 提交由代理执行还是用户自己提交？提交粒度按上述四块还是单块？
2. 是否现在授权 W2+W3（打新包并实机切换验收）？
3. W4 的现象描述可否现在提供？
4. W5 是否授权？若不授权，按"普通权限已知不可用"处理。

## 七、决策记录（2026-09-18 本会话）

- W1：用户选择自行提交。建议按 src+test / 打包脚本 / docs 三块提交（src 内两轮修复存在同文件交叉，按轮拆分会产生不可构建的中间提交，不推荐）。
- W1 完成记录（2026-09-18）：用户安装正式 Git for Windows 2.55.0.windows.3 后自行提交，工作树已清零——`0a423ba`（代码 41 文件，+1502/-855）、`f3959cc`（打包脚本 +220）、`64c21d0`（docs 10 文件，+1033）。R1 风险关闭。git add 时的 LF→CRLF 提示为仓库既有换行转换行为，非错误。推送 origin 尚未执行（需代理）。
- W2+W3：暂不授权。新包发布与实机切换延后；实机继续运行已验收的 0.9.1.0，R2 缺口保持开放。
- W4：用户确认记不清 09:07/09:16 现象。两条 incident 保持 Unclassified，P0 仍开放，不据此改写任何历史分析。
- W5：延后。ETW System 的 E_ACCESSDENIED 暂按"未定位"记录，不预设是权限限制还是可修复配置。
- 环境备注：本机无系统级 Git，唯一可用 git 为 ShunCode 便携版 `D:\ShunCode\resources\app\extensions\shuncode\runtime\git\mingw64\bin\git.exe`（2.55.0.windows.5）。PowerShell 中使用需先把它加入 PATH，或直接用 ShunCode 内置 bash 终端。用户的 PowerShell 为 5.1，不支持 `&&` 分隔符。
