using MelonLoader;
using MelonLoader.Preferences;

namespace ModConfigurator
{
    /// <summary>The configurator's own settings (UserData\ModConfigurator.cfg) - editable on its own Settings > Mods page, too.</summary>
    internal static class Settings
    {
        private static MelonPreferences_Category _cat;
        internal static MelonPreferences_Entry<string> HiddenCategories;
        internal static MelonPreferences_Entry<bool> ControlsPageBindings;

        internal static void Init()
        {
            _cat = MelonPreferences.CreateCategory(ModInfo.Name, "Mod Configurator");
            _cat.SetFilePath($"UserData/{ModInfo.Name}.cfg");
            HiddenCategories = _cat.CreateEntry("HiddenCategories", "MelonLoader,Loader,Il2CppAssemblyGenerator", "Hidden categories",
                "Comma-separated MelonPreferences category identifiers that are not shown in the Mods settings (edit in the .cfg file).");
            ControlsPageBindings = _cat.CreateEntry("ControlsPageBindings", true, "Mod keys in Settings > Controls", "List every mod's bindable keys / gamepad buttons at the bottom of the game's Controls page (one heading per mod).");
        }
    }
}
