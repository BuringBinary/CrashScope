# CrashScope 桌面悬浮信息条（T1）

日期：2026-09-18（UTC+8）。工具箱 T1 第三批交付。

## 结论

已实现常驻桌面的 Topmost 悬浮信息条：CPU / GPU（负载+温度）/ RAM 来自录制传感器流，网络上下行速度来自 OS 计数器（独立于录制，始终实时）。301/301 全量测试通过（新增 9 项），门禁筛选集 261/261，Desktop Release 构建成功，发布脚本 AST 校验 0 错误。未实机 GUI 验收，并入下一次测试包验收。

## 变更

### Core

- `src/CrashScope.Core/Models/FloatingBarView.cs`（新增）：`NextBytesPerSecond`（首样本/非正间隔返回 null，计数器回退返回 0 而非负速度）；`FormatSpeed`（B/s→KB/s→MB/s→GB/s 自适应，1024 基）；`Build`（单行文本，缺失组显示 "--"，GPU 负载与温度独立出现，RAM 总量=已用+可用）。无 WPF 依赖，纯函数可测。

### Desktop

- `src/CrashScope.Desktop/FloatingBarWindow.xaml(.cs)`（新增）：560×40 无边框半透明深色条（OSD 风格，固定配色，不随主题），Topmost、不占任务栏、不抢焦点。拖动（DragMove）+ 记忆位置（VirtualScreen 范围内 clamp，默认右下角）；关闭按钮；单实例（`ShowSingle` 重复调用激活已有实例）。1 秒 DispatcherTimer 采样网卡 IPv4 字节数并计算速率（逐网卡容错，跳过 Loopback）；`SensorUpdated` 事件驱动硬件段刷新，传感器值 3 秒未更新按 "--" 呈现（停止录制不伪装实时，网速不受影响）。传感器索引复用 `DesktopSensorMap`（主 GPU 选择规则与 Dashboard 一致），目录变化时重建映射。
- `src/CrashScope.Desktop/Services/UiSettingsService.cs`：新增 `floatingBarLeft/Top` 两个键，沿用既有读-改-写合并模式（与主题/语言/托盘设置互不覆盖），损坏回退默认。
- `src/CrashScope.Desktop/Pages/HardwareOverviewPage.xaml(.cs)`：页头新增 "Floating Bar" 按钮（与 Raw Sensor 窗口按钮并列）。

### 门禁

`scripts/publish-test-package.ps1`：回归类集合加入 `FloatingBarViewTests`（24 类），最低通过数 252 → 261（实测 261/261）。

## 验证

| 项 | 结果 |
|---|---|
| 全量测试（Release） | 301/301 通过，0 失败 0 跳过 |
| 门禁筛选集（含新类 24 类） | 261/261 通过 |
| Desktop Release 构建 | 成功 |
| publish-test-package.ps1 AST 解析 | 0 错误 |

## 边界

- 网速为所有非环回 IPv4 适配器汇总，与任务管理器口径可能不完全一致（它含全部协议族）；单位标注为 B/s 系（1024 基），不是电信运营商的 bit/s。
- 悬浮条固定深色配色，浅色主题下同样可读；未做主题联动。
- 硬件段数据来自录制会话的传感器白名单（9 类）；CPU 温度等本机不可用项不影响负载体现。
- 位置记忆写入 `%LOCALAPPDATA%\CrashScope\app_settings.json`（与其它 UI 设置同文件合并）。
- 无防遮挡/点击穿透（ClickThrough）能力；如需"鼠标穿透"模式另行规划（需 Win32 分层窗口扩展）。

## 审计材料

探针材料（相对工作区根）：`.mcp-probes/feat-sensor/` 下 tests.log、gate.log、desktop-build.log、gate4.txt、ast-count.txt。
