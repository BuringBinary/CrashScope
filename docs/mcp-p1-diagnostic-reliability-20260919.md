# CrashScope P1 诊断可信度加固与 0.9.4 测试包

日期：2026-09-19（UTC+8）

关联材料：

- `docs/mcp-session-deep-analysis-20260918.md`
- `docs/mcp-p0-collection-hardening-20260919.md`
- `.mcp-probes/checkpoints/20260919-p0-collection-hardening/`

## 1. 本轮结论

P1 代码、确定性测试、15 秒真实采集链 smoke 和独立 0.9.4 测试包已经完成。新增证据解决了此前“采集器本身是否造成压力”“GPU 文件为空是空闲还是失败”“DWM 是否曾正常”“页面文件当时如何配置”“非管理员权限到底降级了什么”等不可判定点。

本轮没有声称完成 Desktop GUI、30–60 分钟无游戏基线、完整的受控游戏退出/重启周期或 2–4 小时稳定性验收；这些仍须使用新包继续执行。

## 2. 已实现

### 2.1 CrashScope 自身开销与 ETW 速率

- 新会话文件 `collector-overhead.jsonl`，每五秒记录当前 CrashScope 宿主及 Desktop/Agent/Service 进程的 PID、代际、CPU、Private Bytes、Working Set、句柄和线程。
- 对每个启用的 ETW provider 计算 received/matched/written 每秒速率。
- ETW coverage 分开记录进程内 handoff buffer drop 与 Windows ETW session 的 `EventsLost`，不再混为一个“丢事件”数字。
- incident recovery 会复制 `collector-overhead-tail.jsonl`。

### 2.2 GPU engine heartbeat

- `gpu-engines.jsonl` 不再只在超过 0.5% 活动阈值时写入。
- 每次采样明确写 `Observed`、`Idle`、`Unavailable`、`Error`；临界 commit 压力下写 `SuppressedByPressure`，避免把主动降载误判为采集失败。
- heartbeat 同时保留 counter instance 数；有活动时继续保留 adapter LUID、physical adapter index、engine index/type 和友好 adapter 名称。

### 2.3 DWM baseline 与健康状态

- 录制初始化时立即调用 `DwmIsCompositionEnabled` 并写入 `dwm-state.jsonl` baseline，不再等待状态变化。
- collector health 区分 `Collecting` 与 `Unavailable`，失败时保存 HRESULT。
- 后续仍只在 composition 真正变化时追加 transition，避免重复噪声。

### 2.4 页面文件现场快照

- 新会话文件 `pagefile.jsonl`；启动即写 baseline，正常压力下每 30 秒更新。
- 记录 `AutomaticManagedPagefile`、每个 pagefile 的路径/卷、allocated/current/peak、配置 initial/max、卷可用空间和 temporary 标记。
- 每秒观察系统 commit limit；发生变化时立即写 `CommitLimitChanged`，带前后值，并复用最近一次页面文件配置以避免变化瞬间执行额外 WMI 查询。
- incident recovery 会复制 `pagefile-tail.jsonl`。

### 2.5 权限预检与 CPU fallback

- 启动时先记录 `Permission Preflight`。标准用户不会再盲目创建要求管理员权限的 System Power/WHEA ETW session，而是直接标记 `PermissionRequired`；DxgKrnl 仍保持安全默认 `Disabled`。
- 管理员会话才尝试启动 System ETW；即使已提权，也仍须以 provider 的 received/matched/written/rate/loss 证明实际覆盖。
- 新增 Windows PDH `Processor(_Total)\\% Processor Time` fallback，写入 `cpu-fallback.jsonl`。LibreHardwareMonitor CPU 指标不完整但 PDH 有效时，CPU health 标记为 `FallbackActive`，而不是把缺失温度/时钟误当作总 CPU 使用率不可用。
- 临界 commit 压力下 CPU fallback 也写 `SuppressedByPressure` heartbeat，不额外查询计数器。

## 3. 自动验证

| 项 | 结果 |
|---|---|
| Core/Desktop Release 构建 | 0 warning / 0 error |
| Agent Release 构建 | 0 warning / 0 error |
| Service Release 构建 | 0 warning / 0 error |
| 完整单元测试 | 333/333 通过 |
| P1 定向测试 | 9/9 通过 |
| 测试包门禁 | 297/297 通过 |
| MCP workspace diagnostics | 0 warning / 0 error |
| `git diff --check` | exit 0 |

P1 测试覆盖：多核归一化自身 CPU、ETW 速率与计数器 reset、handoff/OS loss 分离、DWM baseline/transition/HRESULT failure、页面文件配置和 commit-limit change、GPU heartbeat 状态，以及 CPU fallback 的范围限制和错误降级。

