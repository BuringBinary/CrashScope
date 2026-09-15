# CrashScope

**A Windows system flight recorder that captures the final moments before crashes, freezes, GPU failures, and unexpected reboots.**

Windows PC 崩溃“黑匣子”：持续保存死机前的硬件遥测、前台 App、进程变化和用户空闲状态；如果下一次启动发现上一次没有正常结束，则自动收集 Windows 事件并生成 incident 证据包。

它首先解决一个很具体的问题：**当 AMD 显卡掉驱动或整机硬锁死、只能强制重启时，Windows 往往只留下 Kernel-Power 41，而真正的现场已经消失。**

## 当前 v0.3 能做什么

```text
每 1 秒
  ├─ CPU / GPU / RAM 传感器
  ├─ GPU 温度 / Hotspot（设备支持时）
  ├─ GPU Load / Clock / Power / Voltage（设备支持时）
  ├─ 当前前台进程
  └─ 用户多久没有输入

每 2 秒
  ├─ 对比进程列表，记录 App start / stop
  └─ per-process GPU Engine 利用率（3D / VideoDecode / VideoEncode / Copy …）

每 5 秒
  ├─ 检测显示拓扑变化（显示器增删 / 分辨率 / 虚拟显示器驱动接入）
  └─ 检测远程会话状态（RDP 接管 / 远程登录）

每 10 秒
  └─ 检测电源计划切换（平衡 / 高性能 / 节能 / 卓越性能）

每 30 秒
  └─ 保存一次完整 PID + 进程名快照

会话启动时
  └─ 记录显示驱动清单（厂商 / 版本 / 日期，AMD / NVIDIA / Intel）

持续
  └─ 更新 session heartbeat

下一次启动
  └─ 如果发现上一 session 没有 clean shutdown：
       ├─ 抽取死机前 telemetry 尾部（含 sensor catalog）
       ├─ 抽取 App / process / GPU Engine 尾部
       ├─ 抽取显示拓扑 / 电源计划 / 远程会话变化尾部
       ├─ 查询 Kernel-Power / WHEA / Display / 电源转换 / TerminalServices / UserPnp 事件
       ├─ 扫描 WER 报告 / LiveKernelReports / Minidump
       ├─ 重建死机前时间线（timeline.md）
       ├─ 检测 GPU load / clock / power 状态转换
       ├─ 构建图形栈上下文（图形应用在场 / 虚拟显示器 / 驱动版本 / 最后 GPU 使用者）
       └─ 生成根因排序与置信度（summary.md / analysis.json）
```

### v0.3 的图形栈上下文

针对"打完游戏 → 回启动器 → 远程软件挂着 → 人离开 → 机器卡死"这类故障，v0.3 额外捕获并纳入证据评分：

- **谁在用 GPU**：per-process GPU Engine 计数器，死机前最后时刻哪个进程占用 3D / 编解码引擎
- **显示拓扑变化**：虚拟显示器驱动（ToDesk / Parsec / Sunshine / IddSampleDriver / spacedesk 等）接入或移除的时间点——高权重证据
- **远程会话活动**：RDP 接管 / 远程登录事件，以及常见远程控制软件（ToDesk、向日葵、RustDesk、TeamViewer、UU 远程等）的进程在场状态
- **电源状态**：电源计划切换（驱动 P-state 变化的常见触发）
- **驱动清单与变化**：AMD / NVIDIA / Intel 显示驱动版本、日期，窗口内的驱动安装 / 更新事件（UserPnp）
- **图形应用分类**：启动器（WeGame / Steam / …）、覆盖层（RTSS / Game Bar）、硬件加速应用（浏览器 / 聊天 / 视频会议）在死机前是否仍在运行

### v0.2 的相关性分析

