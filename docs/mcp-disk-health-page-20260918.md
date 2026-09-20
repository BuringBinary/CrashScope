# CrashScope 磁盘健康页（T1）

日期：2026-09-18（UTC+8）。工具箱 T1 第二批交付，紧接 `docs/mcp-raw-sensor-window-20260918.md`。

## 结论

已实现只读磁盘健康页：按物理磁盘展示温度 / SMART 健康 / % Disk Time / 读写延迟 / 队列深度 / 数据来源，实时数据来自录制会话的存储管道（`RecorderEngine.StorageUpdated` → `AppService` → 页面）。292/292 全量测试通过（新增 7 项），门禁筛选集 252/252，Desktop Release 构建成功，发布脚本 AST 校验 0 错误。未实机 GUI 验收，并入下一次测试包验收。

## 变更

### Core

- `src/CrashScope.Core/Models/DiskHealthView.cs`（新增）：纯展示逻辑。按 DiskIndex 排序（未知索引殿后、再按名称）；`--` 语义与传感器窗口一致（缺失/非有限值不显示，实测零值保留，如空闲队列深度 0）；单位仅展示层（°C/%/ms）；`HasAnyMetric` 对浮点做非有限检查（NaN/Infinity 不算有效指标，与展示一致）；`SummarizeCollector` 输出存储采集器三态文案。

### Desktop

- `src/CrashScope.Desktop/Pages/DiskHealthPage.xaml(.cs)`（新增）：状态行区分 Live / 录制中等待首样本（约 5 秒一拍）/ 已停止（显示最后收到样本）/ 无数据；显示存储采集器健康（来自 collector-health）；十列 GridView；底部 DriveInfo 卷容量卡（只读）。订阅 `StorageUpdated/StatusChanged`，Unloaded 退订。
- `src/CrashScope.Desktop/AppService.cs`：转发引擎的 `StorageUpdated` 事件（引擎侧 5 秒一拍，`Notify` 助手已隔离观察者异常）。
- `src/CrashScope.Desktop/MainWindow.xaml(.cs)`：侧栏新增 Disk Health 导航（HardDrive 图标）+ 路由。

### 门禁

`scripts/publish-test-package.ps1`：回归类集合加入 `DiskHealthViewTests`，最低通过数 245 → 252（实测门禁筛选集 23 类 252/252）。

## 验证

| 项 | 结果 |
|---|---|
| 全量测试（Release） | 292/292 通过，0 失败 0 跳过 |
| 门禁筛选集（含新类 23 类） | 252/252 通过 |
| Desktop Release 构建 | 成功 |
| git diff --check | exit 0 |
| publish-test-package.ps1 AST 解析 | 0 错误 |

## 边界

- 数据仅录制期间实时流动；停止录制时页面显示最后收到的样本并明确标注，不伪装实时。
- SMART/温度来自 LibreHardwareMonitor 存储传感器（当前 HardwareMonitor 配置 IsStorageEnabled=false，目录内通常无存储硬件传感器）——届时温度/SMART 显示 "--"，IO 指标（PhysicalDisk 计数器）仍实时可用；这正是采集链路轮修正后的真实口径，不做假数据。
- 页面只读，不写任何系统状态；WMI 标识不在本页（在 Hardware Overview）。

## 审计材料

探针材料（相对工作区根）：`.mcp-probes/feat-sensor/` 下 tests.log、gate.log、desktop-build.log、diff-check.log、gate2/gate3.txt、ast-count.txt。
