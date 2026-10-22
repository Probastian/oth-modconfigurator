# oth-modconfigurator

An in-game **settings editor for every MelonLoader mod** in [Over the Hill](https://store.steampowered.com/search/?term=over%20the%20hill). It reads the standard `MelonPreferences` of all loaded mods, so mods need no integration. The UI is built from the game's own settings controls.

> Unofficial fan mod. Not affiliated with or endorsed by Funselektor.

## Features

- **Settings > Mods**: a new tab in the game's settings menu. Pick a mod, change its toggles, numbers and choices with the game's own option rows, reset a mod to its defaults. Changes apply immediately and are saved to the mod's `.cfg`.
- **Settings > Controls**: every mod's key and gamepad-button settings (string entries whose id ends in `Key` or contains `Button`) are listed at the bottom of the Controls page, one heading per mod, as clones of the game's binding rows - the setting name on the left, the game's own input icon with the current binding on the right, the game's radial waiting circle, Esc / Start cancels, Backspace / Delete or the game's remove-binding input unbinds. Like the game's own bindings, like the game's own actions, every bindable has a binding per device: the page's *Input* entry decides whether the rows show and edit the keyboard or the gamepad binding. A mod that has no binding for the selected device gets a locked row (the game's lock icon) instead of being hidden.

## Requirements

- Over the Hill (Steam), Unity 6 / IL2CPP
- [MelonLoader](https://github.com/LavaGang/MelonLoader) **0.7.x** (tested with 0.7.3 Open-Beta)

## Install

1. Install MelonLoader into the game folder and start the game once.
2. Download `ModConfigurator.dll` from the [latest release](../../releases/latest) and put it in `<game folder>\Mods`.
3. Start the game: Settings now has a **Mods** entry.

## Settings (`UserData\ModConfigurator.cfg`)

| Setting | Default | |
|---|---|---|
| `ControlsPageBindings` | `true` | List mod keys/buttons at the bottom of Settings > Controls |
| `HiddenCategories` | `MelonLoader,Loader,Il2CppAssemblyGenerator` | Categories not shown |

## Tips for mod authors

Give entries a display name and description, use a `ValueRange<T>` validator for numbers (it becomes the allowed range), name hotkey entries `<Name>Key` and gamepad ones `<Name>Button` (stored as `UnityEngine.InputSystem` `Key` / `GamepadButton` names, `None` = unbound). A `<Name>Key` and a `<Name>Button` entry with the same `<Name>` form one bindable in Settings > Controls with a keyboard and a gamepad binding; with only one of them the other device shows a locked row.

## Limits

- Only mods that use MelonPreferences are listed (mods with their own JSON config are not).
- Free-text string settings are read-only.
- Whether a mod applies a changed value live depends on the mod.
- Coexists with other config menu mods.

## Known status

The native Settings pages are cloned from the game's UI at runtime and depend on its layout, so a game update may need a mod update.

## Multiplayer

Purely local UI.

## Build from source

You need the .NET SDK, the game with MelonLoader, and to have started the game once so MelonLoader generated `MelonLoader\Il2CppAssemblies`.

```powershell
dotnet build -c Release            # copies ModConfigurator.dll into <game>\Mods
dotnet build -c Release -p:DeployToGame=false
```

If the game is not in the default Steam path, create `GamePath.user.props`:

```xml
<Project><PropertyGroup><GamePath>D:\Games\over the hill</GamePath></PropertyGroup></Project>
```

No game files are included in this repository; the project references your local game and MelonLoader assemblies.

## Bug reports and contributions

Please report bugs using [GitHub issues](https://github.com/Probastian/oth-modconfigurator/issues). Pull requests and forks are welcome.

## License

MIT - see [LICENSE](LICENSE).