对每个 unclean session，会同时评估 6 类假设：**硬锁死、蓝屏（bugcheck）、显示驱动超时（TDR）、硬件错误（WHEA）、应用崩溃、突然断电**。每条证据带方向与权重（evidence-for / evidence-against），输出加权得分与归一化置信度。判定依据包括 Kernel-Power 41 的 BugcheckCode / PowerButtonTimestamp 事件数据、遥测是否写到最后一刻、死机前的用户 idle 与 GPU 负载组合、GPU 负载塌落转换、显示拓扑变化与远程软件活动等。分析结论明确标注为"证据相关性排序"，不是最终判决。

## 为什么还监控 App

像“打完三角洲 → 回到 WeGame → UU 远程仍开着 → 人离开 → 机器卡死”这种故障，可能发生在 GPU 从 3D 高负载切回桌面/低功耗、DWM/硬件加速接管、虚拟显示/远程软件仍活跃、显示器关闭或系统进入省电状态的时候。

所以只记 GPU 温度不够。agent 会保存**前台程序、进程生命周期、用户 idle 时间和图形栈活动**，但故意不采集命令行、窗口标题、键盘内容、截图或网络内容。

## 技术栈

- C# / .NET 8 (`net8.0-windows`)
- LibreHardwareMonitorLib 0.9.6
- Windows Event Log API
- GPU Engine 性能计数器（per-process GPU 利用率）
- Win32 `GetForegroundWindow` / `GetLastInputInfo` / `EnumDisplayMonitors` / `EnumDisplayDevices` / `GetSystemMetrics` / `PowerGetActiveScheme`
- 注册表显示驱动清单（`Class\{4d36e968-...}`）
- JSONL append-only crash-oriented logging

## 运行

前提：Windows 10/11 + .NET 8 SDK。

```powershell
git clone https://github.com/Burning-binary/CrashScope.git
cd CrashScope
powershell -ExecutionPolicy Bypass -File .\scripts\run-admin.ps1
```

直接开发运行也可以：

```powershell
dotnet run --project .\src\CrashScope.Agent\CrashScope.Agent.csproj
```

日志默认写到：

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
│  └─ remote-session-events.jsonl
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
   ├─ sensor-catalog.json
   ├─ driver-inventory.json
   ├─ windows-events.json（含 EventData 属性）
   ├─ external-evidence.json（WER / LiveKernelReports / Minidump）
   ├─ analysis.json（结构化分析结果，含图形栈上下文）
   ├─ timeline.md（死机前统一时间线）
   └─ summary.md（假设排序 + 置信度 + 图形栈上下文）
```

## 权限

第一版**不需要自己写内核驱动**。普通用户模式可以记录 App/进程等信息；为了尽量完整地读取硬件传感器和 Windows 诊断信息，推荐管理员权限运行。

长期常驻可先用计划任务，不必每次手动点 UAC：

```powershell
.\scripts\publish.ps1
.\scripts\install-startup-task.ps1 -ExecutablePath .\dist\win-x64\CrashScope.Agent.exe
```

## 当前最重要的实验

先不要急着做 GUI。把 agent 装到那台会随机 A 卡硬锁死的电脑上真实跑起来。下一次复现后，检查 incident 里是否能回答：

1. 故障前 5 分钟 GPU 的负载、频率、功耗、温度怎样变化？
2. 三角洲什么时候退出？WeGame/UU/其他图形应用当时是否仍在运行？
3. 死机前最后时刻，哪个进程还在占用 GPU 的 3D / 编解码引擎？
4. 显示拓扑是否发生过变化？ToDesk/Parsec 等虚拟显示器是否接入？
5. 用户是否已经 idle、是否发生过电源计划切换或 RDP 远程接管？
6. Windows 是否留下 TDR、WHEA、Kernel-Power 或 BugCheck 证据？
7. 最后一个成功落盘的样本离死机有多近？
8. 显示驱动版本是什么？故障窗口内是否刚发生过驱动安装 / 更新？

如果这些信息仍不足，再决定后续版本要增加哪些 ETW 与更深的 GPU 内部计数器，而不是盲目堆监控项。

## Status

v0.3 — graphics-stack context. 先抓真实故障，再迭代诊断模型。
