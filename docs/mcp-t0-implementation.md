# T0 工具箱底座实现记录

日期：2026-09-17
阶段：T0 - 底座加固与打包基线

## 目标
- 补全发布脚本，Desktop+Agent+Service 自包含 win-x64，ZIP 结构
- 首页重构聚合硬件/录制/最近故障
- 硬件总览页基础只读实现
- 设置页完善开机启动/托盘/数据目录/恢复队列可见性
- 验证 Release 构建与测试

## 实现细节

### 1. 发布脚本 `scripts/publish.ps1`
- 原脚本只发布 Agent 到 `dist/win-x64`，无错误检查
- 新脚本：
  - 解析 dotnet：优先 PATH，否则 `%USERPROFILE%\.dotnet\dotnet.exe`
  - `Resolve-Dotnet` + `dotnet --version` 失败即抛异常
  - `Publish-Project` 函数：清理旧输出，`dotnet publish -c Release -r win-x64 --self-contained true -o <out> /p:DebugType=None /p:DebugSymbols=false`，检查 LASTEXITCODE 和输出存在
  - 发布三个项目到 `dist/agent-win-x64`, `service-win-x64`, `desktop-win-x64`
  - 组合到 `dist/toolbox-win-x64/{Agent,Service,Desktop}`，生成 `README.txt` 说明被动采集、单写者锁、升级前需停旧版
  - 生成三个 ZIP：`CrashScope.Toolbox.win-x64.zip` (151MB), `CrashScope.Agent.win-x64.zip` (39MB), `CrashScope.Desktop.win-x64.zip` (74MB)
- 验证：执行 `powershell -ExecutionPolicy Bypass -File ./scripts/publish.ps1` 成功，0 错误，产物存在

### 2. 首页 `Pages/HomePage.xaml/.cs`
- 新增 `HomePage` UserControl，注册导航 `Home`，设为默认启动页 `NavigateTo("Home")`
- 布局：
  - 顶部标题：CrashScope Toolbox + 副标题 PC 玩家工具箱·硬件监控·故障黑匣子·本地优先
  - 左：Recorder 卡片 - 显示 RecorderState, Session dir, SampleCount, RecorderError, RecoveryError，StatusDot 颜色区分 Recording/Faulted/Stopped，Start/Stop 按钮调用 AppService.StartRecording/StopRecording，检查上次异常按钮调用 CheckPreviousUncleanSession
  - 右：Hardware Summary 卡片 - 显示 SensorCatalog 数量，CPU/GPU 型号从 HardwareName+SensorName，RAM Used 从 SensorValues，DataRoot, Elevation, .NET 版本
  - 中：Quick Actions - WrapPanel 按钮 Tag 导航到 Dashboard/LiveMonitor/Hardware/Incidents/Timeline/Settings，事件 `NavigateRequested` 冒泡到 MainWindow
  - 底部：Recent Incidents - 调用 `AppService.GetIncidents()` 取最近 5 条，ListView 显示 DirectoryName, Mode, Timestamp，空时显示提示
- 实现注意：
  - 订阅 `AppService.StatusChanged` 和 `SensorUpdated`，Dispatcher.InvokeAsync 刷新
  - `BuildSensorMap` 使用 `SensorCatalogEntry.HardwareType/SensorName` 而非旧的 Name 属性（实际模型为 Id/HardwareType/HardwareName/SensorType/SensorName/Identifier）
  - 避免 WPF/WinForms 命名冲突：使用 `WpfButton`, `WpfMessageBox` 别名
  - 导航：MainWindow.CreateHomePage() 创建并订阅 NavigateRequested，更新对应 RadioButton IsChecked

### 3. 硬件总览页 `Pages/HardwareOverviewPage.xaml/.cs`
- 新增 `HardwareOverviewPage`，导航 Tag `Hardware`，侧边栏新增 NavHardware
- 布局 2x3 Grid：
  - CPU/GPU/Memory/Disk/Mainboard/Display+Network 卡片
  - 底部 Raw Sensor Catalog - 搜索框过滤，ListView GridView 显示 Name/Type/Value
- 只读数据来源：
  - CPU/GPU/Memory：从 `SensorCatalog` 过滤 HardwareType，显示 HardwareName - SensorName
  - Disk：`DriveInfo.GetDrives()` 只读
  - Mainboard：Environment.MachineName/OSVersion/ProcessorCount 等安全信息，不做 WMI 深度查询
  - Display：`System.Windows.Forms.Screen.AllScreens` 需 WinForms，捕获异常
  - Network：`NetworkInterface.GetAllNetworkInterfaces()` 取前 5 个非 Loopback
  - 传感器值：订阅 `SensorUpdated`，实时刷新 ValueText
