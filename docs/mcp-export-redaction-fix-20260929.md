# 导出脱敏修复（N1–N3，2026-09-29）

## 结论

修复深度阅读发现的 N1–N3，以及同类的 JSON 转义问题和“不脱敏导出仍被改写”的问题。改动限于 `src/CrashScope.Desktop/ExportRedactor.cs`、`src/CrashScope.Desktop/DiagnosticExportService.cs` 和两份测试，已提交为 `5388daf`；本文档单独提交。

## 修复前复现（原实现，本地 .NET SDK 8.0.425 实测）

| 输入 | 原实现输出 |
| --- | --- |
| JSON：`{"sessionDirectory":"C:\\Users\\bob\\AppData\\Local\\CrashScope"}` | 原样保留（N1） |
| JSON：`C:\\Users\\\u5F20\u4E09\\Documents`（中文用户名被转义） | 原样保留（N1） |
| `User: <机器名>\bob` | `User: [HOSTNAME]\bob`（N3） |
| `fe80::1c2d:3e4f:5a6b:7c8d%12`、`::1` | 原样保留（N3） |
| JSON：`\u0027toodesk.exe\u0027\r\ntoodesk` | 两处进程名都没替换 |
| 不脱敏导出（`ExportRedactor.Passthrough`） | 仍替换了用户目录和 IPv4 |
| summary 的待确认问题 | 进程名和用户目录原样写出（新端到端测试在原实现下失败，N2） |

## 修改

### `ExportRedactor`
1. 用户目录：同时识别 `C:\Users\<名>`、JSON 转义的 `C:\\Users\\<名>`（含 `\uXXXX` 形式的非 ASCII 用户名）和 `C:/Users/<名>`；替换时保留原分隔符，JSON 仍可解析。
2. 账户：`<机器名>\<账户>`、`<域>\<账户>` 整体替换为 `[HOSTNAME]\[USERNAME]` / `[DOMAIN]\[USERNAME]`。JSON 文本里只认转义后的双反斜杠，单个反斜杠（`\r`、`\u4E0A`）不会被当成分隔符。
3. 当前 Windows 用户名、与机器名不同的域名：按词替换为 `[USERNAME]`、`[DOMAIN]`，同时匹配 JSON 转义写法。
4. IPv6：完整 8 段或含 `::` 的写法（可带 `%区域`）；`16:21:41` 这类时间不受影响。
5. 词边界：完整的 JSON 转义（`\u0027`、`\n`）视为边界；边界只按 ASCII 字母数字判断，中文前后紧贴的名称和地址也能匹配。
6. 已插入的占位符不再被后续规则改写（进程名为 `hostname` 时不会把 `[HOSTNAME]` 变成 `[[PROCESS_1]]`）。
7. 非 ASCII 进程名：从 JSON 采集时先反转义，原文写法和 JSON 写法共用一个 `[PROCESS_n]`。
8. `Passthrough` 真正不做任何替换（用户取消脱敏时导出原始内容）。
9. 新增 `ExportIdentity`（机器名、用户名、域名），默认取当前环境，测试可注入固定值。

### `DiagnosticExportService`
- `.json` / `.jsonl` 按 JSON 规则脱敏，其余文本按普通文本。
- summary 的“待确认问题”和 GPU 传感器转变说明也经过脱敏（N2）。
- summary 隐私说明与 `redaction-rules.txt` 同步新增的规则，并写明识别不了的情况。

### 测试
- `DiagnosticExportRedactionTests` 新增 12 个用例；`DiagnosticExportServiceTests` 新增 1 个端到端用例（脱敏导出后检查 ZIP 里的 summary 和 JSON）。

## 验证

- 本地（Linux，.NET SDK 8.0.425，引用仓库 `CrashScope.Core` 源码，`IncidentInfo` 用同形桩）：两份测试共 22 项通过；另用随机片段做了 6 万次 JSON 模式脱敏，结果全部是合法 JSON；4 MiB JSONL 耗时约 0.3 秒。
- 主机（Windows，.NET SDK 8.0.425，证据在被忽略的 `.mcp-probes/redaction-fix-20260929/`）：
  - `dotnet build src/CrashScope.Desktop -c Release`：0 警告，0 错误。
  - `dotnet build test/CrashScope.Core.Tests -c Release`：0 错误；仅既有警告 `RpcTests.cs(151,23)` CS1998。
  - 定向 `FullyQualifiedName~DiagnosticExport`：22/22 通过。
  - 全量 Core 测试：511/511 通过（基线 498 + 新增 13），`core-tests/core-tests.trx`。
  - UI 冒烟 `scripts/test-ui-smoke.sh`：1205 项通过。
  - get_diagnostics（`src/CrashScope.Desktop`、`test/CrashScope.Core.Tests`）：0 错误，0 警告。
- 改动规模（`git diff --stat`）：4 个文件，+441/−36；`DiagnosticExportService.cs` 保持原有的 CRLF 和末尾无换行，另外三份保持 LF。

## 提交

- `5388daf` desktop: 修复导出脱敏遗漏 N1-N3（4 个文件，+441/−36）。
- 提交前核对：HEAD 为 `4d42afb`，4 个文件的 sha256 与上面验证时一致，工作区没有其他改动。
- 暂存后索引中没有 CR；`DiagnosticExportService.cs` 在工作区是 CRLF，由 `core.autocrlf=true` 转成 LF 入库。

## 仍未覆盖

- 没有 `<机器名>\` 或 `<域>\` 前缀的其他账户名、其他计算机名。
- 嵌在更长标识里的进程名（如 WER 目录名 `AppCrash_<进程>_...`，下划线算作词内字符）。
- 邮箱地址（隐私说明未声明会处理）。
- 紧贴在 ASCII 单词后的 `::` 串（如 `name::1`）不视为 IPv6。
