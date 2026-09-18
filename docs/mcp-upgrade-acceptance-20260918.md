# CrashScope 新版切换与实机验收报告

日期：2026-09-18；时间均为 UTC+8。最终状态复核：11:10:28。

## 结论

**已代办完成新版切换。普通权限下的启动、录制、页面切换、停止后重启和正常收尾通过；采集覆盖仍为降级状态，不等于全部采集器正常，也不证明整机故障已解决。**

- 当前仅新版 Desktop 进程运行，PID `67624`，窗口可见且响应正常。
- 版本 `0.9.1.0`，运行时 `8.0.31`，普通用户权限。
- 最终录制状态 **STOPPED**；按钮显示 Start，界面明确显示 `Collection: stopped (no live readings)`。
- 未保留后台测试录制，不提供对话结束后的持续在线监控。

## 1. 授权范围与切换

按用户选择的“升级并验收”执行：正常退出旧版、完整备份、启动独立新版、录制约五分钟、检查数据和日志，最后停止录制并保留新版窗口。

旧版当时已处于 STOPPED。10:57:42 经托盘 Exit 正常退出，未强杀。未执行自动提权、服务安装、计划任务修改或驱动修改操作。旧 `dist` 包未覆盖；本轮没有修改产品源码或重新打包。

新版入口：

```text
D:\workspace\workspace\CrashScope\artifacts\test-packages\20260918-102913\Toolbox\Desktop\CrashScope.Desktop.exe
```

11:00:03 启动；启动日志确认完整版本 `0.9.1-test.20260918102913+worktree.7618a83baead`。启动前复核 ZIP SHA256 及展开 Desktop 的 501 个文件，与交付清单一致。包内启动器仍为 `Toolbox\Start-CrashScope.cmd`。

## 2. 备份与原始证据保护

完整备份位置：

```text
D:\workspace\workspace\CrashScope\artifacts\data-backups\20260918-105829-before-0.9.1
```

