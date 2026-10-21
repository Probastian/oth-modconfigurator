using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace ModConfigurator
{
    /// <summary>
    /// Key / gamepad-button settings shown like the game's own Settings > Controls rows: a clone of one of its binding rows
    /// (label on the left, the game's own input icon on the right), the same "waiting for input" indicator while a new input is
    /// captured, Esc / Start to cancel and Backspace / Delete to unbind.
    ///
    /// The icon element needs an InputAction to draw, so every row owns a tiny private InputAction that mirrors the setting.
    /// If the icon can't be drawn the row falls back to showing the value as text in its label.
    /// </summary>
    internal static class BindRows
    {
        internal sealed class Row
        {
            public GameObject Go;
            public UIButtonSelector Btn;
            public EntryVm Entry;
            public InputAction Action;
            public UIInputActionIcon Icon;
            public GameObject WaitingGo;
            public TextMeshProUGUI Label;
            public bool IconOk;
        }

        private static GameObject _holder, _template;
        private static readonly List<Row> Rows = new List<Row>();
        private static Row _capturing;
        private static StateUIPoppableState _popOwner;
        private static float _restorePopAt = -1f;

        internal static bool HasTemplate => _template != null;
        internal static GameObject Template => _template;
        private static float Now => Time.realtimeSinceStartup;

        // ------------------------------------------------------------------ template

        /// <summary>Keeps a hidden copy of a Controls binding row so other pages (the Mods page) can build the same rows later.</summary>
        internal static void CacheTemplate(GameObject source)
        {
            if (_template != null || source == null) return;
            if (_holder == null)
            {
                _holder = new GameObject("OTH_BindRowTemplates");
                UnityEngine.Object.DontDestroyOnLoad(_holder);
                _holder.SetActive(false);
            }
            _template = UnityEngine.Object.Instantiate(source, _holder.transform);
            _template.name = "BindRowTemplate";
            _template.SetActive(false);
        }

        // ------------------------------------------------------------------ creation

        internal static Row Create(GameObject template, Transform parent, EntryVm entry, StateUIPoppableState popOwner, Action onSelected)
        {
            var go = UnityEngine.Object.Instantiate(template, parent);
            go.name = "ModBindRow";
            go.SetActive(true);

            var row = new Row { Go = go, Entry = entry, Btn = go.GetComponent<UIButtonSelector>() };
            var rb = go.GetComponentInChildren<ContextUIRebindingControlsButton>(true);
            row.Icon = rb != null && rb.InputActionIcon != null ? rb.InputActionIcon : go.GetComponentInChildren<UIInputActionIcon>(true);
            row.WaitingGo = rb != null && rb.WaitingImage != null ? rb.WaitingImage.gameObject : null;
            row.Label = FindLabel(go, row.Icon);
            if (row.WaitingGo != null) row.WaitingGo.SetActive(false);

            // a clone must not keep pointing at the original page's scroll area
            var rbs = go.GetComponent<UIRebindingButtonSelector>();
            if (rbs != null && parent is RectTransform pr) { try { rbs.viewport = pr; } catch { /* optional */ } }

            row.Action = new InputAction("OTH/" + entry.Owner.Name + "/" + entry.Name, InputActionType.Button);
            AddBinding(row);

            if (row.Btn != null)
            {
                var r = row;
                row.Btn.OnPointerClickOrSubmit = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<UIButtonSelector>>(
                    new Action<UIButtonSelector>(_ => Begin(r, popOwner)));
                if (onSelected != null)
                    row.Btn.OnSelectorSelected = DelegateSupport.ConvertDelegate<Il2CppSystem.Action>(new Action(onSelected));
            }

            Rows.Add(row);
            Refresh(row);
            return row;
        }

        /// <summary>A non-interactive heading (mod name) cloned from a binding row.</summary>
        internal static GameObject CreateHeading(GameObject template, Transform parent, string text)
        {
            var go = UnityEngine.Object.Instantiate(template, parent);
            go.name = "ModBindHeading";
            go.SetActive(true);

            var sel = go.GetComponent<UISelector>();
            if (sel != null) UnityEngine.Object.Destroy(sel);               // no focus, no click

            var icon = go.GetComponentInChildren<UIInputActionIcon>(true);
            if (icon != null) icon.gameObject.SetActive(false);
            var rb = go.GetComponentInChildren<ContextUIRebindingControlsButton>(true);
            if (rb != null && rb.WaitingImage != null) rb.WaitingImage.gameObject.SetActive(false);

            foreach (var img in go.GetComponentsInChildren<Image>(true))
            {
                string n = img.gameObject.name;
                if (n.IndexOf("Opacity", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0)
                    img.enabled = false;
            }

            var label = FindLabel(go, icon);
            if (label != null)
            {
                SetText(label, text.ToUpperInvariant());
                label.color = OthShared.GameUi.Current.Accent;
            }
            return go;
        }

        internal static void Forget(Row row)
        {
            Rows.Remove(row);
            if (_capturing == row) { _capturing = null; }
        }

        // ------------------------------------------------------------------ display

        internal static void Refresh(Row row)
        {
            if (row == null || row.Go == null) return;
            string value = Convert.ToString(row.Entry.Entry.BoxedValue) ?? "None";
            bool none = string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase);

            row.IconOk = false;
            if (row.Icon != null)
            {
                try
                {
                    var scheme = row.Entry.Kind == EntryKind.KeyCapture ? GameInput.ControlSchemeType.KeyboardAndMouse : GameInput.ControlSchemeType.Gamepad;
                    row.Icon.gameObject.SetActive(true);
                    row.Icon.useFixedControlScheme = true;
                    row.Icon.fixedControlScheme = scheme;

                    // rebuild the private action so its single binding mirrors the setting
                    row.Action = new InputAction("OTH/" + row.Entry.Owner.Name + "/" + row.Entry.Name, InputActionType.Button);
                    if (!none) AddBinding(row);
                    row.Icon.inputActionReference = InputActionReference.Create(row.Action);
                    if (none) row.Icon.SetInvisible(); else row.Icon.UpdateIcon();
                    row.IconOk = true;
                }
                catch (Exception e) { Mod.Log.Warning($"Binding icon failed for {row.Entry.Name}: {e.Message}"); }
            }

            SetText(row.Label, row.IconOk ? row.Entry.Name : $"{row.Entry.Name}:  {(none ? "-" : value)}");
        }

        private static void AddBinding(Row row)
        {
            string path = ControlPath(row.Entry);
            if (path == null) return;
            string group = null;
            try
            {
                var pia = GameInput.PlayerInputActions;
                if (pia != null) group = row.Entry.Kind == EntryKind.KeyCapture ? pia.MouseandKeyboardScheme.bindingGroup : pia.GamepadScheme.bindingGroup;
            }
            catch { /* group is optional */ }
            row.Action.AddBinding(path, null, null, group);
        }

        /// <summary>"<Keyboard>/f8" style control path for the entry's current value, or null for None / unknown.</summary>
        internal static string ControlPath(EntryVm e)
        {
            string value = Convert.ToString(e.Entry.BoxedValue);
            if (string.IsNullOrWhiteSpace(value) || value.Equals("None", StringComparison.OrdinalIgnoreCase)) return null;

            if (e.Kind == EntryKind.KeyCapture)
            {
                if (!Enum.TryParse<Key>(value, true, out var key)) return null;
                var kb = Keyboard.current;
                string name = kb != null ? kb[key].name : value.ToLowerInvariant();
                return "<Keyboard>/" + name;
            }
            if (e.Kind == EntryKind.ButtonCapture)
            {
                if (!Enum.TryParse<UnityEngine.InputSystem.LowLevel.GamepadButton>(value, true, out var btn)) return null;
                var pad = Gamepad.all.Count > 0 ? Gamepad.all[0] : Gamepad.current;
                string name = pad != null ? pad[btn].name : char.ToLowerInvariant(value[0]) + value.Substring(1);
                return "<Gamepad>/" + name;
            }
            return null;
        }

        // ------------------------------------------------------------------ capture flow

        private static void Begin(Row row, StateUIPoppableState popOwner)
        {
            if (_capturing != null || Capture.Active) return;
            _capturing = row;
            _popOwner = popOwner;
            Capture.Begin(row.Entry);
            if (_popOwner != null) { try { _popOwner.CanPop = false; } catch { /* optional */ } }   // Esc / B must cancel the capture, not leave the menu
            _restorePopAt = -1f;

            if (row.WaitingGo != null) row.WaitingGo.SetActive(true);
            if (row.Icon != null) { try { row.Icon.SetInvisible(); } catch { /* optional */ } }
            if (!row.IconOk) SetText(row.Label, $"{row.Entry.Name}:  {Capture.Prompt}");
        }

        /// <summary>Per frame; finishes a running capture and re-enables menu closing a moment later.</summary>
        internal static void Tick()
        {
            if (_capturing != null && Capture.Tick())
            {
                var row = _capturing;
                _capturing = null;
                if (row.WaitingGo != null) row.WaitingGo.SetActive(false);
                Refresh(row);
                foreach (var other in Rows) if (other != row && other.Entry == row.Entry) Refresh(other);
                row.Entry.Owner.Dirty = true;
                row.Entry.Owner.LastChange = DateTime.UtcNow;
                _restorePopAt = Now + 0.3f;
            }

            if (_restorePopAt > 0f && Now >= _restorePopAt)
            {
                _restorePopAt = -1f;
                if (_popOwner != null) { try { _popOwner.CanPop = true; } catch { /* optional */ } }
                _popOwner = null;
            }
        }

        internal static bool Capturing => _capturing != null;

        /// <summary>Re-enables menu closing immediately (the page is going away).</summary>
        internal static void Abort()
        {
            if (_capturing != null) { Capture.Cancel(); _capturing = null; }
            if (_popOwner != null) { try { _popOwner.CanPop = true; } catch { } _popOwner = null; }
            _restorePopAt = -1f;
        }

        // ------------------------------------------------------------------ text helpers

        private static TextMeshProUGUI FindLabel(GameObject go, UIInputActionIcon icon)
        {
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t == null) continue;
                if (t.GetComponentInParent<UIInputActionIcon>(true) != null) continue;      // the icon's own key-name text
                return t;
            }
            return null;
        }

        private static void SetText(TextMeshProUGUI t, string text)
        {
            if (t == null) return;
            var fun = t.TryCast<FunTextTMP>();
            if (fun != null)
            {
                try { fun.ClearTextChangeHandler(); } catch { /* not registered */ }
            }
            t.text = text;
        }
    }
}
