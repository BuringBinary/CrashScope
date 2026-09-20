# CrashScope 实机修复 + T1 功能测试包交付

日期：2026-09-18（UTC+8）。第二份独立测试包，覆盖采集链路修复与 T1 三批功能。

## 产物

- 测试版：`0.9.2-test.20260918195240`（程序集 0.9.2.0）
- 构建身份：`0.9.2-test.20260918195240+worktree.6b01d477b0d0`
- 平台：Windows x64，自包含 .NET 8；包含 Desktop、Agent、Service。
- 来源：当前含未提交功能改动的工作树（与上一份包同流程），非签名发行版。

独立输出目录：`artifacts/test-packages/20260918-195240/`
可复制 ZIP：`artifacts/test-packages/20260918-195240/CrashScope.Toolbox.0.9.2-test.20260918195240.win-x64.zip`（157,889,579 字节）
SHA-256：`E4DCC94206D588ADC8A705C89E0BEE68C0F19A7268477BAB6CDA99760D231362`

## 内容（相对上一份 0.9.1-test 包）

1. 采集链路修复（`docs/mcp-collection-chain-20260918.md`）：磁盘按物理盘归属、ETW 覆盖三态与生命周期、CPU 健康评估。
2. 原始传感器窗口 + 硬件总览 WMI 标识（`docs/mcp-raw-sensor-window-20260918.md`）。
3. 磁盘健康页（`docs/mcp-disk-health-page-20260918.md`）。
4. 桌面悬浮信息条（`docs/mcp-floating-bar-20260918.md`）。
5. 发布门禁 200 → 261（新增 RawSensorView/DiskHealthView/FloatingBarView 及采集链轮测试类）。

## 验证结果（脚本自动执行）

| 项 | 结果 |
|---|---|
| 门禁回归（24 类筛选集） | 261/261 通过，0 失败 0 跳过 |
| Desktop / Agent / Service 发布 | Release win-x64 self-contained 成功 |
| ZIP 逐文件流式 SHA256 | 975/975 通过，无缺失/重复 |
| 受保护旧包与历史证据（54 文件） | 哈希全部未变 |
| 构建前后源码指纹 | 一致（sourceUnchanged=true） |
| 脚本结论 | `PACKAGE VERIFIED` |

启动入口：`artifacts\test-packages\20260918-195240\Toolbox\Start-CrashScope.cmd`（检测到已有宿主会拒绝启动）。发布日志与 TRX 在包目录 `verification/`。

## W3 实机切换验收清单（下一步）

1. 当前 0.9.1.0 Desktop 若在录制先 Stop，托盘 Exit 正常退出（不强杀）。
2. 备份整个 `%LOCALAPPDATA%\CrashScope` 到 `artifacts/data-backups/<时间戳>-before-0.9.2`。
3. 双击新包 `Start-CrashScope.cmd`，首页确认 `0.9.2.0`。
4. 录制 5-10 分钟：多次 Home/Dashboard/Hardware/Disk Health 切换、Stop→Start、打开 Raw Sensor 窗口与悬浮条。
5. 核对：storage.jsonl 按盘指标；collector-health ETW 三态；缺失传感器 N/A；Disk Health 页状态语义；旧 evidence 零改写。
6. 输出验收报告并提交推送。
