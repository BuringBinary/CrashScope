# CrashScope

**A Windows system flight recorder that captures the final moments before crashes, freezes, GPU failures, and unexpected reboots.**

Windows PC 崩溃“黑匣子”：持续保存死机前的硬件遥测、前台 App、进程变化和用户空闲状态；如果下一次启动发现上一次没有正常结束，则自动收集 Windows 事件并生成 incident 证据包。

它首先解决一个很具体的问题：**当 AMD 显卡掉驱动或整机硬锁死、只能强制重启时，Windows 往往只留下 Kernel-Power 41，而真正的现场已经消失。**

## 当前 v0.1 能做什么

```text
每 1 秒
  ├─ CPU / GPU / RAM 传感器
  ├─ GPU 温度 / Hotspot（设备支持时）
  ├─ GPU Load / Clock / Power / Voltage（设备支持时）
  ├─ 当前前台进程
  └─ 用户多久没有输入

每 2 秒
  └─ 对比进程列表，记录 App start / stop

每 30 秒
  └─ 保存一次完整 PID + 进程名快照

持续
  └─ 更新 session heartbeat

下一次启动
  └─ 如果发现上一 session 没有 clean shutdown：
       ├─ 抽取死机前 telemetry 尾部
       ├─ 抽取 App / process 尾部
       ├─ 查询 Kernel-Power / WHEA / Display / 6008 / 1001 等事件
       └─ 创建 incidents/<time>/ 证据包
```

## 为什么还监控 App

像“打完三角洲 → 回到 WeGame → UU 远程仍开着 → 人离开 → 机器卡死”这种故障，可能发生在 GPU 从 3D 高负载切回桌面/低功耗、DWM/硬件加速接管、虚拟显示/远程软件仍活跃、显示器关闭或系统进入省电状态的时候。

所以只记 GPU 温度不够。v0.1 会保存**前台程序、进程生命周期和用户 idle 时间**，但故意不采集命令行、窗口标题、键盘内容、截图或网络内容。

## 技术栈

- C# / .NET 8 (`net8.0-windows`)
- LibreHardwareMonitorLib 0.9.6
- Windows Event Log API
- Win32 `GetForegroundWindow` / `GetLastInputInfo`
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
│  ├─ telemetry.jsonl
│  ├─ foreground-events.jsonl
│  ├─ process-events.jsonl
│  └─ process-snapshots.jsonl
└─ incidents\<timestamp-session>\
   ├─ previous-session.json
   ├─ telemetry-tail.jsonl
   ├─ foreground-tail.jsonl
   ├─ process-events-tail.jsonl
   ├─ process-snapshots-tail.jsonl
   ├─ windows-events.json
   └─ summary.md
```

## 权限

第一版**不需要自己写内核驱动**。普通用户模式可以记录 App/进程等信息；为了尽量完整地读取硬件传感器和 Windows 诊断信息，推荐管理员权限运行。

长期常驻可先用计划任务，不必每次手动点 UAC：

```powershell
.\scripts\publish.ps1
.\scripts\install-startup-task.ps1 -ExecutablePath .\dist\win-x64\CrashScope.Agent.exe
```

## 当前最重要的实验

先不要急着做 GUI。把 v0.1 装到那台会随机 A 卡硬锁死的电脑上真实跑起来。下一次复现后，检查 incident 里是否能回答：

1. 故障前 5 分钟 GPU 的负载、频率、功耗、温度怎样变化？
2. 三角洲什么时候退出？WeGame/UU/其他图形应用当时是否仍在运行？
3. 用户是否已经 idle、显示/电源状态是否可能正在切换？
4. Windows 是否留下 TDR、WHEA、Kernel-Power 或 BugCheck 证据？
5. 最后一个成功落盘的样本离死机有多近？

如果这些信息仍不足，再决定 v0.2 要增加哪些 ETW、GPU Engine、远程/显示拓扑和电源事件，而不是盲目堆监控项。

## Status

Early prototype / v0.1. 先抓真实故障，再迭代诊断模型。
