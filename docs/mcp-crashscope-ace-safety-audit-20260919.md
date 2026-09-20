# CrashScope 内存开销与 ACE 安全审计

日期：2026-09-19（UTC+8）

关联事件：`docs/mcp-delta-force-memory-incident-20260918.md`

## 结论

1. **现有源码没有读取三角洲进程内存。** 全仓搜索未发现 `OpenProcess`、`ReadProcessMemory`、`WriteProcessMemory`、`CreateRemoteThread`、远程分配、DLL 注入、调试附加、进程模块扫描或全局钩子。
2. **现有证据不支持“CrashScope 导致了主要内存耗尽”。** 资源耗尽时三角洲提交 14.853 GiB；CrashScope 未进入 Windows 事件 2004 的前三名，因此其进程提交量低于第三名 Explorer 的 0.663 GiB。
3. **但不能说记录器完全没有促成作用。** 系统本来就只剩约 0.1 GiB 提交余量；记录器自身、传感器库和 ETW 缓冲均有开销，可能让已经贴线的系统更早触顶。
4. **不能承诺绝对不会被 ACE 封禁或拦截。** 当前包是未签名测试构建，并开启了系统级 DxgKrnl Verbose ETW。它不属于传统读内存外挂行为，但也尚未经过腾讯/ACE 白名单、签名或兼容性认证。
5. 本次 ACE 弹窗有明确原因：事件原文是非正常关机导致反作弊预启动进入静默模式，错误码 `13-262426-0`；没有记录表明 ACE 将 CrashScope 识别为作弊工具。

## 采集行为审计

| 行为 | 当前实现 | 风险解释 |
|---|---|---|
| 读取游戏内存 | 无 | 没有 `ReadProcessMemory` 或等价实现 |
| 写游戏内存/注入 | 无 | 没有远程线程、远程内存、DLL 注入或调试附加 |
| 枚举进程 | 有 | `Process.GetProcesses()`，只保存 PID 和进程名 |
| 查询前台程序 | 有 | `GetForegroundWindow`、`GetWindowThreadProcessId`，只解析 PID/名称 |
| 按进程查询 GPU | 有 | Windows `GPU Process Memory`、`GPU Engine` 只读性能计数器 |
| 硬件传感器 | 有 | `LibreHardwareMonitorLib 0.9.6` 读取 CPU/GPU/RAM 传感器 |
| 系统 ETW | 有 | TraceEvent 创建 DxgKrnl、Kernel-Power、WHEA 实时会话 |
| 游戏覆盖层注入 | 无 | 悬浮条是独立 WPF 窗口，不挂钩游戏渲染链 |
| 自带内核驱动 | 测试包中未发现 | 包内只有 `LibreHardwareMonitorLib.dll`，未发现 `.sys`；实机也未发现 CrashScope/Libre/WinRing 相关已加载服务或驱动 |
| 数字签名 | 无 | Desktop、Agent、Service 和 LibreHardwareMonitorLib 均显示 `NotSigned` |

相关源码：

- `src/CrashScope.Core/Monitoring/ProcessTracker.cs`：枚举 PID/名称。
- `src/CrashScope.Core/Platform/WindowsActivity.cs`：查询前台窗口 PID/名称及系统空闲时间。
- `src/CrashScope.Core/Monitoring/GpuMemoryTracker.cs`：读取 `GPU Process Memory` 性能计数器，构造函数明确使用 `readOnly: true`。
- `src/CrashScope.Core/Monitoring/GpuEngineTracker.cs`：读取 `GPU Engine` 性能计数器。
- `src/CrashScope.Core/Monitoring/HardwareMonitor.cs`：通过 LibreHardwareMonitor 遍历硬件传感器。
- `src/CrashScope.Core/Monitoring/EtwDxgKrnlMonitor.cs` 与 `EtwRealTimeMonitor.cs`：创建系统级实时 ETW 会话。

## 记录器是否导致内存耗尽

### 能确认的上界

21:30:48 的 Windows 事件 2004 原始 XML：

