# 测试包使用说明（0.9.2-test.20260921155748）

## 包在哪

```
D:\workspace\workspace\CrashScope\artifacts\test-packages\20260921-155748\
├── CrashScope.Toolbox.0.9.2-test.20260921155748.win-x64.zip   (151 MB)
├── CrashScope.Toolbox.....zip.sha256                          (校验值)
├── Toolbox\                                                   (已解开，可直接跑)
│   ├── Desktop\CrashScope.Desktop.exe   <-- 主程序，双击这个
│   ├── Agent\  Service\
│   ├── Start-CrashScope.cmd
│   ├── build-info.json                  (版本与来源指纹)
│   └── file-manifest.json
└── verification\                        (回归日志、trx、发布日志)
```

**要拷到别的机器**：发 zip，对方解压后跑 `Toolbox\Desktop\CrashScope.Desktop.exe`。
**就在本机测**：直接用已解开的 `Toolbox\` 目录，不用解压。

自包含 win-x64 打包，**目标机不需要装 .NET 运行时**。

## 怎么确认跑的是这一版

启动后 Settings → 关于，版本应为 `0.9.2-test.20260921155748`。
或直接看 `Toolbox\build-info.json` 的 `version` 字段。

这一点很重要：`src\...\bin\Release\` 下还有 **10:18** 的旧 exe，早于今天所有改动，
拿那个测等于没测。认准 `artifacts\test-packages\20260921-155748\` 这个路径。

## 已经自动验过的

| 项 | 结果 |
| --- | --- |
| 打包脚本自校验 | PACKAGE VERIFIED |
| 包内回归（Release） | 406/406 通过，0 失败 |
| 开发树全量回归 | 493/493 通过 |
| PE 架构校验 | x64 确认 |
| 启动冒烟 | 进程存活 25 秒未崩，日志走完 Localization → Theme → OnStartup → MainWindow shown |
| 包内 `themeSystem` 本地化键 | en / zh 均存在 |
| 包内 `OnAccentText` | 已编入 Desktop.dll |

注意 `build-info.json` 里 `desktopGuiSmokeExecuted: False` 是打包脚本自己的字段
（它不启动 GUI）。我在打包之后单独启动验证过，结论见上表。

## 需要你手动测的

完整清单在 `docs\mcp-manual-acceptance-checklist.md`。按重要性排：

### 1. 逐页冒烟（最重要，别跳）

侧边栏每一项都点开，深色浅色各一轮。**重点 AgentActions 页** —— 它之前因为
`BoolToVis` 未定义会直接抛 `XamlParseException` 崩掉，而构建和 493 个单元测试全是绿的。
这类问题只有真的点开才会暴露。

### 2. 跟随系统主题（本轮新功能）

- Settings 主题下拉应出现第三项「跟随系统 (浅色)」
  —— 本机 `AppsUseLightTheme=1`，所以括号里应该是「浅色」
- 选中它，界面立刻变浅色
- **保持应用开着**，去 Windows 设置 → 个性化 → 颜色，翻转「应用模式」
- 应用应**无需重启**即刻跟随，下拉括号内容同步变化
- 切回明确的「深色」，再翻转系统主题，应用**不应**再跟随
- 关掉应用重开，偏好应仍是「跟随系统」，而不是被存成固定值

### 3. 列表徽标跟随主题（本轮修复）

- **停留在 Incidents 页**切主题，严重度徽标应立即变色（不是切走再回来才变）
- Timeline 页同样
- 反复切 5 次以上，列表不应丢内容或异常闪烁

### 4. 状态提示可读性（本轮修复）

- 浅色主题下，Settings 点「保存设置」，绿色提示文字应清晰可读
  （修复前对比度 1.91，几乎看不见）

### 5. 徽标文字（注意，不是 bug）

深色主题徽标用**近黑字**，浅色主题用**白字**——两个主题故意相反。
因为深色主题的徽标底色是柔和色系（亮），浅色主题的是深饱和色。看着「不一致」是正确的。

### 6. 悬浮条（注意，不是 bug）

悬浮条在两个主题下**都保持深色半透明**，这是已定案的产品决策。
它浮在任意桌面内容上，深底浅字才能保证可读。

### 7. 其余

- 中英文切换，检查截断和布局撑破
- 高 DPI（150% / 200%）缩放
- Tab 走一圈，焦点框在两个主题下都可见

## 反馈格式

**页面 + 主题（深/浅）+ 语言 + 元素 + 现象**，有截图更好。
这样我能直接定位到具体的资源键或代码位置。

崩溃的话，把 `%LOCALAPPDATA%\CrashScope\` 下的 `crash.log` / `startup.log` 发我。
注意该目录里已有 **9/20 的历史 crash 记录**（`IconTextTemplate` 那条），
那是旧问题，不是本包产生的 —— 看时间戳区分。

