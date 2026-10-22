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
            public BindRows.Heading Heading;
            public readonly List<BindRows.Row> Rows = new List<BindRows.Row>();
            public readonly List<BindableVm> Bindables = new List<BindableVm>();      // parallel to Rows
        }

        private static StateControlsSettingsMenu _state;
        private static ContextUIControlsSettingsMenu _ctx;
        private static float _since;
        private static bool _built;
        private static GameInput.ControlSchemeType _scheme;
        private static readonly List<Block> Blocks = new List<Block>();
        private static List<CategoryVm> _cats = new List<CategoryVm>();

        // the page's own navigation around its last entry ("reset to default") and its first entry
        private static Selectable _reset, _first;
        private static List<Selectable> _ours = new List<Selectable>();
        private static int _navFrame;

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
            _reset = _first = null;
            _ours.Clear();
        }

        internal static void Pump()
        {
            try
            {
                BindRows.Tick();
                if (_state == null) return;

                if (_built)
                {
                    var scheme = _state.currentScheme;           // the device chosen in the page's "Input" entry
                    if (scheme != _scheme) { _scheme = scheme; ApplyScheme(); }
                    if (++_navFrame % 4 == 0) EnforceNavigation();      // the page rebuilds its own links on some events
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
            if (!BindRows.EnsureTemplate()) BindRows.CacheTemplate(template.gameObject);
            BindRows.CacheUnbindPrompt(ctx.gameObject);

            _cats = ConfigModel.Discover();
            var parent = template.transform.parent;
            var popState = _state.TryCast<StateUIPoppableState>();

            // the page's first entry (what its last entry wraps to) - taken before we touch anything
            _reset = ctx.ResetBasicToDefaultButton;
            var vanilla = _state.basicSelectables;
            _first = _reset != null ? _reset.navigation.selectOnDown : null;
            if (_first == null && vanilla != null && vanilla.Count > 0) _first = vanilla[0];

            foreach (var cat in _cats)
            {
                if (cat.Bindables.Count == 0) continue;

                var block = new Block { Heading = BindRows.CreateHeading(template.gameObject, parent, cat.Name) };
                bool keyboard = _state.currentScheme == GameInput.ControlSchemeType.KeyboardAndMouse;
                foreach (var bindable in cat.Bindables)
                {
                    BindRows.Row row = null;
                    row = BindRows.Create(template.gameObject, parent, bindable.For(keyboard), bindable.Name, popState, () => ScrollTo(row));
                    block.Rows.Add(row);
                    block.Bindables.Add(bindable);
                }
                Blocks.Add(block);
            }

            _scheme = _state.currentScheme;
            ApplyScheme();
            RegisterWithPage();
        }

        /// <summary>
        /// The page keeps a list of its focusable rows and builds its up/down links (and the wrap-around) from it. Our rows join that
        /// list right after the "reset" entry, so the page's own logic produces the same chain we want: ... reset, ours..., wrap.
        /// </summary>
        private static void RegisterWithPage()
        {
            try
            {
                var list = _state.basicSelectables;
                if (list == null || _reset == null) return;
                int at = list.IndexOf(_reset);
                if (at < 0) return;                         // the reset entry is not part of the page's list: our own wiring alone has to do
                foreach (var s in _ours)
                    if (!list.Contains(s)) list.Insert(++at, s);
                _state.UpdateBaseSelectablesStatus();
            }
            catch (Exception e) { Mod.Log.Warning($"Could not register the mod rows with the Controls page: {e.Message}"); }
            RewireNavigation(_ours);
        }

        /// <summary>Re-applies our links if something (the page) changed them.</summary>
        private static void EnforceNavigation()
        {
            if (_reset == null || _ours.Count == 0) return;
            var last = _ours[_ours.Count - 1];
            bool ok = _reset.navigation.selectOnDown == _ours[0]
                   && _ours[0].navigation.selectOnUp == _reset
                   && last.navigation.selectOnDown == _first
                   && (_first == null || _first.navigation.selectOnUp == last);
            if (!ok) RewireNavigation(_ours);
        }

        /// <summary>
        /// Like the game's own rows, every bindable is always listed; each row shows and edits the binding of the device selected in
        /// the page's "Input" entry. A mod that has no binding for that device gets a locked row (the game's lock icon).
        /// </summary>
        private static void ApplyScheme()
        {
            bool keyboard = _scheme == GameInput.ControlSchemeType.KeyboardAndMouse;
            var all = new List<Selectable>();

            foreach (var b in Blocks)
            {
                b.Heading.Apply();
                for (int i = 0; i < b.Rows.Count; i++)
                {
                    var r = b.Rows[i];
                    if (!BindRows.Capturing) BindRows.SetEntry(r, b.Bindables[i].For(keyboard));
                    BindRows.Refresh(r);                  // also puts our label back (re-enabling restores the template row's text)
                    var s = r.Go.GetComponent<Selectable>();
                    if (s != null) all.Add(s);
                }
            }

            _ours = all;
            RewireNavigation(all);

            var rt = (Blocks.Count > 0 ? Blocks[0].Heading.Go.transform.parent : null) as RectTransform;
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
        }

        /// <summary>
        /// The page's last native entry ("reset to default") used to link straight back to the first entry (and the first entry
        /// up to it). Only the very last row of the page may wrap: reset -> ours ... -> first entry, and first entry up -> our last
        /// row. So the wrap-around goes to the real bottom, and "down" on reset continues into our rows.
        /// </summary>
        private static void RewireNavigation(List<Selectable> ours)
        {
            if (_reset == null) return;

            if (ours.Count == 0)
            {
                var n = _reset.navigation; n.selectOnDown = _first; _reset.navigation = n;
                if (_first != null) { var f = _first.navigation; f.selectOnUp = _reset; _first.navigation = f; }
                return;
            }

            for (int i = 0; i < ours.Count; i++)
            {
                var nav = ours[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = i == 0 ? _reset : ours[i - 1];
                nav.selectOnDown = i == ours.Count - 1 ? _first : ours[i + 1];
                nav.selectOnLeft = null;
                nav.selectOnRight = null;
                ours[i].navigation = nav;
            }

            var r = _reset.navigation;
            r.selectOnDown = ours[0];
            _reset.navigation = r;

            if (_first != null)
            {
                var f = _first.navigation;
                f.selectOnUp = ours[ours.Count - 1];
                _first.navigation = f;
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
