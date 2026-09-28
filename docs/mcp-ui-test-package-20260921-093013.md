# UI 首轮独立测试包

日期：2026-09-21。版本：0.9.5-test.20260921093013。

## 产物与身份

- 目录：`artifacts/test-packages/20260921-093013/`
- 启动器：`artifacts/test-packages/20260921-093013/Toolbox/Start-CrashScope.cmd`
- ZIP：`artifacts/test-packages/20260921-093013/CrashScope.Toolbox.0.9.5-test.20260921093013.win-x64.zip`
- Windows x64 自包含，含 Desktop、Agent、Service；无需另装 .NET 运行时。没有安装服务。
- 包来自包含未提交 UI 改动的工作树，不是正式签名发行版。
- 基线 HEAD：`71c0603703462b9a43af5070461da064df71f0cb`。
- InformationalVersion：`0.9.5-test.20260921093013+worktree.9f07614eb38c`。
- 源码指纹：`9F07614EB38C9BE4F113ABF56D1D32953928C2E31F3A5199A73500F3A3E0D878`。
- ZIP 大小：158019852 bytes（约 150.70 MiB）。
- ZIP SHA-256：`341ADFE569C8065B45B4890E43EC07094DD2CE5E1F7117DF8087453F50EE3E82`。

## 打包与验证

执行 `scripts/publish-test-package.ps1 -BaseVersion 0.9.5`，生成新的时间戳目录，没有覆盖旧包、启动 Desktop 或停止已有进程。

本轮为 UI 包补齐发布检查：OverviewMetricTextTests 纳入筛选；最低测试数由历史 282 调整为 311；检查 CommonResources.xaml/ControlStyles.xaml；源码快照包含 .sh 测试入口。总数门槛不是逐类必需用例门禁，后续仍可改进。

验证结果见 `artifacts/test-packages/20260921-093013/verification-result.json`：

- status=verified。
- 311 项选定回归通过。
- 976 个 ZIP 成员逐文件 SHA-256 与发布目录一致。
- 54 个受保护旧产物/历史事故文件哈希未变；保护范围由脚本明确列举，不等于所有本机文件。
- 发布前后源码快照一致。
- 三个宿主均检查 x64 PE、自包含运行时、必需文件和构建身份。
- 已执行发布版 Agent --help 安全入口。
- desktopGuiSmokeExecuted=false：没有启动本包 Desktop；此前的 303 项源 XAML 隔离检查和 26 张截图不等于发布包真实 GUI 验收。
- 回归日志包含既有 RpcTests.cs:151 CS1998 警告；PowerShell 原生输出转码使部分中文日志出现乱码，测试数由 TRX XML 校验，不从乱码文本推断。

实施背景及 UI 验证边界见 `docs/mcp-ui-first-pass-20260921.md`。

## 用户查看步骤

1. 若旧版正在录制，先自行保存并 Stop，等待停止，从托盘正常退出；不要强杀进程。若 Agent/Service 正运行也需正常停止对应宿主。
2. 建议备份 `%LOCALAPPDATA%\CrashScope`。新包仍使用该目录，不是数据沙箱；不要同时运行新旧录制器。
3. 本机已有解压目录，直接运行 `Toolbox/Start-CrashScope.cmd` 即可；启动器检测到已有 CrashScope 宿主会拒绝启动，不会自动杀进程。
4. 如需拷贝到其他目录，用 ZIP 全量解压到新目录，不要与旧版合并。
5. 先看首页/导航/滚动，再切换深浅主题和中英文；首页“会话与运行环境”可查看 App 版本，精确构建身份以 build-info.json 为准。
6. 这次看 UI 不必安装 Service、不必提权、不必开启录制。需要验证真实指标时再自行启动录制。

未自动改写用户配置、未重启应用、未安装服务或任务；尚未进行发布包真实交互与跨屏 DPI 验收。