## 4. 真实采集链 smoke

证据目录：`artifacts/p1-smoke/20260919-101244/`

- 使用当前源码构建的真实 `RecorderEngine`，隔离数据根目录，标准用户身份，持续 15 秒后通过 `StopAsync` 正常停止。
- 结果：`SampleCount=15`、`State=Stopped`、`LastError=null`、`RecoveryError=null`。
- 16 个 JSONL 文件共 70 行，逐行反序列化坏行 0。
- `dwm-state.jsonl` 启动即有 hardware composition enabled baseline。
- `pagefile.jsonl` 实测 `AutomaticManagedPagefile=true`，`C:\\pagefile.sys` allocated 15,069,085,696 bytes、current 477,102,080、peak 624,951,296、卷可用 29,851,824,128 bytes。
- `gpu-engines.jsonl` 第一条为 `Idle` heartbeat（429 instances），随后在 RX 9070 XT 的 LUID/phys/engine identity 下写入 `Observed`。当时游戏进程正在运行，因此这只是短时自然负载观测，不是受控“退出—等待—重启”验收。
- `cpu-fallback.jsonl` 每秒产生有效 PDH 总 CPU 使用率；collector health 为 `FallbackActive`，同时保留 LibreHardwareMonitor 哪些 CPU 温度/时钟/功耗字段不完整的说明。
- collector overhead 的两个有效 CPU 窗口为约 1.05% 和 0.66%；最后一条 private 约 41.8 MiB、working set 约 91.5 MiB。由于当时存在游戏负载且窗口只有 15 秒，这些数字只证明指标可用，不能替代 30–60 分钟基线。
- 标准用户预检正确显示 Power/WHEA `PermissionRequired`，没有先触发一次 `E_ACCESSDENIED` 再降级；事件 2004 watcher 为 `Watching`，DxgKrnl 为安全默认 `Disabled`。

## 5. 0.9.4 独立测试包

- 版本：`0.9.4-test.20260919101500`
- 构建身份：`0.9.4-test.20260919101500+worktree.fb9b88c849ff`
- 目录：`artifacts/test-packages/20260919-101500/`
- ZIP：`CrashScope.Toolbox.0.9.4-test.20260919101500.win-x64.zip`
- 大小：157,989,617 字节
- SHA-256：`162621BA7D1226C1645B95BB71FE50DDD46D56A62448040E8EA33F93288E20BC`
- ZIP 逐文件验证：975/975
- 受保护旧文件：54/54 未变
- `sourceUnchanged=true`
- `desktopGuiSmokeExecuted=false`

`verification-result.json` 记录值与 ZIP 实际重算值一致；0.9.3 P0 ZIP 也再次重算为 `EA9C5AEEC31F8D2F442FD86DE652DED2FCE4396DB8002140D5BD0984517C5948`，未被覆盖。三个宿主的 self-contained 目录均包含新增的 `System.Management.dll`。

## 6. 下一步实机验收

1. 等当前游戏/录制活动结束后，先正常停止并退出所有旧 CrashScope 宿主；不要强杀、不要覆盖旧目录。
2. 从 0.9.4 独立目录运行 `Start-CrashScope.cmd`，执行 Desktop GUI smoke：Start、样本递增、页面切换、Stop/Start、托盘正常退出。
3. 以标准用户做 30–60 分钟无游戏基线；确认 overhead、CPU fallback、GPU heartbeat、DWM/pagefile baseline 及所有 JSONL 可解析。
4. 如要验收 Power/WHEA ETW，另做一次明确的管理员启动，并要求 Session `Elevated=true`；只凭 `Started` 不算覆盖成功，必须检查 provider rate、matched/written 和 loss。
5. 执行受控游戏周期：冷启动、进入对局、退出、等待三分钟、第二次启动；检查旧 PID Private Bytes 是否释放、是否与新代重叠、commit limit/pagefile 是否变化。
6. 最后进行 2–4 小时稳定性测试，统计 CrashScope CPU/private/working set/handles/threads 的趋势和 JSONL 增长率。

## 7. 边界

- 本轮未修改或覆盖用户已有工作树改动，也未创建 Git commit、reset、clean 或 checkout。
- 15 秒 smoke 不是 Desktop GUI smoke，也不是无游戏 baseline；当时游戏进程仍在运行。
- 标准用户 smoke 没有启动任何 ETW provider，因此只验证了权限预检和非 ETW 链路，尚未验证管理员条件下的真实 ETW 事件率与 OS loss。
- 页面文件 `ConfiguredInitialBytes/ConfiguredMaximumBytes` 在 Windows 自动管理模式下可能为 `null`；allocated/current/peak 和 `AutomaticManagedPagefile=true` 才是该模式下的现场事实。
