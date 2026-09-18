# Tray Behaviour Settings Wired Up (2026-09-18)

Implements Finding 1 and Finding 3 from [`mcp-ui-interaction-audit.md`](mcp-ui-interaction-audit.md),
which followed [`mcp-titlebar-hittest-fix.md`](mcp-titlebar-hittest-fix.md).

## Why this mattered more after the title-bar fix

`MinimizeToTrayCheckBox` and `CloseToTrayCheckBox` had no handlers and were never read
anywhere in `src/`. Before the hit-test fix the title-bar buttons did nothing at all, so two
inert checkboxes were invisible. Once the buttons started firing, close genuinely hid the
window to the tray - while the setting claiming to control exactly that stayed a no-op and
Save still reported success.

## New: UiSettingsService

`src/CrashScope.Desktop/Services/UiSettingsService.cs` persists the two flags.

It deliberately reuses the existing convention rather than introducing a second store:
same `%LOCALAPPDATA%\CrashScope\app_settings.json` as `ThemeService` and
`LocalizationService`, same read-modify-write `JsonObject` merge so services writing different
keys do not clobber each other, and the same never-throw policy - a missing or corrupt
settings file degrades to defaults (`false`/`true`) because a preferences problem must not
stop the recorder. Setters write through on change.

### Correction: minimise defaulted to the tray, and should not have

The first version of this change defaulted **both** flags to `true`, preserving the old
hardcoded behaviour verbatim. That was wrong for minimise and the user caught it: pressing
minimise made the window disappear into the tray, which is not what the glyph promises and
not where anyone would look for it. Preserving existing behaviour was the wrong instinct here
- the existing behaviour was the bug, and "close and minimise both hide to tray" was only
ever a side effect of both handlers calling the same `HideToTray()`.

`MinimizeToTray` now defaults to `false`: minimise goes to the taskbar like every other
Windows application. `CloseToTray` stays `true`, because a flight recorder is supposed to keep
running after the window is dismissed, and the tray icon is the visible evidence that it is.
Users who want the old behaviour can tick the box.

The checkbox label was changed from "最小化到托盘" to
"最小化时隐藏到托盘（默认最小化到任务栏）" so the non-default nature is stated in the UI
rather than implied by an unticked box.

### Follow-up: changing the default was not enough

Flipping the default did not fix the reported behaviour, because the user had already opened
Settings under the schema-1 build and toggled a checkbox. `TrayBehaviourCheckBox_Changed`
writes **both** keys on every change, so `app_settings.json` contained an explicit
`"minimizeToTray": true`. A stored setting correctly wins over a default, so minimise still
hid the window.

The stored value was not a real preference - it was the old default persisted as a side
effect of touching an unrelated checkbox. That is indistinguishable, per-value, from a
deliberate choice, so the file needs a version to tell the two apart.

`UiSettingsService` now stamps `uiSettingsSchema` (currently `2`) on save. On load:

- **schema >= 2** - the file was written by a build whose minimise default was already
  `false`, so a stored `true` is a genuine user choice and is honoured.
- **schema missing (treated as 1)** - a stored `true` is discarded and the new default
  adopted; a stored `false` is kept, since it already agrees. The file is then re-saved with
  the stamp, so the migration runs exactly once.

`closeToTray` is deliberately **not** migrated: its default did not change, so any stored
value remains a valid expression of intent.

### Migration test matrix

Verified against the real `UiSettingsService` source (only the settings-root was made
injectable; `Load`/`Save` untouched), 6/6 passing:

| Case | Input | Result |
|---|---|---|
| Fresh install | no file | `min=false close=true` |
| **The reported bug** | `{min:true, close:false}` no schema | `min=false close=false` + stamped |
| Schema 1, explicit false | `{min:false, close:true}` | `min=false close=true` + stamped |
| Schema 2, deliberate tray | `{min:true, close:true, schema:2}` | `min=true` **honoured** |
| Re-run after migration | `{..., schema:2}` | unchanged (idempotent) |
| Foreign keys | `{theme:"Light", ...}` | `theme` preserved through re-save |

Exposed as `App.Current.UiSettings`, matching `Service` / `Localization` / `Theme`.

## Behaviour

Defaults are **not** symmetric, and that is deliberate:

| Setting | Default | Rationale |
|---|---|---|
| `MinimizeToTray` | `false` | Minimise must do what the glyph promises - go to the taskbar. |
| `CloseToTray` | `true` | A background recorder should survive the window being dismissed. |

| Action | Setting off (minimise default) | Setting on |
|---|---|---|
| Minimize button | `WindowState.Minimized` (taskbar) | hides to tray |
| Close button | real exit via `RequestExit()` | hides to tray (default) |
| Alt+F4, system menu | real exit | hides to tray (default) |
| Tray menu "Exit" | always a real exit | always a real exit |

`OnClosing` was changed too, not just the button. Alt+F4 and the system menu route through
`OnClosing`, so leaving it unconditional would have made the preference only half-work. The
condition is now `!_forceClose && CloseToTray && _tray is not null`:

- `_forceClose` (set by `RequestExit`) always wins, so the tray Exit path is unaffected and
  still disposes the tray icon exactly once.
- The `_tray is not null` guard preserves the existing invariant that the window can never be
  hidden with no tray icon to restore it from. `HideToTray` keeps its own fallback to
  `WindowState.Minimized` for the same reason.

The stale `<remarks>` on `RequestExit`, which asserted that the close button "still minimises
to the tray, which is the intended behaviour", was updated - that is now a user preference.

## Settings page

Both checkboxes now use `TrayBehaviourCheckBox_Changed` and apply immediately, matching
`AutoStartCheckBox` directly above them. They are *not* routed through `SaveButton_Click`,
which owns retention settings only. `OnLoaded` seeds them from the persisted values instead
of the XAML `IsChecked="True"` literal, so the boxes always show what the app will really do.
The `_initialized` guard prevents the seeding assignment from firing the handler and
writing back during load.

## Finding 3: CheckButton_Click

`Pages/HomePage.xaml.cs` disabled its button and set "检查中..." before an `await`, then
restored both afterwards with no `try/finally`. Restoration now happens in a `finally`.
`AppService.CheckPreviousUncleanSession` catches internally so this does not reproduce today,
but the guarantee now lives next to the code that disabled the button rather than depending
on another class's behaviour.

## Verification

```bash
export PATH="$PATH:/c/Users/BuringBinary/.dotnet"
dotnet build src/CrashScope.Desktop/CrashScope.Desktop.csproj -c Release
```

Build succeeded, 0 warnings, 0 errors; `get_diagnostics` over `src/CrashScope.Desktop`
returns nothing.

Manual checks on the repackaged exe:

1. Default (both ticked): minimize and close hide to tray; tray restores.
2. Untick both, close the window -> process exits, no ghost tray icon.
3. Untick "最小化到托盘" -> minimize goes to the taskbar, window restores from there.
4. Untick "关闭时最小化到托盘" -> Alt+F4 exits rather than hiding.
5. Restart after each change -> checkboxes come back in the state they were left in.
6. Tray menu Exit still terminates cleanly in every combination.

## Note

`app_settings.json` gains two keys. Older builds ignore unknown keys, so downgrading is safe.

