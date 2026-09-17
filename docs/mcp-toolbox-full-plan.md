# CrashScope 工具箱完整版规划（WPF 路线）

日期：2026-09-17
决策：用户已选择 **继续扩展 CrashScope（WPF / C#）**，参考 NexBox 功能形态但不复用其 GPL 代码。CrashScope 黑匣子作为工具箱的一个常驻模块。

> 依据：`docs/mcp-reliability-convergence.md` 已完成 R1-R5 收敛，176 选定回归通过，39 核心测试复跑 2 轮通过。

## 一、收敛是否没问题？

**可以进入下一阶段，但不是“零风险”。**

已修复：
- 上次会话恢复持久化队列 + 重试，不依赖当前录制器初始化
- 数据根单写者锁 state/recorder.lock
- 每次 Start 独立 SessionId/目录，首样本即心跳，先落盘后发布内存
- 录制状态 Starting/Recording/Stopping/Stopped/Faulted，异常转 Faulted
- 同步 Stop 最多等 3s，超时不强行释放，Stopping 立即通知
- 清理保护当前/待恢复会话，允许超配额，坏状态 fail-closed

仍需注意：
- 未做断电、长期硬件压力、原生永久挂死验证
- 恢复队列 UI 未暴露，损坏任务需手工清理指引
- 旧版不遵守新锁，升级必须先停旧版
- ETW 启动失败仅 Trace，Service 事件日志不可见
- 分析评分仍是旧口径，不能当确诊

结论：源码层面收敛完成，测试层面选定集通过；现场长期验证待工具箱首版打包后一起做。

## 二、现有 WPF 能力盘点

当前导航：Recorder / Dashboard / LiveMonitor / AgentMonitor / AgentIncidents / AgentActions / Incidents / Timeline / Settings

已具备：
- 常驻录制、Dashboard 趋势、Live 传感器、托盘、主题/语言框架
- Incident 证据包、时间线、分析摘要（带录制器故障警告）
- Agent RPC：状态查询、电源计划、进程优先级、操作审计、回滚
- Retention、导出脱敏、SensorCatalog、各类事件

缺口：
- 无统一硬件总览（CPU/GPU/RAM/Disk/MB/Display/Network 详情）
- 无 FPS 采集与游戏内悬浮层
- 无启动项管理、存储清理、着色器缓存、内存清理、DNS/测速 UI
- 无显示滤镜、准星叠加、悬浮信息条
- 无游戏启动器、Steam 库、工具检测启动、音频均衡器等
- 安装器仅发布 Agent

## 三、参考功能全量对照（55 页）

硬件监控：HardwarePage, SensorMonitorPage, OverlayPanelPage, VerticalOverlayPage, SpeedTestPage, DiskHealthPage
- 现状部分有，缺多 GPU 详情、主板/BIOS、SMART 评估、FPS、网络延迟、迷你趋势、独立原始传感器窗口

系统优化：MemoryCleanupPage, MemoryLimitPage, StartupManagerPage, PowerManagementPage, StorageCleanPage, ShaderCachePage, NetworkOptimizerPage, PeripheralOptimizePage, SystemOptimizerPage, WindowsUpdatePage, NvidiaDriverPage, RuntimeRepairPage, VtxVirtualizationPage, CpuSchedulerPage, GameProcessOptimizePage, AntiCheatOptimizePage
- 现状仅电源/进程，50+ 注册表批量、驱动下载、虚拟化、反作弊需高风险评审

显示增强：DisplayFilterPage, CrosshairPage - 缺

游戏辅助：SteamPage, DLSSPresetPage, ResolutionConverterPage, GpuRenamePage, AutoClickerPage, AudioEqPage, DeltaForcePage 等 - 缺，内存读取类默认不做

影音娱乐：MusicPage, DesktopLyricsPage 等 - 缺，第三方音乐 API 列为可选

工具集成：BuiltinToolsPage, ToolsPage - 缺检测/启动管理器

通用：HomePage, SettingsPage, TrayMenuPage 等 - 部分有

## 四、WPF 完整工具箱实施顺序

### T0 - 底座加固与打包基线（1-2 天）
- 补发布脚本：Release self-contained win-x64，Desktop+Agent+Service 独立输出，ZIP 结构
- CI：构建 5 项目 + Core.Tests
- 首页重构：聚合硬件摘要、录制状态、最近 incident、快捷入口
- 设置页：数据根、保留策略、托盘、开机启动（仅 Run）

交付：dist/toolbox-win-x64/ 可运行测试包，默认 --no-actions

### T1 - 硬件监控完整化（W2-W4）
- 硬件总览页、原始传感器窗口、磁盘健康只读、网速测试独立进程、悬浮信息条 Topmost、FPS 调研首版只桌面叠加

### T2 - 系统优化安全子集（W5-W7）
- 内存清理、启动项管理、存储清理预览、着色器清理预览、电源导入恢复、DNS 预设切换
- 高风险暂缓：50+ 注册表批量、驱动自动安装、虚拟化/Defender/更新禁用

### T3 - 显示增强（W6-W8 可重叠）
- 显示滤镜、准星叠加、叠加面板

### T4 - 游戏辅助与工具集成（W8-W10）
- 游戏启动器、Steam 库只读、工具检测启动、音频均衡器、自动点击器等独立页
- 默认排除：游戏内存读取、非官方音乐 API、反作弊自动调优

### T5 - 安装与更新（W11-W12）
- MSIX/Innosetup 研究但独立实现，卸载清理选项，更新检查 GitHub Release 手动安装

### T6 - 影音娱乐（可选）
- 本地文件播放，不内置第三方版权 API

## 五、边界与合规
- Clean-room：不复制 NexBox 源码/字符串/资源，GPL 污染会导致整个产品需开源
- 不写“零开销”“提升 FPS”，改为实测数据
- 所有变更需 reason、审计、回滚，默认被动采集
- 叠加/热键/进程操作标注风险，默认不自动优化游戏进程
- 纯本地，不上传，导出默认脱敏

## 六、打包门槛
内部测试包：T0 完成，Release 0 警告，176 测试通过，发布后 EXE 验证
目标机试用包：T1+T0，24-72h 常驻观察
对外试用版：T1+T2安全子集+T3基础，安装/卸载/权限/升级指引完成
正式版：需 2-3 真实 incident、跨 incident 验证、审计回滚全覆盖

## 七、下一步
1. 补发布脚本与 ZIP
2. 重做首页与硬件总览页
3. 按 T1 实现，黑匣子常驻
