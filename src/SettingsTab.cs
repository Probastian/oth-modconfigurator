using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigurator
{
    /// <summary>
    /// Adds a "Mods" entry to the game's settings menu, built from the game's own controls.
    ///
    /// 1. When the settings menu opens, its HUD button is cloned into a new "Mods" button (native look, native navigation).
    /// 2. Clicking it asks the game to open its HUD settings page - a real, native settings page.
    /// 3. When that page is up, it is re-filled (<see cref="ModsPage"/>): the original rows are hidden and replaced by clones of the
    ///    page's own option selector / button rows, one per mod setting.
    /// Everything is wired up with defensive checks; if the page cannot be built the overlay window (F7) opens as a fallback.
    /// </summary>
    internal static class SettingsTab
    {
        private static StateSettingsMenu _settingsState;
        private static float _settingsSince;
        private static bool _injected;

        private static StateHUDSettingsMenu _hudState;
        private static float _hudSince;
        private static bool _hudWantsPage;
        private static bool _openRequested;
        private static ModsPage _page;

        private static float Now => Time.realtimeSinceStartup;

        // ------------------------------------------------------------------ hooks (from Harmony patches)

        internal static void OnSettingsMenuOpen(StateSettingsMenu state)
        {
            _settingsState = state;
            _settingsSince = Now;
            _injected = false;
        }

        internal static void OnHudPageOpen(StateHUDSettingsMenu state)
        {
            _hudState = state;
            _hudSince = Now;
            _hudWantsPage = _openRequested;
            _openRequested = false;
            if (_page != null) { _page.Close(); _page = null; }
        }

        internal static void OnHudPageClosing(StateHUDSettingsMenu state)
        {
            if (_page != null) { _page.Close(); _page = null; }
            if (_hudState != null && state != null && _hudState.Pointer == state.Pointer) _hudState = null;
            _hudWantsPage = false;
        }

        // ------------------------------------------------------------------ per-frame

        internal static void Pump()
        {
            try
            {
                if (_settingsState != null && !_injected)
                {
                    if (Now - _settingsSince > 8f) { Mod.Log.Warning("Settings menu UI never became available - no Mods button added."); _settingsState = null; }
                    else TryInject();
                }

                if (_hudState != null)
                {
                    var ctx = _hudState.hudSettingsMenuUIContext;
                    bool ready = ctx != null && ctx.ShowTime != null;
                    if (_hudWantsPage && _page == null)
                    {
                        if (ready) TryBuildPage(ctx);
                        else if (Now - _hudSince > 5f)
                        {
                            Mod.Log.Warning("HUD settings page never became available - the Mods page was not built.");
                            _hudWantsPage = false;
                        }
                    }
                }

                _page?.Tick();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"SettingsTab.Pump failed: {e}");
                _settingsState = null;
            }
        }

        // ------------------------------------------------------------------ main settings menu: add the button

        private static void TryInject()
        {
            var ctx = _settingsState.settingsMenuUIContext;
            if (ctx == null || ctx.HUDButton == null) return;

            var hud = ctx.HUDButton;
            var parent = hud.transform.parent;
            var go = UnityEngine.Object.Instantiate(hud.gameObject, parent);
            go.name = "ModsButton";
            go.SetActive(true);
            var btn = go.GetComponent<UIButtonSelector>();
            var state = _settingsState;
            ModsPage.SetLabel(go, null, "Mods");

            btn.OnPointerClickOrSubmit = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UIButtonSelector>>(
                new Action<UIButtonSelector>(_ => RequestOpen(state, btn)));

            // place it after the last settings button
            int last = hud.transform.GetSiblingIndex();
            foreach (var b in new[] { ctx.GraphicsButton, ctx.AudioButton, ctx.ControlsButton, ctx.HUDButton, ctx.InterfaceButton, ctx.CameraButton, ctx.LanguageButton, ctx.ProfileButton })
                if (b != null && b.transform.parent == parent) last = Math.Max(last, b.transform.GetSiblingIndex());
            go.transform.SetSiblingIndex(last + 1);

            try { state.settingsMenuButtons.Add(btn); } catch { /* list is optional bookkeeping */ }
            FixNavigation(parent);

            _injected = true;
        }

        /// <summary>If the menu uses explicit up/down navigation, thread the new button into it (automatic navigation needs nothing).</summary>
        private static void FixNavigation(Transform parent)
        {
            var list = new List<Selectable>();
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (!child.gameObject.activeSelf) continue;
                var s = child.GetComponent<Selectable>();
                if (s != null) list.Add(s);
            }
            for (int i = 0; i < list.Count; i++)
            {
                var nav = list[i].navigation;
                if (nav.mode != Navigation.Mode.Explicit) continue;
                nav.selectOnUp = i > 0 ? list[i - 1] : nav.selectOnUp;
                nav.selectOnDown = i < list.Count - 1 ? list[i + 1] : nav.selectOnDown;
                list[i].navigation = nav;
            }
        }

        private static void RequestOpen(StateSettingsMenu state, UIButtonSelector button)
        {
            try
            {
                _openRequested = true;
                state.HUDButtonSelected(button);       // opens the game's own HUD settings page, which we then re-fill
            }
            catch (Exception e)
            {
                _openRequested = false;
                Mod.Log.Error($"Could not open the settings page: {e}");
            }
        }

        // ------------------------------------------------------------------ the page

        private static void TryBuildPage(ContextUIHUDSettingsMenu ctx)
        {
            _hudWantsPage = false;
            try
            {
                _page = new ModsPage(_hudState, ctx);
                _page.Build();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"Could not build the native Mods page: {e}");
                try { _page?.Abort(); } catch { /* best effort */ }
                _page = null;
            }
        }
    }
}
