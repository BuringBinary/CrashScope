# 体验提升批次 A：事件可读化与直达

日期：2026-09-21
状态：实现、回归与隔离 WPF 检查完成；发布包验证身份见文末交付补充。尚未完成真实数据的完整窗口路由/键鼠验收。
基线：`feature/local-desktop-work` / `71c0603` 加前轮未提交工作树。没有 Git 提交，没有覆盖旧包。
本轮依据 `docs/mcp-product-experience-plan-20260921.md` 的批次 A，不宣称 B–E 已实施。

## 1. 实现范围

### 统一事件展示

新增 `src/CrashScope.Desktop/IncidentPresentation.cs`，纯只读展示映射，首页与事件页共用：

- 标题为“异常会话记录”，不根据目录名推断崩溃原因。
- 已知候选分类翻译为中英文；Unclassified 显示未分类，未知枚举显示未知分类。
- 时间按应用语言和本地时区展示，明确标为最后记录；详情解释其来源为最后一次心跳，不保证等于故障发生时间。
- 保留原始记录 ID、分类和含时区时间戳；在技术详情中可选中复制，另提供复制 ID 按钮。
- 证据支持度明确不是故障概率。零值、非有限值和越界值不显示成“0% 风险”。
- 页面概览的“主要原因”改为“常见候选分类”，不把分类计数包装成根因证明。

### 首页直达与返回

修改 `src/CrashScope.Desktop/Pages/HomePage.xaml`、`.xaml.cs` 和 `src/CrashScope.Desktop/MainWindow.xaml.cs`：

- 最近事件行改为原生 WPF Button，鼠标点击及按钮获得焦点后的 Enter/Space 均使用原生激活机制；保留明确的查看详情文字。
- 发出稳定记录 ID，而非列表下标；路由在已有记录集合中精确匹配，不将字符串当文件路径读取。
- ID 不存在或不唯一时，不回退打开第一条，提示记录不可用或无法读取。
- MainWindow 的首页导航复用一个 HomePage 实例，离开前保存滚动偏移；返回时刷新现有数据，尝试恢复被打开事件的按钮焦点，再恢复偏移。
- 通过显式导航更新保护，避免首页操作同时触发 RadioButton 选择和重复构造目的页面。
- 事件页从首页具体记录进入时显示返回概览；从普通导航进入时不显示该上下文返回按钮。
- 当前首页没有筛选器，因此本轮不宣称实现不存在的筛选条件保存。事件页语言刷新保留已选记录；没有新增全局页面历史栈。

### 事件详情和动作

修改 `src/CrashScope.Desktop/Pages/IncidentsPage.xaml`、`.xaml.cs`：

- 列表与详情使用同一展示语义；长分类/证据支持文本可换行。
- 选择边框使用主题强调色，不再依赖系统默认白色选中背景。
- 详情增加时间说明与折叠技术信息，不重写原始分析文件。
- 未选中、选中多条或导出执行中，不启用导出；恰好选中一条才可导出。
- 两条记录仍可比较；清除选择同步清除旧详情，避免导出已不再选中的对象。
- 导出开始时捕获选中记录，结束时根据最新选择恢复按钮；导出逻辑本身、脱敏机制和写入范围未重写。
- 保存对话框标题和过滤器本地化；导出按钮补齐图标/文本分离模板。
- 新增只读数据提供器、语言提供器和翻译函数构造入口，便于在不创建 AppService/采集器的情况下测试真实编译页面。

### 相关资源

- 新增 `eventUi` 中英文键，保留原始枚举以供技术追溯。
- `src/CrashScope.Desktop/Themes/ControlStyles.xaml` 的 SecondaryButton 支持 HorizontalContentAlignment，普通按钮维持居中，事件按钮可铺满可点击区域。
- `scripts/publish-test-package.ps1` 纳入 IncidentPresentationTests；选定回归最低总数改为 341，额外检查至少 30 个该测试类用例通过，避免只依赖总数。
- `scripts/test-ui-smoke.sh` 输出改至本轮独立目录 `.mcp-probes/experience-a-20260921/`。

## 2. 验证

### 自动回归

`test/CrashScope.Core.Tests/IncidentPresentationTests.cs` 新增 30 个用例：

- 标题不从 recovered/故障字样的目录名推断原因；保留原始对象与 ID。
- 7 种已有分类翻译；null、空值与未来分类回退。
- 明确时区下的最后心跳时间格式；英文应用语言日期格式。
- 非有限、负值、零和超范围支持度；正常支持度带非概率说明。
- 按 ID 定位第二条而非第一条；缺失、空、路径式文本不回退；重复 ID 不擅自选一条。
- 无选择/单选/多选/忙碌时导出状态。

原发布筛选集 + OverviewMetricTextTests + IncidentPresentationTests：**341/341 通过，0 失败、0 跳过**。既有 `RpcTests.cs:151` CS1998 警告仍存在，本轮未修改。

### WPF 检查

