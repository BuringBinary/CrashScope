# CrashScope

**A Windows system flight recorder that captures the final moments before crashes, freezes, GPU failures, and unexpected reboots.**

Windows PC 崩溃"黑匣子"：持续保存死机前的硬件遥测、前台 App、进程变化、用户空闲状态、GPU 引擎利用率、显示拓扑变化、电源状态和 ETW 内核级 GPU/电源/硬件错误事件；如果下一次启动发现上一次没有正常结束，则自动收集 Windows 事件并生成 incident 证据包。

它首先解决一个很具体的问题：**当 AMD 显卡掉驱动或整机硬锁死、只能强制重启时，Windows 往往只留下 Kernel-Power 41，而真正的现场已经消失。**

## 当前 v0.8 能做什么

### 飞行记录器（持续采集）

```text
每 1 秒
  ├─ CPU / GPU / RAM 传感器
  ├─ GPU 温度 / Hotspot / VRAM（设备支持时）
  ├─ GPU Load / Clock / Power / Voltage（设备支持时）
  ├─ 当前前台进程
  └─ 用户多久没有输入

每 2 秒
  ├─ 对比进程列表，记录 App start / stop
  └─ per-process GPU Engine 利用率（3D / VideoDecode / VideoEncode / Copy …）

每 5 秒
  ├─ 检测显示拓扑变化（显示器增删 / 分辨率 / 虚拟显示器驱动接入）
  ├─ 检测远程会话状态（RDP 接管 / 远程登录）
  └─ 采集存储 / IO 遥测（NVMe/SSD 温度 / SMART 健康 / 磁盘 IO 延迟 / 队列深度）

每 10 秒
  └─ 检测电源计划切换（平衡 / 高性能 / 节能 / 卓越性能）

每 30 秒
  └─ 保存一次完整 PID + 进程名快照

会话启动时
  └─ 记录显示驱动清单（厂商 / 版本 / 日期，AMD / NVIDIA / Intel）

持续
  ├─ 更新 session heartbeat（崩溃检测依据）
  ├─ ETW 实时会话：Microsoft-Windows-DxgKrnl（TDR 阶段 / GPU 引擎重置 / 电源组件转换）
  ├─ ETW 实时会话：Microsoft-Windows-Kernel-Power（睡眠/唤醒 / 热节流 / 电源转换）
  └─ ETW 实时会话：Microsoft-Windows-WHEA-Logger（实时硬件错误）
```

### 崩溃后自动分析（下一次启动触发）

```text
如果发现上一 session 没有 clean shutdown：
  ├─ 抽取死机前 telemetry 尾部（含 sensor catalog）
  ├─ 抽取 App / process / GPU Engine 尾部
  ├─ 抽取显示拓扑 / 电源计划 / 远程会话变化尾部
  ├─ 抽取 ETW GPU / Kernel-Power / WHEA 事件尾部（64 MB ring buffer）
  ├─ 查询 Kernel-Power / WHEA / Display / 电源转换 / TerminalServices / UserPnp 事件
  ├─ 扫描 WER 报告 / LiveKernelReports / Minidump
  ├─ 重建死机前统一时间线（timeline.md，含 ETW 事件）
  ├─ 检测 GPU load / clock / power 状态转换
  ├─ 构建图形栈上下文（图形应用在场 / 虚拟显示器 / 驱动版本 / 最后 GPU 使用者）
  └─ 生成根因排序与置信度（summary.md / analysis.json）
```

### ETW 内核级 GPU / 电源 / 硬件错误追踪（v0.5 + v0.6）

不同于事后查询 Windows Event Log（可能被覆盖或丢失），v0.5/v0.6 通过 **ETW 实时会话**在崩溃发生的瞬间就捕获到内核事件：

- **TDR 全阶段追踪**：Timeout → Prepare → Recover → Success/Fail，精确到每个阶段的耗时
- **GPU 引擎重置**：哪个引擎被重置、重置原因
- **GPU 电源组件转换**：各电源组件的状态变化（D0/D1/D2/D3）
- **Kernel-Power 实时事件**：睡眠/唤醒/热节流/电源转换——不再依赖事后 Event Log
- **WHEA 硬件错误实时捕获**：PCIe 错误、缓存错误等硬件级故障
- 所有 ETW 事件写入 `etw-gpu.jsonl` / `etw-power.jsonl` / `etw-whea.jsonl`，纳入假设评分

### 桌面 Dashboard（v0.4）

不再只是命令行 agent，v0.4 提供了完整的桌面 UI：

