# CrashScope 阶段 0：基线保护与改动清单

日期：2026-09-27。状态：**已完成基线核对和经用户授权的独立备份；未修改应用源码或原始事故资料。** 后续代码修复、构建/测试、实机启动仍需分别确认。

## 1. 当前工作树与 0.9.10 身份

- 仓库：`D:\workspace\workspace\CrashScope`，分支 `feature/local-desktop-work`；HEAD `71c0603703462b9a43af5070461da064df71f0cb`，相对 `origin/feature/local-desktop-work` 本地领先 10 个提交。未推送或提交。
- 备份前 Git 状态：**28 个未暂存修改、2 个未暂存删除、45 个未跟踪条目**，没有已暂存更改。两项删除为 `RecorderStatusPage.xaml{,.cs}`。其中未跟踪的目录展开成文件后有 **47 个文件**。
- 最新测试包 `artifacts/test-packages/20260921-183401/Toolbox/` 是 `0.9.10-test.20260921183401`，包记录为 dirty worktree、所选自动测试 408 通过、真实桌面 GUI smoke **未执行**。此次没有重建或复测。
- 与该包的 `verification/source-snapshot.json` 逐文件比较：当前 `src/test/scripts` **190/190 个文件 SHA-256 一致**。因此审计中这些当前源码缺陷可对应此测试包的源码快照；这仍不等于在真实 GUI 中复现过。

## 2. 原始资料与自动清理边界

- `%LOCALAPPDATA%\CrashScope\sessions`：10 个会话，约 **184.55 MiB**；其中 9 月 18 日游戏会话 18 个文件、**3,027,184 B**，9 月 20 日长测 19 个文件、**186,005,401 B**。
- `%LOCALAPPDATA%\CrashScope\incidents`：2 个恢复 incident，46 个文件、约 **1.14 MiB**。与 0.9.10 出包时保存的 protected snapshot 比对，**46/46 个文件哈希未变**。
- `RetentionService` 默认 30 天、总量 512 MiB，并将大小限额按会话/incident 各半分配（会话约 **256 MiB**）。会话现已约 184.55 MiB；再录一次长测可能使旧会话进入清理范围。执行 GUI 或保存保留设置前应先核对已保护的副本。未发现运行中的 CrashScope 进程；本轮未启动它。

## 3. 独立备份及校验

按用户选择，备份存于 **仓库与应用数据目录之外**：

`D:\CrashScope-Backups\20260927-stage0-160433-dca92057\`

内容：

| 文件或目录 | 用途 |
| --- | --- |
| `code-repository-current.zip` | 当前非忽略的已跟踪现存文件 208 个、未跟踪文件 47 个，共 255 个；不含生成包、`.git` 和被删除的文件。 |
| `tracked-diff-from-HEAD.patch`、`git-status-porcelain.z` | 记录相对 HEAD 的源码修改和删除状态，辅助从现存仓库恢复；**不要直接把补丁应用到当前工作树**。 |
| `package-source-snapshot.json` | 出包时的源码逐文件校验清单。 |
| `evidence/game-20260918/`、`evidence/soak-20260920/` | 经授权复制的游戏会话 18 个文件和长测会话 19 个文件。 |
| `evidence/incidents/` | 两条原有 incident，共 46 个文件。 |
| `manifest.json`、`manifest.sha256`、`README.txt` | 每个备份文件的来源、字节数及 SHA-256，使用和恢复提示。此目录含私人诊断数据，不应上传公开仓库。 |

共 **83 个证据文件、181.41 MiB**。`manifest.json` SHA-256：`3760c49a289d7dfb81b6116312526fd03e0aeb0fab4b4ceae0fe9adbc23e7e2d`；代码 ZIP SHA-256：`9a8c91691fc84a7dc7c81ebb1e19cefcbe8a0b3aa4f0772d5850904da5a237f1`。

复制过程中逐文件校验**源文件前后哈希与副本哈希**；另开独立核验读取 ZIP 全部 255 个文件、83 个证据副本及清单哈希，均通过。备份前后 Git 状态完全相同；没有残留 `.partial` 目录。备份只防止误操作及应用保留清理，**不是异地灾备**，也不意味着自动恢复已经演练。

## 4. 建议的后续变更边界（未执行）

1. **先处理实时监控 P0**：`LiveMonitorPage` 过滤后空列表访问和六项事件订阅缺少退订，搭配 `MainWindow` 反复新建页面。改动先限定在该页面和回归测试；不混进界面重构。
2. **随后处理诊断口径**：最后心跳≠精确故障时间；事件读取失败应可见。另列游戏内报错正常结束后的人工时间标记，不能先假设存在事故条目。
3. **再收设置/导出和整体 GUI 验收**：脱敏默认值、另存文件名、无数据/错误状态。实机启动和录制须再次授权，用隔离或已备份的数据根。
4. 现有大量未提交工作横跨导航、主题、本地化、页面、测试与出包脚本，**不能仅按目录盲拆提交**。先基于本备份逐批审查和构建，再由用户决定是否提交及提交边界；本轮没有 `git add/commit/reset/clean`。
