# CrashScope 网速测试页（T1）

日期：2026-09-18（UTC+8）。工具箱 T1 第四批交付。

## 结论

已实现只读网速测试页：延迟（4 个公共 DNS × 4 次，含丢包与抖动）+ 下载/上传吞吐（Cloudflare 公共端点）。313/313 全量测试通过（新增 12 项），门禁筛选集 273/273，Desktop Release 构建成功，发布脚本 AST 校验 0 错误。未实机 GUI 验收，并入下一次测试包验收。

## 变更

### Core

- `src/CrashScope.Core/Models/SpeedTestView.cs`（新增）：`Average`/`Jitter`（相邻样本平均绝对差）；`ToMbps`（十进制 1 Mbps = 1,000,000 bit/s，与运营商口径一致）；`FormatLatency`/`FormatMbps`（缺失/非有限/负值一律 "--"）；`Verdict`（仅按实测下载分五档容量说明，措辞为"可支撑 X"，不做提速承诺）。

### Desktop

- `src/CrashScope.Desktop/Pages/SpeedTestPage.xaml(.cs)`（新增）：Ping（阿里/腾讯/Cloudflare/Google DNS，直连与代理环境都能得到有效目标）+ Download（50 MB 流式读取，20s 预算，部分传输也给出实测值）+ Upload（8 MB POST，20s 预算）。全程可取消（Unloaded 自动取消）；按钮用 try/finally 复位（UI 审计 Finding 3 的教训）。
- `src/CrashScope.Desktop/MainWindow.xaml(.cs)`：侧栏 Speed Test 导航 + 路由。

### 诚实口径

- 上传未在预算内完成时**不报告数值**（HttpClient 缓冲上传无法得知已发送字节数，外推会虚高），只提示"未完成"。
- 下载部分传输按实际字节/实际时长计算，并如实标注 MB 数与秒数。
- 页面与报告均标注端点与"结果受代理/防火墙影响"；本机有 Clash 代理，直连 Cloudflare 的表现即为本机真实路径。

### 门禁

`scripts/publish-test-package.ps1`：回归类集合加入 `SpeedTestViewTests`（25 类），最低通过数 261 → 273（实测 273/273）。

## 验证

| 项 | 结果 |
|---|---|
| 全量测试（Release） | 313/313 通过，0 失败 0 跳过 |
| 门禁筛选集（含新类 25 类） | 273/273 通过 |
| Desktop Release 构建 | 成功 |
| publish-test-package.ps1 AST 解析 | 0 错误 |

## 边界

- 网页测速（浏览器到就近 CDN）与本页（直连固定端点）数值可能不同；页面如实显示所用端点与字节数/时长，用户可自行比对。
- 上传依赖 Cloudflare `__up` 端点可用性；不可用时如实报错，不做端点回退（避免不同端点结果不可比）。
- Ping 的 RoundtripTime 为整毫秒（OS API 粒度），抖动精度受此限制。
- 本页不测量延迟抖动的长期分布，也不做 ping 持续监控；如需持续网速监控可在悬浮条观察实时吞吐。

## 审计材料

探针材料（相对工作区根）：`.mcp-probes/feat-sensor/` 下 tests.log、gate.log、gate6.txt、ast-count.txt。
