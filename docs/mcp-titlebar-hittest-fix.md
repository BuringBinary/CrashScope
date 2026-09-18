# Title-bar Buttons Dead: WindowChrome Hit-Test Fix (2026-09-18)

Minimize / maximize / close in the custom title bar did nothing in the packaged build.
Dragging the title bar worked, so the window itself was healthy - only the three buttons
were unreachable.

## Root cause

`MainWindow.xaml` combines `WindowStyle="None"` with a `WindowChrome` whose
`CaptionHeight` is `40`:

```xml
<WindowChrome CaptionHeight="40" ResizeBorderThickness="6" .../>
```

That declares the entire top 40 px strip as a **non-client caption area**. Windows resolves
hit-testing there to `HTCAPTION` before WPF ever routes a mouse event, so the strip drags
the window and swallows the click. The three buttons live inside that strip and are exactly
`Height="40"`, so every pixel of them was covered.

`WindowChrome.IsHitTestVisibleInChrome="True"` is the opt-out that returns an element to the
client area. It did not appear anywhere in the repository - confirmed by a repo-wide search
returning zero matches. The handlers in `MainWindow.xaml.cs`
(`MinimizeButton_Click` / `MaxRestoreButton_Click` / `CloseButton_Click`) were correct and
wired up; they simply never fired.

This is invisible in a plain `dotnet run` only by luck of focus/DPI, and reproduces reliably
in a packaged build - which matches the report that the exe launches fine but the buttons are dead.

## Changes

| File | Change |
|---|---|
| `src/CrashScope.Desktop/Themes/DarkTheme.xaml` | `TitleBarButton` style: added `WindowChrome.IsHitTestVisibleInChrome=True` + `SnapsToDevicePixels` |
| `src/CrashScope.Desktop/Themes/LightTheme.xaml` | same, keeping both themes identical |
| `src/CrashScope.Desktop/MainWindow.xaml` | button `StackPanel` also marked hit-test visible |
| `src/CrashScope.Desktop/MainWindow.xaml.cs` | maximise glyph now driven by `OnStateChanged` |

The setter lives on the shared `TitleBarButton` style, so `TitleBarCloseButton`
(`BasedOn` that style) inherits it and any future caption button is covered by default.

### Secondary fix: the maximise glyph lied

`MaxRestoreButton_Click` used to flip `MaxRestoreButton.Content` by hand. Any state change
that bypassed the button - Aero Snap, Win+Up, double-clicking the caption (which this window
supports via `TitleBar_MouseLeftButtonDown`), or restoring from the tray - left the icon
showing the wrong glyph. The glyph is now derived from the real `WindowState` in an
`OnStateChanged` override, and the click handler only toggles state.

## Behaviour notes (unchanged, by design)

Minimize and close both call `HideToTray()`: the window hides to the tray rather than
minimising or exiting, and `OnClosing` cancels unless `RequestExit()` set `_forceClose`.
That is the documented intent (see the remarks on `RequestExit`). Now that the buttons
actually fire, **close will make the window vanish to the tray** - it is not a crash. Real
exit is the tray menu's Exit item. `HideToTray` falls back to `WindowState.Minimized` when
the tray icon failed to construct, so the window can never become unreachable.

Note `SettingsPage.xaml` exposes `MinimizeToTrayCheckBox` and `CloseToTrayCheckBox`, but
neither is read anywhere in `src/` - the tray behaviour is currently hardcoded. Wiring those
checkboxes is a separate task.

## Verification

```bash
export PATH="$PATH:/c/Users/BuringBinary/.dotnet"
dotnet build src/CrashScope.Desktop/CrashScope.Desktop.csproj -c Release
```

Build succeeded, 0 warnings, 0 errors; `get_diagnostics` over
`src/CrashScope.Desktop` returns nothing.

Manual checks to run on the repackaged exe:

1. Click minimize -> window hides to tray; tray icon restores it.
2. Click maximize -> fills the work area, glyph becomes restore; click again -> back to normal.
3. Aero Snap / Win+Up / double-click caption -> glyph matches the actual state.
4. Click close -> hides to tray; tray Exit terminates the process with no ghost icon.
5. Drag the empty part of the title bar -> still moves the window.

## Repackage

The fix is in XAML and code-behind, so the published output must be rebuilt - an existing
packaged exe still contains the broken resources.