- **实时硬件仪表盘**：CPU/GPU/RAM 实时读数 + OxyPlot 历史曲线图
- **Live Monitor**：GPU 状态转换实时检测与告警
- **Incident 查看器**：浏览历史崩溃、查看假设评分、导出诊断包
- **Timeline 页面**：死机前完整时间线可视化
- **一键诊断导出**：带隐私脱敏的 ZIP 导出（打码用户名/路径/进程参数）
- **系统托盘**：最小化到托盘、后台常驻、录制状态指示
- **Windows Service 模式**：开机自启、无需登录即可录制
- **可配置保留策略**：Ring buffer 自动清理旧数据

### 跨 Incident 智能分析（v0.7）

单次崩溃分析回答"这次怎么了"，v0.7 回答"这些崩溃之间有什么规律"：

- **跨 Incident 模式挖掘**：共同进程、共同虚拟显示器、共同 GPU 状态走势、共同 ETW 事件模式
- **驱动-失败关联**：驱动版本 vs 失败模式关联表、驱动更新前后失败率对比、回滚建议
- **稳定性趋势**：MTBF（平均无故障时间）计算、按失败模式分类的趋势线、稳定性评分（0-100）
- **推荐引擎**：基于累积证据的可操作建议——"建议回退驱动到 24.5.1，因为升级后发生 5 次 TDR"
- **Health Dashboard 卡片**：桌面主页显示稳定性评分 + 趋势箭头 + 最近 incident 摘要
- **Incident 对比视图**：并排对比两次崩溃的差异和共同点

### 6 类假设评分（v0.2）

对每个 unclean session，同时评估：**硬锁死、蓝屏（bugcheck）、显示驱动超时（TDR）、硬件错误（WHEA）、应用崩溃、突然断电**。每条证据带方向与权重（evidence-for / evidence-against），输出加权得分与归一化置信度。

### 图形栈上下文（v0.3）

针对"打完游戏 → 回启动器 → 远程软件挂着 → 人离开 → 机器卡死"这类故障：

- **谁在用 GPU**：per-process GPU Engine 计数器，死机前最后时刻哪个进程占用 3D / 编解码引擎
- **显示拓扑变化**：虚拟显示器驱动（ToDesk / Parsec / Sunshine / IddSampleDriver / spacedesk 等）接入或移除——高权重证据
- **远程会话活动**：RDP 接管 / 远程登录 + 常见远程控制软件进程检测（ToDesk、向日葵、RustDesk、TeamViewer、UU 远程等）
- **电源状态**：电源计划切换（驱动 P-state 变化的常见触发）
- **驱动清单与变化**：AMD / NVIDIA / Intel 显示驱动版本、日期，窗口内驱动安装/更新事件（UserPnp）
- **图形应用分类**：启动器（WeGame / Steam / …）、覆盖层（RTSS / Game Bar）、硬件加速应用（浏览器 / 聊天 / 视频会议）

## 为什么还监控 App

像"打完三角洲 → 回到 WeGame → UU 远程仍开着 → 人离开 → 机器卡死"这种故障，可能发生在 GPU 从 3D 高负载切回桌面/低功耗、DWM/硬件加速接管、虚拟显示/远程软件仍活跃、显示器关闭或系统进入省电状态的时候。

所以只记 GPU 温度不够。agent 会保存**前台程序、进程生命周期、用户 idle 时间和图形栈活动**，但故意不采集命令行、窗口标题、键盘内容、截图或网络内容。

## 技术栈

- C# / .NET 8 (`net8.0-windows`)
- **采集层**：LibreHardwareMonitorLib 0.9.6（硬件传感器）、Windows Event Log API、ETW 实时会话（Microsoft.Windows.EventTracing.Processing）
- **GPU Engine 性能计数器**：per-process GPU 利用率
- **Win32 API**：`GetForegroundWindow` / `GetLastInputInfo` / `EnumDisplayMonitors` / `EnumDisplayDevices` / `GetSystemMetrics` / `PowerGetActiveScheme` / WMI
- **注册表**：显示驱动清单（`Class\{4d36e968-...}`）
- **桌面 UI**：WPF + OxyPlot（实时图表）
- **存储**：JSONL append-only crash-oriented logging（write-through safety）

## 架构

```
CrashScope.Core/          — 核心分析引擎（与 UI / Agent 解耦）
  ├─ Monitoring/          — 所有采集器（HW/进程/GPU/显示/电源/ETW）
  ├─ Analysis/            — 假设评估 / 时间线 / 图形上下文 / 跨 incident 分析 / 推荐引擎
  ├─ Diagnostics/         — 崩溃证据收集 / 驱动清单 / 外部证据扫描
  ├─ Models/              — 共享数据模型
  ├─ Infrastructure/      — CrashSafeJsonlWriter
  └─ Platform/            — Windows 平台抽象

CrashScope.Agent/         — 后台录制 agent（命令行 / Windows Service / 启动任务）
CrashScope.Desktop/       — WPF 桌面 Dashboard
```