- 系统提交：39.022 GiB / 上限 39.115 GiB。
- 所有普通进程合计提交：29.621 GiB。
- 分页池：1.239 GiB。
- 非分页池：1.854 GiB。
- 进程数：311。
- 第一名三角洲：14.853 GiB。
- 第二名 `msedgewebview2`：1.044 GiB。
- 第三名 Explorer：0.663 GiB。

事件只列前三名。CrashScope 不在其中，因此当时其进程提交量 **低于 0.663 GiB**。这不能给出精确值，但足以排除其是 14–15 GiB 级主占用者。

此外，游戏退出时总提交量在约五秒内下降 6.292 GiB；退出后重试出现两个三角洲 PID并再次触发事件 2004。CrashScope Desktop 正常退出后内存不足仍继续发生，说明根压力独立于记录器存在。

### 不能完全排除的附加开销

DxgKrnl ETW 当前使用：

- `TraceEventLevel.Verbose`
- `ulong.MaxValue` 全关键字
- 对提供者的每个事件先进入托管回调，再过滤感兴趣事件

本次 23 分钟会话收到 191,614,618 个 DxgKrnl 事件，约 13.8 万个/秒，虽然没有匹配事件进入持久队列，仍会产生 CPU、托管回调和 ETW 缓冲开销。事件 2004 记录系统 `EtwB` 非分页池标签约 0.166 GiB；该标签是系统所有 ETW 缓冲合计，不能全部归因 CrashScope，但证明当时 ETW 不是零开销。

因此最严谨的判断是：

- CrashScope **不是主要耗尽源**。
- 在提交余量只剩几十 MiB 的条件下，任何几百 MiB 级应用都可能成为“最后一根稻草”。
- 如果不运行 CrashScope，三角洲可能稍晚触顶，也可能暂时不弹窗；现有历史记录无法反事实证明具体会延后多久。

## ACE 封禁与拦截风险

### 本次事件

没有发现 Code Integrity、AppLocker 或 Defender 将 CrashScope/其模块拦截的事件。ACE 的 Application Popup 明确将原因写为“电脑非正常关机，预启动处于静默模式”，不是“检测到非法模块、调试器、内存读取或注入”。

所以本次不能解释为 CrashScope 被 ACE 判定为外挂，也没有封号证据。

### 未来风险边界

即使不读取进程内存，也无法替 ACE 保证永不误判或永不改变规则。当前需要谨慎的部分是：

- 测试包及依赖未签名。
- 运行时枚举所有进程并按 PID 关联 GPU 性能计数器。
- 启动高流量 DxgKrnl Verbose ETW 会话。
- 使用第三方硬件监控库。
- 产品尚未接受 ACE 官方兼容性测试或白名单审核。

这些行为与传统作弊注入不同，但对于内核级反作弊，“没有读内存”不等于“官方保证兼容”。

## 安全建议

在完成专用游戏模式前，**不要用当前未签名全功能测试包与三角洲同时运行**。最安全的临时做法是游戏时关闭 CrashScope，游戏结束后再收集 Windows 事件与已有日志。

建议实现 `Game Safe Mode`：

1. 检测到 `DeltaForceClient-Win64-Shipping`、`SGuard64` 或 ACE 服务时，默认关闭 DxgKrnl 实时 ETW。
2. 关闭按进程 GPU 计数器、进程枚举、前台 PID 记录和悬浮条，只保留适配器级温度/负载、系统 RAM/commit、磁盘及 Windows 事件。
3. 将 ETW 从 Verbose + 全关键字改为经过验证的窄事件集合；在 ACE 游戏模式中最好完全不用实时 ETW。
4. 增加 CrashScope 自身 Private Bytes、Commit Size、Working Set 和 ETW 缓冲指标，以便精确量化开销。
5. 对发布 EXE/DLL 做可信代码签名，生成可复现哈希，并保留源码到产物的可追溯构建信息。
6. 在无游戏环境先做 30–60 分钟基线，再做离线、非排位、非正式账号的兼容性测试；在获得正式兼容结论前不宣称“零封禁风险”。
