CrashScope 实机修复测试包（Windows x64，自包含）
版本：{{VERSION}}
构建：{{INFO_VERSION}}
此包由源码工作树构建，并非正式签名发行版。是否包含未提交修改，以 build-info.json 的 dirtyWorkingTree 字段为准。

开始前：
1. 在旧版点击 Stop，等待停止；从托盘菜单完整退出，不要强杀进程。
2. 如有 Agent/Service，请正常停止对应录制宿主；不要同时运行多个录制入口。
3. 备份整个 %LOCALAPPDATA%\CrashScope。不要删除历史异常。
4. 将 ZIP 解压到独立目录，勿与旧版合并；双击 Start-CrashScope.cmd。
   启动器发现已有 CrashScope 宿主时会拒绝启动，不会关闭任何旧进程。

新版仍使用 %LOCALAPPDATA%\CrashScope，并不是独立的数据沙箱。
程序集版本：{{ASSEMBLY_VERSION}}。精确构建身份见 build-info.json 或用户数据目录下 logs/startup.log。

5-10 分钟验收：
- 未录制时应明确提示没有实时数据；经确认开始录制后，检查样本是否持续增加。
- 多次切换 Home / Dashboard，然后 Stop / Start，检查无图表异常。
- 缺失/可疑读数显示 N/A；将鼠标放在 Collection 状态上查看原因。
- ETW 按 provider 显示接收/筛选/写入速率、handoff drop、OS lost 及权限预检；DxgKrnl 默认 Disabled，Started 不代表事件覆盖完整。
- 会话目录应持续产生 collector-overhead.jsonl、cpu-fallback.jsonl；pagefile.jsonl 和 dwm-state.jsonl 启动即有 baseline。
- gpu-engines.jsonl 即使空闲也应有 Idle/Unavailable/Error heartbeat，不再用“文件为空”表达未知状态。
- 磁盘使用独立 Windows PhysicalDisk 数据源，记录物理索引与来源；没有温度/SMART 时保持不可用。
- 本地历史异常按新规则只读重评，不再把未验证的显存计数确诊为 HardFreeze。
- 原始 summary.md / analysis.json 不会被重写；包中不包含你的实机数据。

组件：
Desktop/：默认图形入口。
Agent/：可选命令行录制；使用 start-agent-passive.cmd，默认带 --no-actions。
Service/：仅提供宿主文件；本次没有安装服务或计划任务，请勿为本次验收自行安装。

本次自动验证：{{TEST_COUNT}} 项选定回归全部通过；发布文件/版本/自包含运行时检查；
Agent --help 安全入口检查；ZIP 每个文件 SHA-256 验证。
上述打包环节没有启动 Desktop；后续 GUI/实机验收结果以单独验收报告为准，未自动提权。
CPU 温度/部分功耗、磁盘温度/SMART、ETW 完整覆盖与 GPU 显存统计口径仍有验证边界。
停止新版并正常退出后，旧包仍可保留作回退入口；勿让新旧版同时录制。