- `CrashScope\` 是原数据根 `%LOCALAPPDATA%\CrashScope` 的完整副本。
- `manifest.json` 保存逐文件 SHA256：137 个文件，共 4,837,243 字节。
- 复制前、复制后及备份文件哈希均一致；11:10 最终复核仍全部匹配。
- 原数据中既有 `sessions\`、`incidents\` 下 **131 个文件全部保持原 SHA256**，其中两个历史事件目录共 46 个文件。
- 与备份相比，既有文件仅 `logs\startup.log`、`state\last-session.json` 因正常运行而变化。新增测试会话单独保存；事件目录集合未增加。
- 新旧版本共享数据根，不是数据沙箱。回退时应先正常停止、退出当前版本，并保留新增会话；不要直接覆盖回滚整个数据目录。

## 3. 真实录制验收

| 阶段 | 会话时间 | 遥测条数 | 结果 |
|---|---|---:|---|
| 首次自动化预检 | 11:02:06–11:02:50 | 38 | 探针变量命名错误触发正常清理；应用未崩溃，正常停止 |
| 正式主录制 | 11:03:18–11:08:43，约 5 分 25 秒 | **324** | 10 次 Home / Dashboard 往返，样本持续增长 |
| Stop → Start 验收 | 11:08:43–11:09:21，约 38 秒 | **37** | 新会话标识、新目录，额外一次页面往返，最后正常停止 |

正式主会话 ID：`198a7aa5b341420b913c3ad2bf832f6d`。

重启会话 ID：`2750d37298394335a0b485e2dbea44a1`。

预检会话 ID：`ab73e0cba6144f58beab7244b54b9632`。

主会话十个检查点的样本数为：`36 → 68 → 100 → 132 → 164 → 196 → 227 → 260 → 291 → 323`；正常停止后文件为 324 条。

三段会话均逐条验证遥测 JSON 可解析、时间戳严格递增；最终均 `cleanShutdown=true`、`neverStarted=false`、`recorderError=null`，版本为 `0.9.1.0`，且 `elevated=false`。

### 界面与异常日志

- 新版启动以来 `startup.log` 未新增 `[ERROR]`；观察期未出现未处理 UI 异常或窗口无响应。
- Dashboard 显示 CPU 为 Ryzen 7 9700X；CPU 温度及频率正确显示 **N/A**，不是伪装成正常的 0 值。
- 主 GPU 显示为 **AMD Radeon RX 9070 XT**，不是集显；观察到约 49–50 W、2.8 GiB 等实时读数。
- Dashboard RAM 为约 12.2–12.3 GiB；Home 稳定刷新后显示 `RAM Used: 12.2 GiB`。切页瞬间的 `RAM: --` 是尚未收到下一次刷新，本次单独核实了稳定读数。
- Home 的两个历史事件已以 **Unclassified / 支持度 0** 只读呈现，历史证据文件未被重写。这不是对 09:07、09:16 实际故障类型的新确认。

## 4. 仍存在的采集缺口

`collector-health.json` 已在新会话中实际落盘，录制时界面也明确显示对应状态：

| 采集器 | 实测状态 | 解释与边界 |
|---|---|---|
| ETW GPU | Started | 仅表示会话启动；匹配事件覆盖未验证。新测试的 `etw-gpu.jsonl` 仍为空，不能宣称 ETW GPU 采集完整。 |
| ETW System | Error | 启用 `Microsoft-Windows-Kernel-Power` 时出现 `UnauthorizedAccessException`，`0x80070005 E_ACCESSDENIED`。本轮按授权保持普通权限，不自行提权。 |
| Sensors | Degraded | CPU 部分读数为零或无效，界面标为不可用；原始遥测保留。未解决底层权限或驱动支持问题。 |
| Storage | NoData | 没有磁盘指标；文件存在或有记录不代表实际返回了有效磁盘数据。 |

主会话另有 GPU engines 161 条、GPU memory 64 条、memory pressure 64 条、process snapshots 10 条等记录，说明不是只有界面数字变化。Kernel Power / WHEA ETW 文件也为空，不能据此排除硬件、电源或系统故障。

本次不包含管理员采集、驱动调整、长时间稳定性、压力测试、断电或真实死机复现；没有证明显存耗尽，也没有证明所有冻结问题已修复。

## 5. 自动化探针说明

开始阶段出现两处探针自身问题，均未误报为产品缺陷：一次命令内 `$PSScriptRoot` 为空，发生在启动应用前；一次使用 PowerShell 只读变量 `$HOME`，预检在正常停止录制后退出。已修正探针并保留失败记录，之后重新完整执行正式五分钟验收，命令退出码为 0。

## 6. 审计材料

本报告仓库位置：`CrashScope/docs/mcp-upgrade-acceptance-20260918.md`。

工作区探针材料（相对于 `D:\workspace\workspace`）：

- `.mcp-probes/takeover/old-exit.json`：旧版正常退出确认。
- `.mcp-probes/takeover/backup.json`：备份及发布文件复核。
- `.mcp-probes/takeover/new-initial.json`：新版初始窗口、版本和权限。
- `.mcp-probes/takeover/validation-result.json`：正式录制、检查点、收尾状态及健康记录。
- `.mcp-probes/takeover/live-health.json`：录制期间的采集健康快照。
- `.mcp-probes/takeover/steady-home.json`：Home 稳定刷新读数。
- `.mcp-probes/takeover/new-startup-log.txt`：仅本次启动后的日志增量。
- `.mcp-probes/takeover/final-audit.json`：最终备份哈希、原始证据、全部测试遥测及进程状态。
- `.mcp-probes/takeover/validation-attempt1-result.json`、`probe-attempt1-session.json`：预检失败原因及正常停止确认。

先前独立打包报告：`CrashScope/docs/mcp-test-package-20260918.md`；它与本轮实机 GUI 验收是两个不同阶段，不互相替代。
