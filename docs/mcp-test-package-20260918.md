# CrashScope 实机修复测试包交付

日期：2026-09-18（UTC+08:00）

## 产物

- 测试版：`0.9.1-test.20260918102913`
- 程序集版本：`0.9.1.0`
- 构建身份：`0.9.1-test.20260918102913+worktree.7618a83baead`
- 平台：Windows x64，自包含 .NET 8；包含 Desktop、Agent、Service。
- 来源：当前含未提交修复的工作树，不是正式签名发行版。

独立输出目录（仓库相对路径）：

`artifacts/test-packages/20260918-102913/`

实机目录：

```text
D:\workspace\workspace\CrashScope\artifacts\test-packages\20260918-102913
```

### 本机启动入口

文件已展开，不必再次解压。正常停止并退出旧版、备份数据后，双击：

```text
D:\workspace\workspace\CrashScope\artifacts\test-packages\20260918-102913\Toolbox\Start-CrashScope.cmd
```

启动器检测到已有 Desktop/Agent/Service 宿主时会拒绝启动，不会关闭旧进程。校验结束时旧版 Desktop PID 64720 仍在 `dist/toolbox-win-x64/Desktop/` 运行。启动器脚本经过语法和目标路径检查，但本轮未实际启动新版 Desktop。

### 可复制到其他机器的 ZIP

`artifacts/test-packages/20260918-102913/CrashScope.Toolbox.0.9.1-test.20260918102913.win-x64.zip`

- 大小：157,850,501 字节，约 158 MB / 150.5 MiB。
- SHA-256：

```text
9AD87E0211E2A8BC2A577075D65DDF97837E03D695B70D5563AE652924E8DFAA
```

同目录附有 `.zip.sha256` 和 `verification-result.json`。包内附 `README.txt`、`build-info.json`、`file-manifest.json`，不包含用户实机数据。

## 发布方式

新增：

- `scripts/publish-test-package.ps1`
- `scripts/test-package-readme.zh-CN.txt`

新脚本只向新的 `artifacts/test-packages/<BuildId>/` 写入，遇到已有 BuildId 立即拒绝。不会清理旧目录、结束应用、安装服务或修改开机启动。已用相同 BuildId 实测重复发布被拒绝，现有 ZIP 哈希与 verified 状态不变。

没有运行原有 `scripts/publish.ps1` 或 `scripts/smoke-test.ps1`：前者会强杀旧进程并清空 dist，后者也会强杀 Desktop，不符合本次保留旧版的约定。

构建过程使用 PowerShell 进程级 `RemoteSigned` 执行本地新脚本，没有修改持久执行策略。若以后需要重新打测试包，在仓库根运行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\publish-test-package.ps1
```

默认生成新的时间戳目录，不覆盖本次包。

## 验证结果

1. 打包前重新运行选定回归：200 通过、0 失败、0 跳过，以 TRX 计数为准。测试工程仍有既有 `RpcTests.cs:151` CS1998 警告。
2. Desktop、Agent、Service 均成功执行 Release / win-x64 / self-contained 发布。
3. 三个 EXE 的 MZ/PE 结构及 x64 架构验证通过；程序程序集和 Core 的版本、ProductVersion 与本次构建身份一致。
4. 自包含运行时文件、runtimeconfig、依赖清单、WPF/图表/硬件监控依赖、语言资源和主题文件检查通过。
5. 发布后的 Agent 原生 EXE 执行 `--help` 成功。该分支在创建数据目录和 RecorderEngine 前返回，没有开始采集。
6. ZIP 的 975 个文件逐项解压流 SHA-256 核验通过，无缺失或重复条目。
7. 构建前后源文件指纹一致；旧 ZIP、旧包关键程序文件及两份历史证据包共 54 个受保护文件的哈希一致。
8. 额外使用实际发布到 Desktop 目录的 `CrashScope.Core.dll` 做只读回放，先核对程序集构建身份再分析两个实机包：二者均从旧版 HardFreeze 排名变为 Unclassified；每包 23 个原始文件 SHA-256 全部不变。
9. 启动器中 PowerShell 命令语法检查通过。旧版进程仍在运行，未启动新 Desktop 验证图形窗口或录制。

发布日志、TRX、源码指纹与保护文件快照在 `artifacts/test-packages/20260918-102913/verification/`。额外只读回放与启动器检查记录在 ShunCode 工作区根的 `.mcp-probes/`，不放入 docs。

## 你接下来要做的事

1. 旧版若正在录制，点 Stop 并等待停止，然后从托盘菜单完整退出；不要直接强杀。
2. 若另有 Agent/Service 正在录制，同样正常停止；不要启动多个录制宿主。
3. 复制备份整个 `%LOCALAPPDATA%\CrashScope`。本次没有替你创建数据备份。
4. 双击上方新版 `Start-CrashScope.cmd`。不要再点旧 dist 中的 EXE。
5. 在首页确认 `App: 0.9.1.0`，精确构建见 build-info.json 或启动日志。个别页面原有硬编码版本文案不能作为构建身份。
6. 普通用户权限下录制 5–10 分钟；多次切换 Home/Dashboard，再 Stop/Start，检查样本增长、无图表异常、缺失读数 N/A 和 Collection 说明。
7. 检查本地两条旧异常是否显示原因未确定；保存的旧 summary.md 不会被改写。
8. 完成后告知测试情况，再读取新 collector-health.json 与日志排查采集缺口。

注意：程序文件是独立目录，但新版仍使用 `%LOCALAPPDATA%\CrashScope`，不是独立的数据沙箱。启动 Desktop 的正常恢复/保留策略可能处理其数据根，因此先备份。尚未执行 GUI、长期录制、管理员对照或真实故障实验，不把打包成功当成实机验收通过。
