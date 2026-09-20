# CrashScope P0 采集链路加固与 0.9.3 测试包

日期：2026-09-19（UTC+8）

关联分析：`docs/mcp-session-deep-analysis-20260918.md`

## 1. 本轮目标

把现场事故分析中的五项 P0 要求落成可运行代码：默认停用高开销 DxgKrnl ETW、增加按进程内存与生命周期、原生捕获事件 2004、保留 GPU adapter/LUID/engine identity，并在系统提交压力升高时自动降载。

## 2. 已实现

### 2.1 安全默认值和提交压力自保护

- 新增 `RecorderOptions`；`EnableDxgKrnlEtw` 默认 `false`，只有显式构造选项才能开启。
- 系统 commit 从五秒采样改为每秒采样。
- 90%：`Warning`，继续完整采集并在 collector health 中告警。
- 95%：`Throttled`，停止已经运行的 GPU/System ETW；进程 lifecycle 降为五秒、外围采集降为十至二十秒。
- 98%：`Critical`，暂停进程、GPU engine/memory、存储、显示和电源轮询；只保留每秒 commit、GPU 温度、前台变化、事件 2004、会话心跳和低开销状态。
- ETW 因压力停止后不会在同一会话中自动恢复，避免压力反复时频繁重建 session。

### 2.2 按进程内存与生命周期

- `ProcessInfo` 和 `ProcessEvent` 新增：创建时间、Private Bytes（Windows 私有提交）、Working Set、句柄数、退出标记和可用时的退出码。
- 周期快照从 30 秒改为 5 秒。
- lifecycle 以 `PID + CreationTime` 区分进程代际；同名 PID 重用会先写旧代 `stop`，再写新代 `start`。
- `stop` 事件保留旧代最后一次已知内存和句柄值，可直接检查“GPU 活动消失后进程 private commit 是否仍未释放”。

### 2.3 Resource-Exhaustion-Detector 2004

- 新增 `ResourceExhaustionEventTracker`，实时监听 System 日志中的 `Microsoft-Windows-Resource-Exhaustion-Detector/2004`。
- 新会话文件：`resource-exhaustion.jsonl`。
- 每条记录保存系统 commit limit/charge、进程 commit 合计、paged/nonpaged pool、物理内存、进程数、top process 的 PID/CreationTime/Commit/HandleCount/Version/TypeInfo，以及完整原始 XML。
- unclean-session 事后证据查询也增加事件 2004，并把 `resource-exhaustion.jsonl` 尾部带入 incident bundle。

### 2.4 GPU adapter/LUID 分层

- 解析 Windows GPU performance-counter instance 中的 PID、adapter LUID、physical adapter index、engine index 和 engine type。
- 通过 `HKLM\SOFTWARE\Microsoft\DirectX` 的 `AdapterLuid` 尽力映射友好适配器名称；无法映射时仍保留原始 LUID/phys identity。
- `gpu-memory.jsonl` 保留原有进程总计字段以兼容旧分析，同时新增每 adapter segment 的 `adapters` 明细。
- `gpu-engines.jsonl` 改为每个 physical engine 单独记录；不再把跨 adapter、跨 engine 的同名 engine type 直接相加。
- 时间线明确标注 adapter/LUID/phys/engine；旧数据标记为 `legacy/unattributed aggregate`。

## 3. 兼容性

- 所有新增模型字段都有默认值；旧 JSONL 仍可反序列化。
- `ProcessGpuMemory.DedicatedBytes/SharedBytes` 仍提供进程级总计，但该总计明确是 adapters 的合计，不能解释为单块独显的物理驻留。
- 现有三参数 `RecorderEngine` 构造调用不需要修改；新增选项为可选第四参数。
- System Power/WHEA ETW 当前仍默认尝试启用；权限失败继续以 collector-health 状态呈现。实时 DxgKrnl 才是本轮强制改为默认关闭的高风险链路。

## 4. 自动验证

| 项 | 结果 |
|---|---|
| Core/Desktop 测试项目 Release 构建 | 成功 |
| Agent Release 构建 | 0 warning / 0 error |
| Service Release 构建 | 0 warning / 0 error |
| 完整单元测试 | 324/324 通过 |
| 测试包门禁 | 288/288 通过 |
| 三宿主 self-contained publish | 成功 |
| ZIP 逐文件 SHA-256 | 975/975 通过 |
| 受保护旧包与 incident 文件 | 54 个哈希全部未变 |
| 构建期间源码指纹 | 一致 |

新增测试覆盖 commit 阈值、DxgKrnl 安全默认值、GPU counter identity 解析、事件 2004 `UserData` 解析、PID 重用和退出时最后内存状态，以及 adapter 明细 JSON roundtrip。

## 5. 测试包

- 版本：`0.9.3-test.20260919092106`
- 构建身份：`0.9.3-test.20260919092106+worktree.485d41ccfe19`
- 目录：`artifacts/test-packages/20260919-092106/`
- ZIP：`CrashScope.Toolbox.0.9.3-test.20260919092106.win-x64.zip`
- 大小：157,942,456 字节
- SHA-256：`EA9C5AEEC31F8D2F442FD86DE652DED2FCE4396DB8002140D5BD0984517C5948`
- 启动器：`artifacts/test-packages/20260919-092106/Toolbox/Start-CrashScope.cmd`

脚本结论为 `PACKAGE VERIFIED`。发布过程没有安装服务、没有启动 Desktop，也没有覆盖旧包。`desktopGuiSmokeExecuted=false`，因此该包当前只完成构建与静态/单测门禁，尚未完成 W3 实机界面和自然负载验收。

## 6. 下一步验收

1. 先退出旧 CrashScope，不覆盖旧目录；从上述独立测试包启动。
2. 做 30–60 分钟无游戏基线，确认 `ETW GPU=Disabled`、每秒 `memory-pressure.jsonl`、每五秒带内存字段的 `process-snapshots.jsonl`、空的或有效的 `resource-exhaustion.jsonl`，以及 CrashScope 自身开销。
3. 基线通过后再做受控游戏周期：冷启动、进入对局、退出、等待三分钟、第二次启动。
4. 验收旧 PID 的 Private Bytes 是否在 30 秒内归零、是否发生 PID 代际重叠、GPU 明细能否区分 RX 9070 XT/集显/虚拟适配器，以及 95/98% 自保护是否按预期触发。
5. 最后进行 2–4 小时稳定性和自身开销测试；这些属于 P2，不能由本轮单元测试替代。

## 7. 尚未声称完成的事项

- 未执行 Desktop GUI smoke。
- 未执行 30–60 分钟基线或游戏实机复测。
- 未证明旧 PID 8.38 GiB 残留一定会再次发生；本轮只是补齐了直接观测它所需的采集字段。
- CPU fallback、DWM baseline 强制验收、页面文件现场快照、CrashScope 自身 CPU/Commit 指标和统一 Session ID 仍属于后续 P1/P2。
