# CrashScope 采集链路首轮修复与验证

日期：2026-09-18，UTC+8；最终保护性复核时间 11:32:32。

## 结论与当前状态

**已完成源码修复、37 项新增专项测试、237 项相关回归，以及普通权限下的磁盘只读实机验证。尚未发布或切换包含这些改动的新测试包，未验证新代码的实时 ETW 事件链路。**

当前运行的仍是已验收的 `0.9.1.0` 独立包，PID `67624`，窗口可见，录制状态 `STOPPED`。本轮未提权、未修改驱动或系统配置、未安装服务、未启动应用录制。源码及发布脚本有变更，没有覆盖旧包或提交/重置工作树。

## 一、只读诊断发现

### 磁盘：确认了应用漏采，不是系统完全无法提供数据

- Windows 普通权限可通过 `Win32_DiskDrive` 枚举物理磁盘 0，型号 `INTEL SSDPEKNW512G8`。
- PhysicalDisk 实例名为 `0 C: D:`；四个性能计数器均可读取。
- 旧 `StorageTracker` 最终只遍历 LibreHardwareMonitor 返回的磁盘。如果该列表为空，即使 Windows 性能计数器已有数据，也输出 `disks=[]`。
- 旧代码在无法归属物理磁盘时，还会把跨盘汇总值复制到个别磁盘；这会混淆指标归属。

初次诊断探针曾用简写实例 `0 C:`，与实际实例不匹配；随后按枚举出的名称重新读取，四项均成功。这不是权限错误，正式结果见 `disk-counters.json`。

### ETW：现有状态不足以解释空文件

- 本机 provider 元数据可只读查询；DxgKrnl、Kernel-Power、WHEA-Logger 的 GUID 与源码一致。
- 既有录制的 Kernel-Power 启用错误为 `E_ACCESSDENIED`；本轮没有重新启用 provider 或尝试提权。
- 旧代码没有区分收到事件、通过筛选、成功写入；provider 错误聚合后可能被其他错误覆盖。
- 旧停止流程会把已写过的缓冲事件再写一遍；引擎同时使用直接回调和队列通知，存在重复通知路径。
- 系统 ETW 队列的排空还依赖 GPU ETW 正在运行，两个采集链路不应如此绑定。

### CPU：明确缺口，但不能断言具体权限或驱动根因

对已停止会话 `2750d37298394335a0b485e2dbea44a1` 的既有目录和末条样本进行只读评估，未重新打开硬件采集器：

- Load：18/18 通过现有读数质量规则。
- Temperature：0/1 可用。
- Clock：9/18 通过规则，属于部分可用。
- Power：8/9 通过规则，属于部分可用。

这些是按现有传感器质量规则的统计，并不表示缺失指标已恢复，也不能仅凭零值、空值推断是权限拒绝。温度、CPU 平均频率、Package 功耗等可疑读数继续显示 N/A，原始数据不变。

## 二、已实施改动

### 1. 磁盘采集与指标归属

- Windows PhysicalDisk 性能计数器和磁盘目录不再依赖 LHM 成功枚举磁盘。
- 使用枚举得到的实际实例名，预读验证计数器，按物理磁盘索引归属数据。
- 不将 LogicalDisk 卷实例当作物理磁盘；不再向单盘复制全机汇总值。
- 无法确认归属的硬件传感器保持独立；只有唯一型号匹配或明确 Disk 索引才合并。
- 增加可选的 `diskIndex`、`performanceSource`、`sensorSource` 字段，保留旧 JSON 可读性。
- 盘目录不等于有效指标；空值、无穷值和负的性能计数值不会使状态变成 Collecting。空闲时的有效零值保留。
- Windows I/O 指标不冒充温度或 SMART 健康。数据源错误进入健康说明，不静默消失。

### 2. ETW 覆盖状态与生命周期

- 每个 provider 独立记录：启用状态、处理线程状态、停止状态、received/matched/written 计数、最后接收/写入时间及错误。
- 在筛选之前统计接收事件；系统 provider 按 GUID 匹配，不依赖名称解析。
- PermissionDenied 与一般 Error 区分；优先保留明确的权限拒绝原因。
- Started 只代表启用，Receiving 代表有输入但未必有持久化目标事件，Observed 代表确实写入过目标事件。都不等于“全部事件覆盖完整”。
- 有界内存队列只用于交接；写入失败不会增加 written，停止不会重复落盘。
- 取消引擎的双重通知路径；系统 ETW 排空不再依赖 GPU ETW 存活。
- 不再尝试销毁同名既有 ETW 会话；使用新的唯一会话名。
- 处理任务持有稳定的 session 引用，停止时等待任务退出后再关闭写入器；最终健康快照保留计数和停止状态。
- `BufferDroppedEvents` 是内存交接队列丢弃量，不是 OS ETW 丢失事件数；后者仍未测量，说明中明确标注。

### 3. CPU 可用性说明

