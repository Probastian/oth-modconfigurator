# Changelog

All notable changes to **oth-modconfigurator** are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).

## [0.7.2] - 2026-10-22

### Added
- **Settings > Controls**: like the game's own actions, every bindable has a binding **per input device**. The page's *Input* entry decides whether the rows show and edit the keyboard or the gamepad binding. A mod's `<Name>Key` and `<Name>Button` entries form one bindable.
- A mod that has no binding for the selected device gets the game's **lock icon** instead of being hidden; headings are never hidden.
- **Settings > Mods**: the game's own *remove binding* footer prompt appears while a binding row is selected, and the game's remove-binding input clears the row (same as on the Controls page).

### Fixed
- Controls page navigation: our rows join the page's own list of focusable rows, so only the very last row wraps to the top and wrapping up goes to the real last row (not to "reset to default"). The links are re-applied if the page rebuilds them.

### Removed
- The **F7 / L3+R3 overlay window** and its settings (`OpenKey`, `OpenButton`, `GamepadChord`, `UiScale`, `BlockGameInput`).
- The `NativeSettingsTab` and `Enabled` settings: the Mods entry in Settings is always on (switching them off could lock you out of re-enabling the mod).

## [0.5.0] - 2026-10-22

### Added
- Binding rows on the Controls page show the game's own **input glyph on the right** (keyboard keys, PlayStation / Xbox pad glyphs). If the game's own icon update refuses a row, the glyph is resolved and drawn from the game's icon sets.
- The game's radial **waiting circle** fills while a binding waits for an input.
- The game's **remove-binding** input clears our rows on the Controls page.
- Only the entries for the device selected in the page's *Input* entry are shown, and they follow it when it changes.
- The Mods page loads the Controls row template on demand, so it always uses the same binding rows without visiting Settings > Controls first.

### Fixed
- Binding rows no longer show the template row's text (e.g. "Hochschalten") after being re-enabled.
- Binding L1 / R1 no longer makes the Controls page switch tabs.
- A press that just bound a button can no longer start a new capture.

### Removed
- UI hierarchy dump files and development-only logging.

## [0.3.0] - 2026-10-22

First public release.

### Added
- **Settings > Mods**: a new tab in the game's settings menu, built from the game's own controls. Pick a mod, change its toggles, numbers and choices, reset a mod to its defaults; changes apply immediately and are saved to the mod's `.cfg`.
- Mod bindings at the bottom of **Settings > Controls**: every mod's key and gamepad-button settings (entries whose id ends in `Key` or contains `Button`), one heading per mod, as clones of the game's binding rows with press-to-rebind (Esc / Start cancels, Backspace / Delete unbinds).
- A game-styled **quick window** (F7 or L3 + R3) with the same content, also used as a fallback.
- Settings: `NativeSettingsTab`, `ControlsPageBindings`, `OpenKey`, `GamepadChord`, `HiddenCategories`, `UiScale`, `BlockGameInput`.
