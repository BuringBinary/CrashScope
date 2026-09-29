# 待审改动提交前复验与提交记录（2026-09-29）

## 结论

9/28 的两轮修复（0.9.11 验收问题 A01–A05，以及未录制空态、图标和文案）在提交前重新构建并回归，全部通过，已分两个本地提交入库，**未推送**。本轮没有修改源码，没有启动 CrashScope，没有录制，也没有出包。

## 提交前核对

- 分支 `feature/local-desktop-work`，起点 HEAD `f026a3d`，领先 `origin/feature/local-desktop-work` 15 个提交。
- `git diff` 与深度阅读时的快照 `.mcp-probes/deep-read-20260929/uncommitted.diff` 逐字节一致；未跟踪文件只比当时多了 `docs/mcp-deep-reading-20260929.md`；3 个新增代码文件的 sha256 与审阅时一致。
- 没有 CrashScope 进程在运行，9/28 验收时打开的 0.9.11 桌面进程已经退出。
- Git 配置为 `core.autocrlf=true`，仓库没有 `.gitattributes`；索引里都是 LF，工作树里 LF 和 CRLF 混用。

## 复验结果

| 项目 | 命令（在 `CrashScope/` 下执行） | 结果 |
| --- | --- | --- |
| Desktop 构建 | `dotnet build src/CrashScope.Desktop -c Release` | 成功，0 警告，0 错误 |
| 测试程序集构建 | `dotnet build test/CrashScope.Core.Tests -c Release` | 成功；1 条既有警告：`RpcTests.cs(151)` 的 CS1998 |
| UI 冒烟 | `SMOKE_OUT=.mcp-probes/commit-20260929/ui-smoke bash scripts/test-ui-smoke.sh` | 1205 项通过，覆盖中英文 × 深浅色，共 59 张截图 |
| Core 测试 | `dotnet test test/CrashScope.Core.Tests -c Release` | 498 通过，0 失败，0 跳过（9/28 为 497，本批新增 1 条） |

- dotnet 使用 `~/.dotnet/dotnet.exe`（SDK 8.0.425）。
- 证据目录：`.mcp-probes/commit-20260929/`（已被 Git 忽略）。

## 提交

按 `docs/mcp-commit-plan-20260928.md` 的做法处理行尾：已跟踪文件用 `git add --renormalize` 暂存，新增文件直接暂存。暂存后逐个检查，索引中没有 CRLF。

| 提交 | 标题 | 内容 |
| --- | --- | --- |
| `25622b5` | `desktop: 修复 0.9.11 验收问题 A01-A05 与未录制空态/图标/文案` | 20 个修改文件（含 `.gitignore`），加上新增的 `src/CrashScope.Desktop/MonitoringPresentation.cs`、`test/CrashScope.Ui.Smoke/AgentLayoutChecks.cs`、`scripts/test-package-readme.zh-CN.txt`；共 23 个文件，+489/−78 |
| 本文所在提交 | `docs: 0.9.11 验收、两轮修复、深度阅读与提交复验记录(20260928-20260929)` | 9/28 的三份记录、`docs/mcp-deep-reading-20260929.md` 和本文 |

- `25622b5` 同时解决了深度阅读中的 N9：出包 README 模板已经入库，此后可以从干净检出出包。
- 两个提交都没有推送。提交后本地领先远端 17 个提交，是否推送由用户决定。

## 未覆盖

- 这些修复还没有打包，也没有在新进程的真实窗口中复验；0.9.11 包（`artifacts/test-packages/20260928-162159/`）保持原样。
- 深度阅读中的 N1–N8 和 N10–N12 尚未处理。按建议顺序，下一步先修隐私问题 N1–N3。