## 运行

前提：Windows 10/11 + .NET 8 SDK。

```powershell
git clone https://github.com/Burning-binary/CrashScope.git
cd CrashScope

# 管理员权限运行 agent（推荐）
powershell -ExecutionPolicy Bypass -File .\scripts\run-admin.ps1

# 或直接 dotnet run
dotnet run --project .\src\CrashScope.Agent\CrashScope.Agent.csproj

# 启动桌面 Dashboard
dotnet run --project .\src\CrashScope.Desktop\CrashScope.Desktop.csproj
```

长期常驻部署：

```powershell
.\scripts\publish.ps1
.\scripts\install-service.ps1        # 安装为 Windows Service
# 或
.\scripts\install-startup-task.ps1   # 安装为计划任务
```

## 数据文件结构

```text
%LOCALAPPDATA%\CrashScope\
├─ state\last-session.json
├─ sessions\<timestamp>\
│  ├─ sensor-catalog.json
│  ├─ driver-inventory.json
│  ├─ telemetry.jsonl
│  ├─ foreground-events.jsonl
│  ├─ process-events.jsonl
│  ├─ process-snapshots.jsonl
│  ├─ gpu-engines.jsonl
│  ├─ display-events.jsonl
│  ├─ power-events.jsonl
│  ├─ remote-session-events.jsonl
  ├─ storage.jsonl               ← v0.8 Storage / IO telemetry
  ├─ etw-gpu.jsonl              ← v0.5 ETW DXGKRNL
│  ├─ etw-power.jsonl            ← v0.6 ETW Kernel-Power
│  └─ etw-whea.jsonl             ← v0.6 ETW WHEA
└─ incidents\<timestamp-session>\
   ├─ previous-session.json
   ├─ telemetry-tail.jsonl
   ├─ foreground-tail.jsonl
   ├─ process-events-tail.jsonl
   ├─ process-snapshots-tail.jsonl
   ├─ gpu-engines-tail.jsonl
   ├─ display-events-tail.jsonl
   ├─ power-events-tail.jsonl
   ├─ remote-session-events-tail.jsonl
   ├─ storage-tail.jsonl          ← v0.8
   ├─ etw-gpu-tail.jsonl
   ├─ etw-power-tail.jsonl
   ├─ etw-whea-tail.jsonl
   ├─ sensor-catalog.json
   ├─ driver-inventory.json
   ├─ windows-events.json（含 EventData 属性）
   ├─ external-evidence.json（WER / LiveKernelReports / Minidump）
   ├─ analysis.json（结构化分析结果）
   ├─ timeline.md（死机前统一时间线）
   └─ summary.md（假设排序 + 置信度 + 图形栈上下文）
```

## 权限

**不需要自己写内核驱动**。普通用户模式可以记录 App/进程等信息；为了尽量完整地读取硬件传感器、ETW 内核事件和 Windows 诊断信息，推荐管理员权限运行。

## 当前最重要的实验

v0.7 所有代码功能已经完成，但 **还缺最关键的一步：在真机上验证**。把 agent 装到那台会随机 A 卡硬锁死的电脑上真实跑起来。下一次复现后，检查 incident 里是否能回答：

1. 故障前 5 分钟 GPU 的负载、频率、功耗、温度怎样变化？
2. 故障进程（游戏/启动器/远程软件）当时是否仍在运行？
3. 死机前最后时刻，哪个进程还在占用 GPU 的 3D / 编解码引擎？
4. 显示拓扑是否发生过变化？ToDesk/Parsec 等虚拟显示器是否接入？
5. 用户是否已经 idle、是否发生过电源计划切换或 RDP 远程接管？
6. Windows 是否留下 TDR、WHEA、Kernel-Power 或 BugCheck 证据？
7. ETW 实时事件是否比事后 Event Log 多捕获了关键信息？
8. 最后一个成功落盘的样本离死机有多近？
9. 显示驱动版本是什么？故障窗口内是否刚发生过驱动安装 / 更新？
10. 多次崩溃之间是否存在共同模式？（v0.7 跨 incident 分析）

如果这些信息仍不足，再决定 v0.8 要增加哪些更深层的监控项，而不是盲目堆数据。

## Status

**v0.8 — Storage / IO Telemetry（代码完成，待 AMD 真机验证）**