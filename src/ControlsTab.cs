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
    /// </summary>
    internal static class ControlsTab
    {
        private static StateControlsSettingsMenu _state;
        private static float _since;
        private static bool _built;
        private static bool _dumped;
        private static readonly List<GameObject> Added = new List<GameObject>();
        private static readonly List<BindRows.Row> Rows = new List<BindRows.Row>();
        private static List<CategoryVm> _cats = new List<CategoryVm>();

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
        }

        private static void Teardown()
        {
            BindRows.Abort();
            foreach (var r in Rows) BindRows.Forget(r);
            Rows.Clear();
            Added.Clear();                                  // the game destroys the page, our rows are part of it
            foreach (var c in _cats) c.Save();
            _cats.Clear();
        }

        internal static void Pump()
        {
            try
            {
                BindRows.Tick();
                if (_state == null || _built) { SaveIfDue(); return; }

                var ctx = _state.controlsSettingsMenuUIContext;
                var template = ctx != null ? ctx.ShiftUpButton : null;
                if (template == null || template.GetComponentInChildren<ContextUIRebindingControlsButton>(true) == null)
                {
                    if (Now - _since > 10f) { Mod.Log.Warning("Controls page never became available - no mod bindings added."); _state = null; }
                    return;
                }
                if (Now - _since < 0.5f) return;            // let the page finish its own layout first

                if (!_dumped) { _dumped = true; UiDump.Dump("Controls page (before changes)", ctx.transform, 11, 700); }
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
            var chain = new List<Selectable>();
            var popState = _state.TryCast<StateUIPoppableState>();

            // the last native, focusable row of this section: our first row hangs below it
            Selectable last = null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var s = parent.GetChild(i).GetComponent<Selectable>();
                if (s != null && s.interactable && s.gameObject.activeInHierarchy) last = s;
            }
            if (last != null) chain.Add(last);

            int rows = 0, mods = 0;
            foreach (var cat in _cats)
            {
                var binds = cat.Entries.FindAll(e => e.Kind == EntryKind.KeyCapture || e.Kind == EntryKind.ButtonCapture);
                if (binds.Count == 0) continue;
                mods++;

                Added.Add(BindRows.CreateHeading(template.gameObject, parent, cat.Name));
                foreach (var e in binds)
                {
                    BindRows.Row row = null;
                    row = BindRows.Create(template.gameObject, parent, e, popState, () => ScrollTo(row));
                    Rows.Add(row);
                    Added.Add(row.Go);
                    var sel = row.Go.GetComponent<Selectable>();
                    if (sel != null) chain.Add(sel);
                    rows++;
                }
            }

            for (int i = 1; i < chain.Count; i++)
            {
                var up = chain[i - 1].navigation;
                up.mode = Navigation.Mode.Explicit;
                up.selectOnDown = chain[i];
                chain[i - 1].navigation = up;

                var down = chain[i].navigation;
                down.mode = Navigation.Mode.Explicit;
                down.selectOnUp = chain[i - 1];
                down.selectOnDown = null;
                down.selectOnLeft = null;
                down.selectOnRight = null;
                chain[i].navigation = down;
            }

            var rt = parent as RectTransform;
            if (rt != null) LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            UiDump.Dump("Controls page (after changes)", ctx.transform, 11, 900);
            Mod.Log.Msg($"Controls page: added {rows} binding row(s) for {mods} mod(s) at the bottom");
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
