using System;
using System.Collections.Generic;
using Il2Cpp;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigurator
{
    /// <summary>
    /// Appends every mod's key / gamepad-button settings to the bottom of the game's Settings > Controls page: one heading per mod
    /// followed by native-looking binding rows (see <see cref="BindRows"/>). Mods need no integration - any MelonPreferences
    /// string entry whose id ends in "Key" or contains "Button" is picked up.
    ///
    /// Like the game's own bindings, only the rows for the current input device are shown: gamepad buttons while the page's
    /// "Input" entry is on gamepad, keyboard keys while it is on keyboard &amp; mouse. Our rows are woven into the page's
    /// up/down navigation, including its wrap-around between the first and the last entry.
    /// </summary>
    internal static class ControlsTab
    {
        private sealed class Block
        {
            public GameObject Heading;
            public readonly List<BindRows.Row> Rows = new List<BindRows.Row>();
        }

        private static StateControlsSettingsMenu _state;
        private static ContextUIControlsSettingsMenu _ctx;
        private static float _since;
        private static bool _built;
        private static GameInput.ControlSchemeType _scheme;
        private static readonly List<Block> Blocks = new List<Block>();
        private static List<CategoryVm> _cats = new List<CategoryVm>();

        // the page's own navigation around its last entry, remembered so it can be restored
        private static Selectable _reset, _wrapTarget;
        private static bool _wrapExists;

        private static float Now => Time.realtimeSinceStartup;

        internal static void OnPageOpen(StateControlsSettingsMenu state)
        {
            if (!Settings.ControlsPageBindings.Value) return;
            Teardown();
            _state = state;
            _since = Now;
            _built = false;
        }

        internal static void OnPageClosing()
        {
            Teardown();
            _state = null;
            _ctx = null;
        }

        private static void Teardown()
        {
            BindRows.Abort();
            foreach (var b in Blocks) foreach (var r in b.Rows) BindRows.Forget(r);
            Blocks.Clear();                                 // the game destroys the page, our rows are part of it
            foreach (var c in _cats) c.Save();
            _cats.Clear();
            _reset = _wrapTarget = null;
            _wrapExists = false;
        }

        internal static void Pump()
        {
            try
            {
                BindRows.Tick();
                if (_state == null) return;

                if (_built)
                {
                    var scheme = GameInput.ControlScheme;
                    if (scheme != _scheme) { _scheme = scheme; ApplyScheme(); }
                    SaveIfDue();
                    return;
                }

                var ctx = _state.controlsSettingsMenuUIContext;
                var template = ctx != null ? ctx.ShiftUpButton : null;
                if (template == null || template.GetComponentInChildren<ContextUIRebindingControlsButton>(true) == null)
                {
                    if (Now - _since > 10f) { Mod.Log.Warning("Controls page never became available - no mod bindings added."); _state = null; }
                    return;
                }
                if (Now - _since < 0.5f) return;            // let the page finish its own layout first

                _ctx = ctx;
                Build(ctx, template);
                _built = true;
            }
            catch (Exception e)
            {
                Mod.Log.Error($"ControlsTab.Pump failed: {e}");
                _state = null;
            }
        }

        private static void Build(ContextUIControlsSettingsMenu ctx, UIButtonSelector template)
        {
            BindRows.CacheTemplate(template.gameObject);

            _cats = ConfigModel.Discover();
            var parent = template.transform.parent;
            var popState = _state.TryCast<StateUIPoppableState>();

            _reset = ctx.ResetBasicToDefaultButton;
            if (_reset != null)
            {
                _wrapTarget = _reset.navigation.selectOnDown;
                _wrapExists = _wrapTarget != null && _wrapTarget.navigation.selectOnUp == _reset;
            }

            foreach (var cat in _cats)
            {
                var binds = cat.Entries.FindAll(e => e.Kind == EntryKind.KeyCapture || e.Kind == EntryKind.ButtonCapture);
                if (binds.Count == 0) continue;

                var block = new Block { Heading = BindRows.CreateHeading(template.gameObject, parent, cat.Name) };
                foreach (var e in binds)
                {
                    BindRows.Row row = null;
                    row = BindRows.Create(template.gameObject, parent, e, popState, () => ScrollTo(row));
                    block.Rows.Add(row);
                }
                Blocks.Add(block);
            }

            _scheme = GameInput.ControlScheme;
            ApplyScheme();
        }

        /// <summary>Shows the rows that match the current input device, hides the rest, and re-weaves the navigation.</summary>
        private static void ApplyScheme()
        {
            bool keyboard = _scheme == GameInput.ControlSchemeType.KeyboardAndMouse;
            var visible = new List<Selectable>();

            foreach (var b in Blocks)
            {
                bool any = false;
                foreach (var r in b.Rows)
                {
                    bool show = (r.Entry.Kind == EntryKind.KeyCapture) == keyboard;
                    if (r.Go.activeSelf != show) r.Go.SetActive(show);
                    if (show) { any = true; var s = r.Go.GetComponent<Selectable>(); if (s != null) visible.Add(s); }
                }
                if (b.Heading.activeSelf != any) b.Heading.SetActive(any);
            }

            RewireNavigation(visible);

            var rt = (Blocks.Count > 0 ? Blocks[0].Heading.transform.parent : null) as RectTransform;
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        /// <summary>
        /// The page's last native entry ("reset to default") used to link straight back to the first entry (and the first entry
        /// up to it). Our rows go in between: reset -> ours ... -> first entry, and first entry up -> our last row.
        /// </summary>
        private static void RewireNavigation(List<Selectable> ours)
        {
            if (_reset == null) return;

            if (ours.Count == 0)
            {
                var n = _reset.navigation; n.selectOnDown = _wrapTarget; _reset.navigation = n;
                if (_wrapExists) { var f = _wrapTarget.navigation; f.selectOnUp = _reset; _wrapTarget.navigation = f; }
                return;
            }

            for (int i = 0; i < ours.Count; i++)
            {
                var nav = ours[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = i == 0 ? _reset : ours[i - 1];
                nav.selectOnDown = i == ours.Count - 1 ? (_wrapExists ? _wrapTarget : null) : ours[i + 1];
                nav.selectOnLeft = null;
                nav.selectOnRight = null;
                ours[i].navigation = nav;
            }

            var r = _reset.navigation;
            r.selectOnDown = ours[0];
            _reset.navigation = r;

            if (_wrapExists)
            {
                var f = _wrapTarget.navigation;
                f.selectOnUp = ours[ours.Count - 1];
                _wrapTarget.navigation = f;
            }
        }

        private static void ScrollTo(BindRows.Row row)
        {
            try
            {
                var rt = row?.Go != null ? row.Go.GetComponent<RectTransform>() : null;
                if (rt != null && _state != null) _state.ScrollToView(rt);
            }
            catch { /* scrolling is cosmetic */ }
        }

        private static void SaveIfDue()
        {
            foreach (var c in _cats)
                if (c.Dirty && (DateTime.UtcNow - c.LastChange).TotalSeconds > 1.0) c.Save();
        }
    }
}