增加单独 CPU 健康评估，按 Load / Temperature / Clock / Power 展示 NotExposed、Unavailable、Partial 或 Available。按传感器 ID 读取，缺项不假装健康，也不自动归因于驱动或权限。

### 4. 发布门禁

独立打包脚本已加入 `CollectionChainTests`，相关回归最低通过数由 200 提升至 237；脚本 AST 校验通过。本轮没有执行发布脚本。

## 三、验证结果

| 验证 | 结果 |
|---|---|
| 新增采集专项测试 | 37/37 通过 |
| 既有 200 项 + 新增 37 项相关回归 | **237/237 通过，0 失败、0 跳过** |
| Core / Desktop Release | 测试构建成功 |
| Agent / Service Release | 构建成功，各 0 警告、0 错误；没有启动宿主 |
| 普通权限磁盘只读探针 | 成功，未打开 LHM 硬件接口，未启动 ETW 或应用录制 |
| 数据根文件 | **191 个文件全部原 SHA256 不变，文件数不变** |
| 当前 Desktop 发布文件 | **501 个文件与原包清单一致** |
| 修改文件校验 | 11 个源码/测试文件 + 1 个发布脚本，远端原始字节 SHA256 已核对 |

测试构建仍有既有 `RpcTests.cs:151` 的 CS1998 警告，未将其说成全项目零警告。

11:27:56–11:27:57 的只读磁盘探针取得两条新样本：

| 指标 | 样本 1 | 样本 2 |
|---|---:|---:|
| DiskTimePercent | 0.7652% | 0.8892% |
| 读延迟 | 0.29044 ms | 0 ms |
| 写延迟 | 0.39950 ms | 0.51413 ms |
| 队列深度 | 0 | 0 |

温度与 SMART 健康均为 null。这证明修正后的 Windows 磁盘分支可在本机普通权限下取得数据，不等于整套新版 GUI 已完成录制验收。DiskTimePercent 保留 Windows `% Disk Time` 的语义，不宣称与其他工具的“活动时间”指标完全等价。

## 四、变更位置

- `src/CrashScope.Core/Monitoring/StorageTracker.cs`
- `src/CrashScope.Core/Monitoring/StorageSampleBuilder.cs`
- `src/CrashScope.Core/Models/Records.cs`
- `src/CrashScope.Core/Monitoring/EtwCoverageTracker.cs`
- `src/CrashScope.Core/Monitoring/EtwEventSink.cs`
- `src/CrashScope.Core/Monitoring/EtwDxgKrnlMonitor.cs`
- `src/CrashScope.Core/Monitoring/EtwRealTimeMonitor.cs`
- `src/CrashScope.Core/Monitoring/CollectorHealthTracker.cs`
- `src/CrashScope.Core/Monitoring/CpuSensorAssessment.cs`
- `src/CrashScope.Core/Monitoring/RecorderEngine.cs`
- `test/CrashScope.Core.Tests/CollectionChainTests.cs`
- `scripts/publish-test-package.ps1`

## 五、未完成边界与下一步

1. **尚未打包部署本轮改动。** 当前窗口显示的仍是旧的 0.9.1.0 包，不包含本轮修复；源码构建也不作为新的发布版本交付。
2. **ETW 新代码尚未做实时端到端验收。** 单元测试验证计数、写入失败、权限分类、队列和旧格式兼容，不代替真实 provider 启用、事件到达和启停验证。
3. CPU 温度、部分频率/功耗、SMART 温度和健康仍有缺口；本轮没有绕过权限或安装驱动来获取它们。
4. 尚未补全 OS ETW 丢失量、复杂停止异常场景和长时间压力/稳定性实验。
5. GPU 进程显存的 adapter/LUID 统计维度仍待独立修正；本轮没有恢复显存耗尽因果评分。

建议接着生成一个新的独立测试包，在明确短时录制范围和最终停止状态后，进行新 GUI + 真实 ETW + 磁盘落盘的 5–10 分钟验收。确有需要时再另行授权管理员对照，不自动改变当前权限或录制状态。

## 六、审计材料

仓库报告：`docs/mcp-collection-chain-20260918.md`。

探针材料相对于工作区根 `D:\workspace\workspace` 位于 `.mcp-probes/collection-chain/`：

- `baseline.json`、`final-verification.json`：数据保护与最终状态。
- `read-only-diagnostics.json`、`disk-counters.json`：OS 数据源与 provider 元数据。
- `read-only-probe/`、`live-read-only-result.json`、`read-only-probe.log`：隔离、非提权的磁盘新采样与 CPU 历史样本评估。
- `chain-new-tests.trx`、`chain-regression.trx`、`new-tests.log`、`regression.log`：测试结果。
- `agent-build.log`、`service-build.log`：仅构建的宿主验证。
- `final-source-manifest.json`：12 个修改文件的远端原始字节 SHA256，保留原文件换行格式。

安全基线参见 `docs/mcp-upgrade-acceptance-20260918.md`；阶段计划参见 `docs/mcp-next-steps-20260918.md`。
