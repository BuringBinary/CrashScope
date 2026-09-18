# UI Interaction Audit: Are There More Dead Controls? (2026-09-18)

Follow-up to [`mcp-titlebar-hittest-fix.md`](mcp-titlebar-hittest-fix.md). That fix found the
title-bar buttons were swallowed by `WindowChrome`'s caption area. This audit asks the obvious
next question: is the same class of defect - a control the user can click that does nothing -
present anywhere else in `CrashScope.Desktop`?

## Scope and method

Checked across all 14 XAML files and their code-behind:

1. Other windows/popups with `WindowStyle=None` + `WindowChrome` (the exact original bug).
2. Named interactive controls with no event wiring **and** no code reference (dead controls).
3. Event handlers wired in XAML but with an empty body.
4. Controls whose state is never read (silently inert settings).
5. `async void` handlers that can throw past the handler boundary (click appears to do nothing,
   or takes the process down).
6. Hit-test blockers: `IsHitTestVisible=False`, `IsEnabled=False`, `Opacity=0`, z-order overlays.

## Result summary

| # | Finding | Severity | Status |
|---|---|---|---|
| 1 | `MinimizeToTrayCheckBox` / `CloseToTrayCheckBox` never read | **Real bug** | **Fixed** - see [`mcp-tray-settings-fix.md`](mcp-tray-settings-fix.md) |
| 2 | No global unhandled-exception handler | ~~Real risk~~ | **RETRACTED - audit error, see below** |
| 3 | `CheckButton_Click` leaves the button disabled if it throws | Minor | **Fixed** |
| 4 | Other windows with the WindowChrome bug | None | Clear |
| 5 | Dead controls (no event, no reference) | None | Clear |
| 6 | Empty handler bodies | None | Clear |
| 7 | Hit-test blockers | None | Clear |

The original hit-test bug is **not** replicated anywhere: `MainWindow` is the only `Window` in
the project, and it is the only user of `WindowChrome`. Every other clickable control resolves
to a real, non-empty handler.

## Finding 1 - two tray checkboxes are decorative (real bug)

`Pages/SettingsPage.xaml:74-79` declares:

- `MinimizeToTrayCheckBox` ("最小化到托盘"), `IsChecked="True"`
- `CloseToTrayCheckBox` ("关闭时最小化到托盘而非退出"), `IsChecked="True"`

Neither has a `Checked`/`Unchecked` handler, and neither name appears anywhere in `*.cs`.
`SaveButton_Click` (`SettingsPage.xaml.cs:188`) persists only the two retention sliders:

```csharp
_retention.Settings.MaxRetentionDays = (int)RetentionDaysSlider.Value;
_retention.Settings.MaxTotalSizeMb   = (int)MaxSizeSlider.Value;
_retention.Save();
```

So the user can untick "关闭时最小化到托盘而非退出", press Save, get
"Settings saved successfully.", and the behaviour does not change: `MinimizeButton_Click` and
`CloseButton_Click` both call `HideToTray()` unconditionally.

This is worse after the title-bar fix than before it. Previously the buttons did nothing at
all, so the checkbox being inert was invisible. Now close really does hide to tray, and the
setting that claims to control that is a no-op.

Contrast with `AutoStartCheckBox` directly above it, which is wired (`AutoStartCheckBox_Changed`),
applies immediately, reads back its real state via `RefreshStartupState()`, and reports failure.
That is the pattern the two tray checkboxes should follow.

Fix sketch: persist both flags (RetentionService already owns a settings file, or add a small
UI-settings store), then branch in `MainWindow.HideToTray()` / `CloseButton_Click`. The
existing `_tray is null` fallback to `WindowState.Minimized` must be preserved so the window
can never become unreachable.

## Finding 2 - RETRACTED: the global exception handler already exists

**This finding was wrong and is withdrawn. No code change was needed.**

`App.xaml.cs:30-53` already installs all three handlers in the `App` constructor:
`AppDomain.CurrentDomain.UnhandledException`, `TaskScheduler.UnobservedTaskException` and
`DispatcherUnhandledException`. The dispatcher handler logs via `StartupLog.Error`, writes a
legacy crash log, shows the user a message box naming `StartupLog.LogPath`, guards re-entry
with `_shuttingDown`, and exits with `Shutdown(1)`. That is a more complete net than the
audit proposed adding.

Root cause of the error: the audit searched with `pattern` containing `|` alternation but
without `is_regex: true`. `search_files` defaults to **literal** matching, so it looked for the
single string `DispatcherUnhandledException|UnhandledException|TaskScheduler.UnobservedTaskException`
and returned `returned_matches: 0`. That zero was misread as "the handlers do not exist"
rather than "the query was malformed". Re-running the same pattern with `is_regex: true`
returns 4 matches, all in `App.xaml.cs`.

Lesson for future audits in this repository: a zero-result search is evidence about the query
until the query itself has been validated. The MCP server's own guidance makes the same point
about LSP - an empty semantic result does not prove a symbol is absent - and it applies to
text search with equal force. Confirm a negative with a second, differently-shaped query
before reporting it as a finding.

## Finding 3 - `CheckButton_Click` can strand its own button (minor)

`Pages/HomePage.xaml.cs:178`:

```csharp
CheckButton.IsEnabled = false;
CheckButton.Content = "检查中...";
var analysis = await Task.Run(_service.CheckPreviousUncleanSession);
CheckButton.Content = "检查上次异常";
CheckButton.IsEnabled = true;
```

There is no `try/finally`. `CheckPreviousUncleanSession` catches internally today, so this does
not currently reproduce - but the guarantee lives in another class. If it ever throws, the
button stays greyed out at "检查中..." until the page is rebuilt, and with Finding 2 the
process would already be gone. Wrapping the restore in `finally` is a two-line hardening.

## Notes on false positives

A first pass flagged ~13 controls as "dead" because their `x:Name` is never referenced in
code-behind. That is not a defect: names like `ConnectButton`, `SaveButton` and
`CloseAnalysisButton` carry `Click` wiring in XAML and their handlers are implemented. Only
controls with *neither* wiring *nor* a code reference qualify, and after that filter the list
is empty apart from the two checkboxes in Finding 1.

`IncidentsPage.xaml:67` has a deliberate `IsEnabled="False"` on a placeholder element; it is
not user-facing breakage.

## Verification

No source changes were made by this audit. The tree still builds clean from the previous fix:

```bash
export PATH="$PATH:/c/Users/BuringBinary/.dotnet"
dotnet build src/CrashScope.Desktop/CrashScope.Desktop.csproj -c Release
```

