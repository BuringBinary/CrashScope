# CrashScope Raw Sensor Window 与 Hardware Overview 深化

日期：2026-09-18（UTC+8）。工具箱 T1（硬件监控完整化）首批交付。

## 结论

已实现独立原始传感器窗口与硬件总览页深化，并顺带修复总览页内嵌传感器列表的过滤下标错位缺陷。285/285 全量测试通过（新增 8 项），门禁筛选集 245/245，Desktop Release 构建成功，MCP 结构化诊断 0 项，`git diff --check` 干净，发布脚本 AST 校验 0 错误。尚未实机 GUI 验收，将并入下一次测试包验收。

## 变更

### Core

- `src/CrashScope.Core/Models/RawSensorView.cs`（新增）：共享纯展示逻辑。`Build` 按 `entry.Id` 索引数值数组（绝不按过滤后位置），支持搜索（HardwareType/HardwareName/SensorName/SensorType，大小写不敏感）与精确 HardwareType 过滤；`FormatValue` 按 HardwareMonitor 白名单内的 SensorType 给出仅展示层单位提示（°C/%/W/MHz/V/RPM/GB/MB），未知类型保持无单位；null/NaN/Infinity 一律呈现 "--"，`HasValue` 语义为"有可显示值"。

### Desktop

- `src/CrashScope.Desktop/RawSensorWindow.xaml(.cs)`（新增）：独立原始传感器窗口。标准窗口 chrome（全应用第二个 Window，刻意不使用 WindowChrome，规避标题栏命中测试一类缺陷）；订阅 `AppService.SensorUpdated/StatusChanged` 并投递 UI 线程；按 HardwareName 分组的 ListView；搜索 + 类型下拉过滤；Topmost 开关；Refresh catalog；滚动位置跨刷新保持；状态语义区分 Live / Stopped（显示最后收到值）/ 空目录（提示开始录制）。只读，不修改任何系统状态。
- `src/CrashScope.Desktop/Pages/HardwareOverviewPage.xaml(.cs)`：
  - 头部与内嵌 Raw Sensor Catalog 卡片各加一个打开窗口的入口按钮。
  - 内嵌列表改用 `RawSensorView`，列改为 Sensor/Value/Type/Hardware。
  - 新增 WMI 硬件标识（后台查询、静态缓存、逐查询 try/catch 与 5 秒超时）：Win32_Processor（名称/核线程/标频）、Win32_VideoController（多 GPU 名称/驱动版本/日期/状态）、Win32_PhysicalMemory（每条容量/频率/厂商/部件号 + 汇总）、Win32_BaseBoard + Win32_BIOS + Win32_ComputerSystem。标识行与传感器摘要合并显示；WMI 失败时保留传感器摘要，不打断。
  - Disk（DriveInfo）/Display（Screen）/Network（NIC）原有逻辑保留。

### 修复的缺陷

过滤下标错位：原 `RefreshSensors` 在过滤后用"过滤列表下标"索引"全量数值数组"，导致搜索状态下列表显示的是其它传感器的数值。现统一按 `entry.Id` 取值，回归测试 `Values_AreIndexedById_NotByFilteredPosition` 覆盖。

### 门禁

`scripts/publish-test-package.ps1`：回归类集合加入 `RawSensorViewTests`，最低通过数 237 → 245。实测门禁筛选集（22 类）245/245，与阈值一致。发布脚本本轮未执行，不会生成新包。

## 验证

| 项 | 结果 |
|---|---|
| 全量测试（Release） | 285/285 通过，0 失败 0 跳过 |
| 门禁筛选集（含新类 22 类） | 245/245 通过 |
| Desktop Release 构建 | 成功 |
| get_diagnostics | 0 项 |
| git diff --check | exit 0（仅既有 LF/CRLF 提示） |
| publish-test-package.ps1 AST 解析 | 0 错误 |

## 边界

- 未实机运行 GUI；窗口布局、主题（暗/亮）下的可读性与 Topmost 行为需下一次测试包实机验收确认。
- 单位提示是展示层约定，不改变遥测原值，也不冒充其它工具的口径。
- 传感器目录来自录制会话（HardwareMonitor 允许的 9 类传感器；主板/存储/网络的 LHM 硬件未启用），窗口只呈现目录中存在的项；总览页的 Disk/Network 为 OS 级信息，与传感器目录分列。
- WMI 标识 best-effort：普通权限下已验证可查询（与采集链路轮的只读探针同机制），个别查询失败不影响页面其余部分。
- `AllowMultipleWindow`：重复点击会打开多个传感器窗口，各自正确订阅/退订；如需单实例可在后续轮次收敛。

## 审计材料

仓库报告：`docs/mcp-raw-sensor-window-20260918.md`。探针材料（相对工作区根）：`.mcp-probes/feat-sensor/` 下 desktop-build.log、tests.log、gate.log、diff-check.log、gate-patch.txt、ast-count.txt、run.sh、run-gate.sh、ast-check.ps1。