- Release 构建：0 warning、0 error。
- 最终 smoke：**321 项总检查**，其中包含 **16 项编译后事件页交互检查**；不能将 321 与 16 再相加。
- **26 张源 XAML 隔离截图 + 1 张编译后事件页合成截图**。截图数据是明确的 fixture，不是用户实机事故数据。
- 16 项交互检查覆盖指定第二条选中、单选导出、技术 ID、未知分类、时间标签、多选比较、取消选择清空详情、语言切换保留选择、编译后返回按钮事件、已删选择不再导出、缺失目标不选第一条、空列表禁用导出。
- 事件页测试使用真实编译的 IncidentsPage 和事件绑定，注入合成只读记录；首页及主窗口截图仍使用移除业务绑定的源 XAML，不将其冒充完整产品交互测试。
- 1.25/1.50 位图渲染比例不等于实际跨显示器 DPI 验收。
- 检查了默认中文深色首页（合成事件）与编译事件页截图；不声称人工逐张审查全部组合。
- 编辑器 error/warning 查询返回 0；`git --no-pager diff --check` exit 0。该 Git 检查不覆盖未跟踪文件。

### 证据

- `.mcp-probes/experience-a-20260921/regression.log`
- `.mcp-probes/experience-a-20260921/regression.trx`
- `.mcp-probes/experience-a-20260921/ui-smoke-build.log`
- `.mcp-probes/experience-a-20260921/ui-smoke.log`
- `.mcp-probes/experience-a-20260921/diff-check.log`
- `.mcp-probes/experience-a-20260921/screenshots/`
- `test/CrashScope.Ui.Smoke/IncidentInteractionChecks.cs`

复跑：Windows Git Bash 在仓库根目录执行 `bash scripts/test-ui-smoke.sh`；可通过 DOTNET 环境变量指定 .NET 8 SDK。

## 3. 保护边界与已知剩余工作

- 没有启停真实录制、重启当前应用、安装服务、上传数据或改写已有事故文件。
- 没有修改 AppService.GetIncidents 的读取/跳过行为。损坏文件被跳过和同步重评成本属于批次 B，尚未解决。
- 没有全面重写跨事件分析/对比面板的英文、布局和固定色；本轮聚焦主列表、详情摘要与入口。
- 没有新建采样器、常驻动画或额外数据上传。
- 源码和纯展示测试验证了 ID 选择规则，编译 WPF 测试验证了事件页绑定；MainWindow → HomePage → IncidentsPage → 返回的完整路由和实际鼠标/键盘尚需在新包中实测。
- 首页缩减留白、固定设置和高级分组属于批次 C，不在本轮扩大范围。
- 未测试真实导出保存对话框和真实诊断包写入；原诊断导出回归仍在选定测试集中。

## 4. 用户验收建议

1. 在原版自行正常停止录制并从托盘退出，备份 `%LOCALAPPDATA%\CrashScope` 后启动新独立包；新旧包不可同时录制。
2. 首页点击第二条事件，核对技术详情 ID 是否为该条，不是第一条。
3. 返回概览，核对滚动位置；用 Tab 与 Enter 再打开记录。
4. 切换中英文，观察分类和最后记录时间；原始 ID/分类/时间仍可展开查看。
5. 在事件页取消选择、选择一条、Ctrl 选择两条，检查导出和比较按钮状态。
6. 确认“未分类/未知分类/证据不足”没有变成确定的根因判断。

发布前第一次补丁因共享模板上下文不唯一被原子拒绝，没有写入任何文件；重新读取并补足上下文后成功。对照运行的旧版 303 项 smoke 不计入本轮最终 321 项结果。

## 5. 本轮交付

- 版本：`0.9.6-test.20260921094604`。
- 构建身份：`0.9.6-test.20260921094604+worktree.7cc50e02c10f`。
- 发布目录：`artifacts/test-packages/20260921-094604/Toolbox/`。
- 启动器：`artifacts/test-packages/20260921-094604/Toolbox/Start-CrashScope.cmd`。
- ZIP：`artifacts/test-packages/20260921-094604/CrashScope.Toolbox.0.9.6-test.20260921094604.win-x64.zip`。
- ZIP 大小：158025877 bytes。
- SHA-256：`50B9BA86825E1F3D8881ADC1DAB5DCF226DF2EE21490F0D7DDF176C48E81EA27`。
- 发布校验：status=verified；341 项打包回归通过；976 个文件逐成员哈希一致；54 个受保护文件未变；源码快照前后一致。
- 发布过程没有启动 Desktop，desktopGuiSmokeExecuted=false。321 项 WPF 检查是构建前的隔离检查，不伪称最终包完整 GUI 验收。
- 新包仍使用 `%LOCALAPPDATA%\CrashScope`，不是数据沙箱。启动器拒绝在其他 CrashScope 宿主运行时启动，不会自动停止旧实例。
- 上一版 0.9.5 包保留，未覆盖；未自动安装服务、启动产品或更改用户配置。

原始打包验证记录：`artifacts/test-packages/20260921-094604/verification-result.json`；本轮文档未加入 ZIP，保存在仓库 docs 下。
