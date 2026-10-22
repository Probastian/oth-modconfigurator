using MelonLoader;

namespace ModConfigurator
{
    public class Mod : MelonMod
    {
        internal static Mod Instance { get; private set; }
        internal static MelonLogger.Instance Log => Instance.LoggerInstance;

        public override void OnInitializeMelon()
        {
            Instance = this;
            Settings.Init();
            Log.Msg($"{ModInfo.Name} {ModInfo.Version} loaded - Settings > Mods, mod bindings in Settings > Controls");
        }

        public override void OnUpdate()
        {
            SettingsTab.Pump();          // native "Mods" tab in the game's settings menu
            ControlsTab.Pump();          // mod bindings at the bottom of Settings > Controls
        }
    }
}
