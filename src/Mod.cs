using MelonLoader;
using UnityEngine.InputSystem;

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
            Log.Msg($"{ModInfo.Name} {ModInfo.Version} loaded - settings tab: Settings > Mods; quick window: {Settings.OpenKey.Value}" +
                    (Settings.GamepadChord.Value ? " or L3+R3" : ""));
        }

        public override void OnUpdate()
        {
            if (!Settings.Enabled.Value) { if (ConfigWindow.IsOpen) ConfigWindow.Close(); return; }

            // native "Mods" tab in the game's settings menu
            SettingsTab.Pump();
            ControlsTab.Pump();

            // quick-access overlay window (also the fallback if the native page cannot be built)
            var kb = Keyboard.current;
            var open = Settings.ParseOpenKey();
            if (kb != null && open.HasValue && kb[open.Value].wasPressedThisFrame)
                ConfigWindow.Toggle();

            var pad = Gamepad.current;
            if (pad != null && Settings.GamepadChord.Value)
            {
                bool both = pad.leftStickButton.isPressed && pad.rightStickButton.isPressed;
                bool edge = pad.leftStickButton.wasPressedThisFrame || pad.rightStickButton.wasPressedThisFrame;
                if (both && edge) ConfigWindow.Toggle();
            }

            ConfigWindow.Tick();
            ConfigWindow.AutoSave();
        }

        public override void OnApplicationQuit() => ConfigWindow.Close();
    }
}
