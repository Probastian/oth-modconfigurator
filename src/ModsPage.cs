using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppFunCore.UI;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace ModConfigurator
{
    /// <summary>
    /// The "Mods" settings page: the game's own HUD settings page, re-filled with clones of its own controls.
    ///  - a "Mod" option row (cycle through the loaded mods), an optional "Page" row for mods with many settings,
    ///  - one native option row per bool / number / choice setting, native button rows for hotkeys / gamepad buttons,
    ///  - a native "reset" button.
    /// The page's original rows stay alive but hidden, so the HUD page's own logic keeps working untouched.
    /// Rows are cloned once and then re-used (never destroyed while the page is open); the game destroys the page as a whole.
    /// </summary>
    internal sealed class ModsPage
    {
        private const int MaxRows = 9;           // the HUD page's own row count - it is laid out for exactly this many

        private sealed class RowRef
        {
            public GameObject Go;
            public UIOptionSelector Sel;
            public UIButtonSelector Btn;
            public Transform Parent;
            public Action<int> OnChange;          // option rows: called with the new index
            public Action OnClick;                // button rows
            public BindRows.Row Bind;             // Controls-style key / button rows (not pooled)
        }

        private readonly StateHUDSettingsMenu _state;
        private readonly ContextUIHUDSettingsMenu _ctx;
        private readonly Transform _rowParent, _resetParent;
        private readonly GameObject _optionTemplate, _buttonTemplate;

        private readonly List<GameObject> _hidden = new List<GameObject>();
        private readonly List<RowRef> _optionPool = new List<RowRef>();
        private readonly List<RowRef> _buttonPool = new List<RowRef>();
        private readonly List<RowRef> _active = new List<RowRef>();
        private int _optionsUsed, _buttonsUsed;

        private List<CategoryVm> _cats = new List<CategoryVm>();
        private int _cat, _pageIndex;
        private bool _building, _pendingRebuild, _closed;
        private int _pendingSelect = -1;
        private int _selectFrames;

        internal ModsPage(StateHUDSettingsMenu state, ContextUIHUDSettingsMenu ctx)
        {
            _state = state;
            _ctx = ctx;
            _optionTemplate = ctx.ShowTime.gameObject;
            _buttonTemplate = ctx.ResetToDefaultButton.gameObject;
            _rowParent = ctx.ShowTime.transform.parent;
            _resetParent = ctx.ResetToDefaultButton.transform.parent;
        }

        // ------------------------------------------------------------------ build / teardown

        internal void Build()
        {
            foreach (var o in new[] { _ctx.ShowTime, _ctx.ShowEngineRPM, _ctx.ShowCompass, _ctx.ShowDrivetrainMode, _ctx.InclineIndicatorMode,
                                      _ctx.PingIndicatorMode, _ctx.ShowPlayerNames, _ctx.StreamerMode, _ctx.ShowDebugLocation })
                if (o != null) Hide(o.gameObject);
            Hide(_ctx.ResetToDefaultButton.gameObject);

            _cats = ConfigModel.Discover();
            SetTitle("Mods");
            RebuildRows(0);
            TryFirstSelected();
        }

        internal void Abort()
        {
            DeactivateRows();
            foreach (var go in _hidden) if (go != null) go.SetActive(true);
            _hidden.Clear();
        }

        internal void Close()
        {
            if (_closed) return;
            _closed = true;
            foreach (var c in _cats) c.Save();
            _ownCapture = false;
            Capture.Cancel();
            BindRows.Abort();
        }

        private void Hide(GameObject go)
        {
            if (go == null || !go.activeSelf) return;
            go.SetActive(false);
            _hidden.Add(go);
        }

        private void SetTitle(string text)
        {
            TextMeshProUGUI best = null;
            foreach (var t in _ctx.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t == null || t.GetComponentInParent<UISelector>(true) != null) continue;
                if (best == null || t.fontSize > best.fontSize) best = t;
            }
            if (best == null) return;
            SetText(best, text);
        }

        // ------------------------------------------------------------------ rows

        private void DeactivateRows()
        {
            foreach (var r in _active)
                if (r.Bind != null) { BindRows.Forget(r.Bind); if (r.Go != null) UnityEngine.Object.Destroy(r.Go); }
            foreach (var r in _optionPool) if (r.Go != null) r.Go.SetActive(false);
            foreach (var r in _buttonPool) if (r.Go != null) r.Go.SetActive(false);
            _active.Clear();
            _optionsUsed = _buttonsUsed = 0;
        }

        /// <param name="selectIndex">Index into the active rows to focus afterwards (clamped).</param>
        private void RebuildRows(int selectIndex)
        {
            DeactivateRows();

            if (_cats.Count == 0)
            {
                UseButton(_rowParent, "No mod settings found", null, false);
                FinishLayout(0);
                return;
            }

            _cat = Mathf.Clamp(_cat, 0, _cats.Count - 1);
            var cat = _cats[_cat];

            // row 0: which mod
            var names = new List<string>();
            foreach (var c in _cats) names.Add(c.Name);
            UseOption("Mod", names, _cat, i => { _cat = i; _pageIndex = 0; Defer(0); });

            int free = MaxRows - 1;                        // rows left under the mod selector
            int perPage = cat.Entries.Count <= free ? free : free - 1;
            int pages = Math.Max(1, (cat.Entries.Count + perPage - 1) / perPage);
            _pageIndex = Mathf.Clamp(_pageIndex, 0, pages - 1);

            if (pages > 1)
            {
                var texts = new List<string>();
                for (int p = 0; p < pages; p++) texts.Add($"{p + 1} / {pages}");
                UseOption("Page", texts, _pageIndex, i => { _pageIndex = i; Defer(1); });
            }

            int start = _pageIndex * perPage;
            for (int i = start; i < Math.Min(cat.Entries.Count, start + perPage); i++) AddEntry(cat.Entries[i]);

            UseButton(_resetParent, "Reset this mod to defaults", () =>
            {
                foreach (var e in cat.Entries) e.Reset();
                Defer(_active.Count - 1);
            }, true);

            FinishLayout(selectIndex);
        }

        /// <summary>Puts the active rows into display order and schedules the controller focus.</summary>
        private void FinishLayout(int selectIndex)
        {
            foreach (var r in _active) r.Go.transform.SetAsLastSibling();
            ApplyNavigation();
            _pendingSelect = Mathf.Clamp(selectIndex, 0, Math.Max(0, _active.Count - 1));
        }

        /// <summary>
        /// The game's rows use explicit up/down navigation. A clone still points at the original (now hidden) rows, so the
        /// controller could not leave the first row. Re-thread the links through our active rows, in display order.
        /// </summary>
        private void ApplyNavigation()
        {
            var chain = new List<Selectable>();
            foreach (var r in _active)
            {
                Selectable s = r.Sel != null ? (Selectable)r.Sel : r.Btn;
                if (s != null && s.interactable) chain.Add(s);
            }
            for (int i = 0; i < chain.Count; i++)
            {
                var nav = chain[i].navigation;
                nav.mode = Navigation.Mode.Explicit;
                nav.selectOnUp = i > 0 ? chain[i - 1] : null;
                nav.selectOnDown = i < chain.Count - 1 ? chain[i + 1] : null;
                nav.selectOnLeft = null;      // left/right are handled by the option rows themselves
                nav.selectOnRight = null;
                chain[i].navigation = nav;
            }
        }

        private void Defer(int selectIndex)
        {
            _pendingRebuild = true;
            _pendingSelect = selectIndex;
        }

        private void AddEntry(EntryVm e)
        {
            switch (e.Kind)
            {
                case EntryKind.Bool:
                    UseOption(e.Name, new List<string> { "Off", "On" }, (bool)e.Entry.BoxedValue ? 1 : 0, i => e.SetBool(i == 1));
                    break;

                case EntryKind.Number:
                {
                    var values = e.NumberChoices(41, out int idx);
                    var texts = new List<string>();
                    foreach (var v in values) texts.Add(e.FormatNumber(v));
                    int rowIndex = _active.Count;
                    UseOption(e.Name, texts, idx, i =>
                    {
                        e.SetNumber(values[Mathf.Clamp(i, 0, values.Count - 1)]);
                        if (!e.HasRange) Defer(rowIndex);        // unbounded numbers: re-centre the window of choices
                    });
                    break;
                }

                case EntryKind.Choice:
                {
                    var names = new List<string>(e.ChoiceNames);
                    int cur = Math.Max(0, names.IndexOf(e.Entry.BoxedValue.ToString()));
                    UseOption(e.Name, names, cur, i => e.SetChoice(i));
                    break;
                }

                case EntryKind.KeyCapture:
                case EntryKind.ButtonCapture:
                {
                    if (BindRows.HasTemplate)
                    {
                        // the same row element and capture flow as Settings > Controls
                        var br = BindRows.Create(BindTemplate(), _rowParent, e, _state, null);
                        _active.Add(new RowRef { Go = br.Go, Btn = br.Btn, Parent = _rowParent, Bind = br });
                        break;
                    }
                    RowRef row = null;
                    row = UseButton(_rowParent, $"{e.Name}:  {e.ValueText()}", () => BeginCapture(e, row), true);
                    break;
                }

                default:
                    UseButton(_rowParent, $"{e.Name}:  {e.ValueText()}", null, false);
                    break;
            }
        }

        // ---- pooled row creation / reuse ----

        private RowRef UseOption(string label, List<string> options, int index, Action<int> onChange)
        {
            RowRef row;
            bool fresh = _optionsUsed >= _optionPool.Count;
            if (fresh)
            {
                var go = UnityEngine.Object.Instantiate(_optionTemplate, _rowParent);
                go.name = "ModsOptionRow";
                go.SetActive(true);
                row = new RowRef { Go = go, Sel = go.GetComponent<UIOptionSelector>(), Parent = _rowParent };
                var r = row;
                row.Sel.carouselOption.OnChanged += DelegateSupport.ConvertDelegate<CarouselOption.OptionChanged>(
                    new Action<int>(i => { if (!_building && !_closed) r.OnChange?.Invoke(i); }));
                _optionPool.Add(row);
            }
            else row = _optionPool[_optionsUsed];
            _optionsUsed++;

            row.Go.SetActive(true);
            row.OnChange = null;                           // no callbacks while we configure the row

            var carousel = row.Sel.carouselOption;
            SetLabel(row.Go, carousel != null ? carousel.Option : null, label);

            var list = new Il2CppSystem.Collections.Generic.List<string>();
            foreach (var o in options) list.Add(o);
            int idx = Mathf.Clamp(index, 0, Math.Max(0, options.Count - 1));
            _building = true;
            try
            {
                if (fresh) row.Sel.Initialize(list, idx);
                else { row.Sel.carouselOption.SetOptions(list); row.Sel.carouselOption.SetSelected(idx); }
            }
            finally { _building = false; }

            row.OnChange = onChange;
            _active.Add(row);
            return row;
        }

        private RowRef UseButton(Transform parent, string text, Action onClick, bool interactable)
        {
            RowRef row = null;
            foreach (var r in _buttonPool)
                if (r.Parent == parent && !_active.Contains(r)) { row = r; break; }

            if (row == null)
            {
                var go = UnityEngine.Object.Instantiate(_buttonTemplate, parent);
                go.name = "ModsButtonRow";
                go.SetActive(true);
                row = new RowRef { Go = go, Btn = go.GetComponent<UIButtonSelector>(), Parent = parent };
                var r = row;
                row.Btn.OnPointerClickOrSubmit = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UIButtonSelector>>(
                    new Action<UIButtonSelector>(_ => { if (!_closed) r.OnClick?.Invoke(); }));
                _buttonPool.Add(row);
            }
            _buttonsUsed++;

            row.Go.SetActive(true);
            SetLabel(row.Go, null, text);
            row.OnClick = onClick;
            row.Btn.interactable = interactable;
            _active.Add(row);
            return row;
        }

        // ------------------------------------------------------------------ capture (hotkeys / gamepad buttons)

        private bool _ownCapture;

        private void BeginCapture(EntryVm e, RowRef row)
        {
            _ownCapture = true;
            Capture.Begin(e);
            if (row != null) SetLabel(row.Go, null, $"{e.Name}:  {Capture.Prompt}");
        }

        private static GameObject BindTemplate() => BindRows.Template;

        // ------------------------------------------------------------------ per frame

        internal void Tick()
        {
            if (_closed) return;

            if (_ownCapture && Capture.Active && Capture.Tick())
            {
                _ownCapture = false;
                var es = EventSystem.current;
                int idx = _active.FindIndex(r => r.Go != null && es != null && es.currentSelectedGameObject == r.Go);
                Defer(Math.Max(0, idx));
            }

            if (_pendingRebuild)
            {
                _pendingRebuild = false;
                RebuildRows(_pendingSelect);
            }

            // keep the controller focus on one of our rows (the page's own first-selected row is hidden now)
            if (_pendingSelect >= 0 && _pendingSelect < _active.Count)
            {
                if (SelectRow(_pendingSelect)) _pendingSelect = -1;
            }
            else if (_selectFrames < 30)
            {
                _selectFrames++;
                var es = EventSystem.current;
                var cur = es != null ? es.currentSelectedGameObject : null;
                if (_active.Count > 0 && (cur == null || !cur.activeInHierarchy)) SelectRow(0);
            }

            foreach (var c in _cats)
                if (c.Dirty && (DateTime.UtcNow - c.LastChange).TotalSeconds > 1.5) c.Save();
        }

        private bool SelectRow(int index)
        {
            var es = EventSystem.current;
            if (es == null || index < 0 || index >= _active.Count || _active[index].Go == null) return false;
            var go = _active[index].Go;
            if (!go.activeInHierarchy) return false;
            es.SetSelectedGameObject(go);
            return true;
        }

        private void TryFirstSelected()
        {
            try
            {
                var simple = _state.menuControl != null ? _state.menuControl.TryCast<SimpleMenuControl>() : null;
                if (simple != null && _active.Count > 0) simple.firstSelected = _active[0].Go;
            }
            catch { /* optional */ }
        }

        // ------------------------------------------------------------------ text helpers

        /// <summary>Sets the label of a cloned row. <paramref name="skip"/> is the carousel's value text, which must not be touched.</summary>
        internal static void SetLabel(GameObject row, TextMeshProUGUI skip, string text)
        {
            var t = FindLabel(row, skip);
            if (t != null) SetText(t, text);
        }

        private static TextMeshProUGUI FindLabel(GameObject row, TextMeshProUGUI skip)
        {
            foreach (var t in row.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t == null) continue;
                if (skip != null && t.Pointer == skip.Pointer) continue;
                return t;
            }
            return null;
        }

        private static void SetText(TextMeshProUGUI t, string text)
        {
            // The label is a localized FunTextTMP: detach its localization callback (this instance only) so a late
            // localization refresh cannot put the original text back.
            var fun = t.TryCast<FunTextTMP>();
            if (fun != null)
            {
                try { fun.ClearTextChangeHandler(); } catch { /* not registered */ }
            }
            t.text = text;
        }
    }
}
