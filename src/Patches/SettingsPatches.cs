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

    // L1 / R1 switch between the Controls page's tabs (Einfach / Erweitert). While one of our bindings waits for an input,
    // those presses must be captured instead.
    [HarmonyPatch(typeof(StateControlsSettingsMenu), nameof(StateControlsSettingsMenu.SwitchMenuLeft))]
    internal static class StateControlsSettingsMenu_SwitchLeft_Patch
    {
        private static bool Prefix() => !BindRows.Capturing;
    }

    [HarmonyPatch(typeof(StateControlsSettingsMenu), nameof(StateControlsSettingsMenu.SwitchMenuRight))]
    internal static class StateControlsSettingsMenu_SwitchRight_Patch
    {
        private static bool Prefix() => !BindRows.Capturing;
    }

    // "Remove binding" (F): the page's handler acts on the last selected *game* row; when one of our rows is selected we clear
    // our own setting instead and keep the game's handler away from its bindings.
    [HarmonyPatch(typeof(StateControlsSettingsMenu), nameof(StateControlsSettingsMenu.Unbind))]
    internal static class StateControlsSettingsMenu_Unbind_Patch
    {
        private static bool Prefix()
        {
            try { return !BindRows.TryUnbindSelected(); }
            catch (Exception e) { Mod.Log.Error(e.ToString()); return true; }
        }
    }
}
