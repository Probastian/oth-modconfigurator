using System;
using MelonLoader;
using MelonLoader.Preferences;
using UnityEngine.InputSystem;

namespace ModConfigurator
{
    /// <summary>The configurator's own settings (UserData\ModConfigurator.cfg) - editable in its own window, too.</summary>
    internal static class Settings
    {
        private static MelonPreferences_Category _cat;

        internal static MelonPreferences_Entry<bool> Enabled;
        internal static MelonPreferences_Entry<string> OpenKey;
        internal static MelonPreferences_Entry<bool> GamepadChord;
        internal static MelonPreferences_Entry<string> HiddenCategories;
        internal static MelonPreferences_Entry<float> UiScale;
        internal static MelonPreferences_Entry<bool> BlockGameInput;
        internal static MelonPreferences_Entry<bool> NativeSettingsTab;
        internal static MelonPreferences_Entry<bool> ControlsPageBindings;

        internal static void Init()
        {
            _cat = MelonPreferences.CreateCategory(ModInfo.Name, "Mod Configurator");
            _cat.SetFilePath($"UserData/{ModInfo.Name}.cfg");

            Enabled = _cat.CreateEntry("Enabled", true, "Enabled", "Master switch for this mod.");
            OpenKey = _cat.CreateEntry("OpenKey", "F7", "Open key", "Keyboard key that opens/closes the settings window. Any UnityEngine.InputSystem.Key name, or None.");
            GamepadChord = _cat.CreateEntry("GamepadChord", true, "Gamepad shortcut (L3 + R3)", "Click both sticks together to open/close the window with a gamepad.");
            HiddenCategories = _cat.CreateEntry("HiddenCategories", "MelonLoader,Loader,Il2CppAssemblyGenerator", "Hidden categories",
                "Comma-separated MelonPreferences category identifiers that are not shown in the window (edit in the .cfg file).");
            UiScale = _cat.CreateEntry("UiScale", 1f, "Window scale", "Size multiplier of the settings window.", validator: new ValueRange<float>(0.5f, 1.6f));
            NativeSettingsTab = _cat.CreateEntry("NativeSettingsTab", true, "Native 'Mods' settings tab", "Add a Mods entry to the game's settings menu (needs a restart). The F7 window works either way.");
            ControlsPageBindings = _cat.CreateEntry("ControlsPageBindings", true, "Mod keys in Settings > Controls", "List every mod's key / gamepad-button settings at the bottom of the game's Controls page (one heading per mod).");
            BlockGameInput = _cat.CreateEntry("BlockGameInput", true, "Block game input while open",
                "Switch the game to its UI input map while the window is open so the car/camera don't react to your navigation.");
        }

        internal static Key? ParseOpenKey()
        {
            string s = OpenKey.Value;
            if (string.IsNullOrWhiteSpace(s) || s.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;
            return Enum.TryParse<Key>(s, true, out var k) ? k : (Key?)null;
        }
    }
}