- 边界：不修改系统，不申请额外权限，失败时显示错误文本

### 4. 主窗口导航 `MainWindow.xaml/.cs`
- `MainWindow.xaml` 新增两个 RadioButton：NavHome (Home), NavHardware (Hardware)，Style NavButton
- `MainWindow.xaml.cs`：
  - 默认 `NavigateTo("Home")`
  - `NavigateTo` switch 增加 Home/Hardware，使用 `CreateHomePage()` 工厂
  - `CreateHomePage` 订阅 NavigateRequested，更新 RadioButton 并导航
  - 修复 Hardware 映射之前为 null 的问题

### 5. 设置页 `SettingsPage.xaml/.cs`
- XAML 新增 `Startup & Tray` Border：
  - AutoStartCheckBox：开机自动启动（当前用户注册表 Run）
  - MinimizeToTrayCheckBox/CloseToTrayCheckBox：UI 占位，实际托盘逻辑已在 TrayIconManager
  - 打开数据目录/故障目录按钮
  - RecoveryPendingText：显示待恢复任务数量
- C#：
  - `OnLoaded` 扩展调用 `RefreshStartupState()` 和 `RefreshRecoveryPending()`
  - `RefreshStartupState` 读取注册表 `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run` 值 `CrashScope.Desktop`
  - `IsAutoStartEnabled` / `SetAutoStart`：读写注册表，exe 路径取当前进程 MainModule.FileName 或 BaseDirectory 下 CrashScope.Desktop.exe
  - `AutoStartCheckBox_Changed`：切换时写注册表，ShowStatus 提示，失败回滚
  - `OpenDataFolder_Click` / `OpenIncidentsFolder_Click`：`Process.Start` with UseShellExecute=true 打开目录
  - `RefreshRecoveryPending`：检查 `%LOCALAPPDATA%\CrashScope\state\recovery-pending\*.json` 数量，显示提示
  - 所有注册表/文件操作 try/catch，不影响主流程

## 测试与验证

### 构建
- `dotnet build Desktop -c Release --no-restore`：0 警告 0 错误
- `dotnet build Agent/Service -c Release --no-restore`：0 错误（输出被 PTY 截断但 exit 0）
- `dotnet build Core.Tests -c Release --no-restore`：1 个既有警告 CS1998 (RpcTests.cs:151 async 无 await)，0 错误

### 测试
- Release 全量：`dotnet test -c Release --no-build` => 216 通过，0 失败，0 跳过，6s
- 之前 Debug 选定集 176 通过，39 核心收敛测试复跑 2 轮通过（见 mcp-reliability-convergence.md）
- 新页面为纯 UI，不引入额外单元测试；手动验证通过构建和发布后 EXE 存在性

### 发布验证
- `scripts/publish.ps1` 执行 2 次均成功：
  - Toolbox ZIP 151MB，Agent 39MB，Desktop 74MB
  - 解压结构 `toolbox-win-x64/{Agent,Service,Desktop,README.txt}`
  - README 包含被动采集、单写者锁、升级说明

## 未决与需确认

1. **开机启动范围**：当前仅当前用户 Run，是否需要提供任务计划程序（Task Scheduler）高权限选项？推荐保持 Run，避免提权，Agent 若需高权限另行通过 Service 安装。
2. **托盘关闭行为**：CloseToTrayCheckBox 已加 UI，但实际关闭逻辑在 MainWindow.OnClosing 是否已尊重？需确认是否要接入设置持久化（目前仅 UI 默认勾选，未保存到配置文件）。
3. **硬件总览深度**：当前 Disk/Network/Display 为基础只读，是否需要接入 LibreHardwareMonitor 更详细的 SMART、主板型号、BIOS 版本？需 WMI，权限和性能需评估，建议 T1 再细化。
4. **首页硬件摘要**：当前显示传感器数量和名称，是否需要显示实时温度/负载数值？已订阅 SensorUpdated 可扩展，但首版保持简洁。
5. **FPS 悬浮**：T0 未实现，T1 需决定是否用 PresentMon 或游戏内叠加，避免反作弊敏感注入。

## 下一步 T1
- 完善硬件总览：多 GPU 详情、主板/BIOS、SMART 评估、网速测试独立进程、悬浮信息条 Topmost
- 首页聚合更多实时数据
- 设置持久化托盘选项到本地 JSON

所有修改未复制 NexBox 源码，仅参考功能形态，符合 clean-room 要求。
