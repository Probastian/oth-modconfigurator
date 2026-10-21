using System;
using HarmonyLib;
using Il2Cpp;

namespace ModConfigurator.Patches
{
    // The game's settings menu (the list: Graphics, Audio, Controls, HUD, ...). We add a "Mods" entry to it.
    [HarmonyPatch(typeof(StateSettingsMenu), nameof(StateSettingsMenu.Open))]
    internal static class StateSettingsMenu_Open_Patch
    {
        private static void Postfix(StateSettingsMenu __instance)
        {
            try { SettingsTab.OnSettingsMenuOpen(__instance); }
            catch (Exception e) { Mod.Log.Error(e.ToString()); }
        }
    }

    // The HUD settings page doubles as the vessel for our "Mods" page (cloned native rows).
    [HarmonyPatch(typeof(StateHUDSettingsMenu), nameof(StateHUDSettingsMenu.Open))]
    internal static class StateHUDSettingsMenu_Open_Patch
    {
        private static void Postfix(StateHUDSettingsMenu __instance)
        {
            try { SettingsTab.OnHudPageOpen(__instance); }
            catch (Exception e) { Mod.Log.Error(e.ToString()); }
        }
    }

    [HarmonyPatch(typeof(StateHUDSettingsMenu), nameof(StateHUDSettingsMenu.ClosingCallback))]
    internal static class StateHUDSettingsMenu_Closing_Patch
    {
        private static void Prefix(StateHUDSettingsMenu __instance)
        {
            try { SettingsTab.OnHudPageClosing(__instance); }
            catch (Exception e) { Mod.Log.Error(e.ToString()); }
        }
    }

    // Settings > Controls: our mods' key / button settings are appended at the bottom of the page.
    [HarmonyPatch(typeof(StateControlsSettingsMenu), nameof(StateControlsSettingsMenu.Open))]
    internal static class StateControlsSettingsMenu_Open_Patch
    {
        private static void Postfix(StateControlsSettingsMenu __instance)
        {
            try { ControlsTab.OnPageOpen(__instance); }
            catch (Exception e) { Mod.Log.Error(e.ToString()); }
        }
    }

    [HarmonyPatch(typeof(StateControlsSettingsMenu), nameof(StateControlsSettingsMenu.ClosingCallback))]
    internal static class StateControlsSettingsMenu_Closing_Patch
    {
        private static void Prefix()
        {
            try { ControlsTab.OnPageClosing(); }
            catch (Exception e) { Mod.Log.Error(e.ToString()); }
        }
    }
}
