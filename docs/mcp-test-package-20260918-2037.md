# CrashScope 全功能测试包交付（含网速测试页）

日期：2026-09-18（UTC+8）。第三份独立测试包，取代 `docs/mcp-test-package-20260918-1952.md` 的 20260918-195240 包（该包打于网速测试页完成之前，保留不部署；两份包均为独立目录，互未触碰）。

## 产物

- 测试版：`0.9.2-test.20260918203700`（程序集 0.9.2.0）
- 构建身份：`0.9.2-test.20260918203700+worktree.bbae089fce9c`
- 目录：`artifacts/test-packages/20260918-203700/`
- ZIP：`CrashScope.Toolbox.0.9.2-test.20260918203700.win-x64.zip`（157,897,107 字节）
- SHA-256：`001946373731CD2D1033831071AAE146645B39B6129E5FFD355D896A73F34340`

## 内容（相对上一份 0.9.1-test 已部署包）

采集链路修复、原始传感器窗口 + 硬件总览 WMI 标识、磁盘健康页、桌面悬浮信息条、**网速测试页**（延迟/下载/上传，诚实口径）。门禁 200 → 273。

## 验证结果（脚本自动执行）

| 项 | 结果 |
|---|---|
| 门禁回归（25 类筛选集） | 273/273 通过 |
| 三宿主 Release self-contained 发布 | 成功 |
| ZIP 逐文件流式 SHA256 | 975/975 通过 |
| 受保护旧文件（54 个） | 哈希全部未变 |
| 构建期间源码指纹 | 一致 |
| 脚本结论 | `PACKAGE VERIFIED` |

## W3 实机验收清单

启动入口：`artifacts\test-packages\20260918-203700\Toolbox\Start-CrashScope.cmd`。
1. 0.9.1.0 Desktop：Stop → 托盘 Exit 正常退出。
2. 数据根备份到 `artifacts/data-backups/<时间戳>-before-0.9.2`（带逐文件哈希）。
3. 双击新版启动器，首页确认 `0.9.2.0`。
4. 5-10 分钟：切页（Home/Dashboard/Hardware/Disk Health/Speed Test）、Stop→Start、Raw Sensor 窗口、悬浮条、（可选）跑一次网速测试。
5. 核对 storage 按盘指标、ETW 三态、缺失项 N/A、页面状态语义、旧证据零改写；出验收报告。
