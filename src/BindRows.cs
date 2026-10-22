using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppFunCore.Managers;
using Il2CppInterop.Runtime;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.DualShock;
using UnityEngine.UI;

namespace ModConfigurator
{
    /// <summary>
    /// Key / gamepad-button settings shown like the game's own Settings > Controls rows: a clone of one of its binding rows
    /// (setting name on the left, the game's own input icon with the current binding on the right), the same "waiting for
    /// input" indicator while a new input is captured, Esc / Start to cancel and Backspace / Delete to unbind.
    ///
    /// The game's icon element draws an InputAction. Every setting is mirrored by a small private action that lives in a
    /// private InputActionAsset. If the game's own icon update refuses it, the glyph is resolved and drawn here from the game's
    /// icon sets; if that fails too the row shows the value as text.
    /// </summary>
    internal static class BindRows
    {
        internal sealed class Row
        {
            public GameObject Go;
            public UIButtonSelector Btn;
            public EntryVm Entry;
            public InputAction Action;
            public InputActionReference Reference;
            public UIInputActionIcon Icon;                 // null once replaced by manual drawing
            public UIInputActionIconElement Element;
            public GameObject WaitingGo;
            public Image WaitingImage;                     // the game's radial "waiting for input" circle (inside the first icon element)
            public TextMeshProUGUI Label;
            public UIRebindingButtonSelector Rbs;
            public string Title;                           // the text shown on the left
            public bool IconOk;
        }

        internal sealed class Heading
        {
            public GameObject Go;
            public TextMeshProUGUI Label;
            public string Text;
            public void Apply() { SetText(Label, Text.ToUpperInvariant()); if (Label != null) Label.color = OthShared.GameUi.Current.Accent; }
        }

        private static GameObject _holder, _template, _pageInstance;
        private static UIInputActionIcon _utilIcon;       // a living icon component, for its display-string helper and icon sets
        private static InputActionAsset _asset;
        private static InputActionMap _map;
        private static int _actionCounter;
        private static bool _nativeBroken, _loggedFailure;
        private static readonly Dictionary<string, (InputAction action, InputActionReference reference)> Actions =
            new Dictionary<string, (InputAction, InputActionReference)>();
        private static readonly List<Row> Rows = new List<Row>();
        private static Row _capturing;
        private static StateUIPoppableState _popOwner;
        private static float _restorePopAt = -1f;
        private static float _lastFinishAt = -10f;

        internal static bool HasTemplate => _template != null;
        internal static GameObject Template => _template;
        /// <summary>True while a key / button capture is running (the Controls page must not switch tabs then).</summary>
        internal static bool Capturing => _capturing != null;
        private static float Now => Time.realtimeSinceStartup;

        // ------------------------------------------------------------------ template

        private static void EnsureHolder()
        {
            if (_holder != null) return;
            _holder = new GameObject("OTH_BindRowTemplates");
            UnityEngine.Object.DontDestroyOnLoad(_holder);
            _holder.SetActive(false);                       // everything under it stays inactive: no Awake/OnEnable side effects
        }

        /// <summary>
        /// Makes sure a Controls binding row is available as a template, even if the Controls page was never opened: the page's
        /// prefab is loaded through the game's own resource manager and instantiated (inactive) once.
        /// </summary>
        internal static bool EnsureTemplate()
        {
            if (_template != null) return true;
            try
            {
                string path = GameConstants.ADDRESSABLES.Menus.ControlsSettingsMenu;
                var prefab = ResourcesManager.Instance.Load<GameObject>(path);
                if (prefab == null) return false;

                EnsureHolder();
                var inst = UnityEngine.Object.Instantiate(prefab, _holder.transform);
                var ctx = inst.GetComponentInChildren<ContextUIControlsSettingsMenu>(true);
                var row = ctx != null && ctx.ShiftUpButton != null ? ctx.ShiftUpButton.gameObject : null;
                if (row == null) { UnityEngine.Object.Destroy(inst); return false; }
                _pageInstance = inst;
                _template = row;
                _utilIcon = row.GetComponentInChildren<UIInputActionIcon>(true);
                return true;
            }
            catch (Exception e)
            {
                Mod.Log.Warning($"Could not load the Controls row template: {e.Message}");
                return false;
            }
        }

        /// <summary>Fallback: keep a hidden copy of a live Controls row (used when the prefab could not be loaded).</summary>
        internal static void CacheTemplate(GameObject source)
        {
            if (_template != null || source == null) return;
            EnsureHolder();
            _template = UnityEngine.Object.Instantiate(source, _holder.transform);
            _template.name = "BindRowTemplate";
            _template.SetActive(false);
            _utilIcon = _template.GetComponentInChildren<UIInputActionIcon>(true);
        }

        // ------------------------------------------------------------------ footer prompt

        private static GameObject _unbindPrompt;

        /// <summary>
        /// The game's "remove binding" footer prompt (its input glyph + localized text) as found on the Controls page, for other
        /// pages to clone. Null if it could not be found.
        /// </summary>
        internal static GameObject UnbindPromptTemplate
        {
            get
            {
                if (_unbindPrompt == null && _pageInstance != null) _unbindPrompt = FindUnbindPrompt(_pageInstance);
                return _unbindPrompt;
            }
        }

        /// <summary>Fallback: remember the prompt of a live Controls page.</summary>
        internal static void CacheUnbindPrompt(GameObject livePage)
        {
            if (_unbindPrompt != null || livePage == null) return;
            var found = FindUnbindPrompt(livePage);
            if (found == null) return;
            EnsureHolder();
            _unbindPrompt = UnityEngine.Object.Instantiate(found, _holder.transform);
            _unbindPrompt.SetActive(false);
        }

        private static GameObject FindUnbindPrompt(GameObject root)
        {
            try
            {
                foreach (var group in root.GetComponentsInChildren<ContextUILabelsGroup>(true))
                {
                    var list = group.Bindings;
                    if (list == null) continue;
                    for (int i = 0; i < list.Count; i++)
                    {
                        var action = list[i]?.InputAction?.inputActionReference?.action;
                        if (action != null && action.name.IndexOf("Unbind", StringComparison.OrdinalIgnoreCase) >= 0) return list[i].gameObject;
                    }
                }
            }
            catch (Exception e) { LogFailure("unbind prompt", e); }
            return null;
        }

        // ------------------------------------------------------------------ creation

        /// <param name="entry">The setting the row edits; null for a bindable the mod does not support on the current device (shown locked).</param>
        internal static Row Create(GameObject template, Transform parent, EntryVm entry, string title, StateUIPoppableState popOwner, Action onSelected)
        {
            var go = UnityEngine.Object.Instantiate(template, parent);
            go.name = "ModBindRow";
            go.SetActive(true);

            var row = new Row { Go = go, Entry = entry, Title = title, Btn = go.GetComponent<UIButtonSelector>() };
            var rb = go.GetComponentInChildren<ContextUIRebindingControlsButton>(true);
            row.Icon = rb != null && rb.InputActionIcon != null ? rb.InputActionIcon : go.GetComponentInChildren<UIInputActionIcon>(true);
            row.WaitingImage = rb != null ? rb.WaitingImage : null;
            row.WaitingGo = row.WaitingImage != null ? row.WaitingImage.gameObject : null;
            row.Label = FindLabel(go);
            if (row.WaitingGo != null) row.WaitingGo.SetActive(false);

            // a clone must not keep pointing at the original page's scroll area
            var rbs = go.GetComponent<UIRebindingButtonSelector>();
            row.Rbs = rbs;
            if (rbs != null && parent is RectTransform pr) { try { rbs.viewport = pr; } catch { /* optional */ } }

            if (entry != null) (row.Action, row.Reference) = GetAction(entry);

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
        internal static Heading CreateHeading(GameObject template, Transform parent, string text)
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

            var h = new Heading { Go = go, Label = FindLabel(go), Text = text };
            h.Apply();
            return h;
        }

        internal static void Forget(Row row)
        {
            Rows.Remove(row);
            if (_capturing == row) _capturing = null;
        }

        /// <summary>Points the row at another setting (the binding of another device), or null (not supported on that device).</summary>
        internal static void SetEntry(Row row, EntryVm entry)
        {
            if (row.Entry == entry) return;
            row.Entry = entry;
            if (entry != null) (row.Action, row.Reference) = GetAction(entry);
        }

        /// <summary>The game's lock icon: what its own rows show for a binding that cannot be changed.</summary>
        private static void ShowLock(Row row, bool on)
        {
            try { if (row.Rbs != null) row.Rbs.ShowLockIcon(on); }
            catch { /* cosmetic */ }
        }

        // ------------------------------------------------------------------ the mirrored input actions

        private static (InputAction, InputActionReference) GetAction(EntryVm e)
        {
            string key = e.Owner.Name + "/" + e.Entry.Identifier;
            if (Actions.TryGetValue(key, out var found) && found.action != null) return found;

            if (_asset == null)
            {
                _asset = ScriptableObject.CreateInstance<InputActionAsset>();
                _asset.name = "OTH_ModBindings";
                _asset.hideFlags = HideFlags.HideAndDontSave;
                _map = _asset.AddActionMap("OTH");
                try   // the game's icon code looks the control schemes up on the asset
                {
                    var schemes = GameInput.PlayerInputActions?.controlSchemes;
                    if (schemes != null) for (int i = 0; i < schemes.Count; i++) _asset.AddControlScheme(schemes[i]);
                }
                catch { /* optional */ }
            }

            bool keyboard = e.Kind == EntryKind.KeyCapture;
            string group = null;
            try
            {
                var pia = GameInput.PlayerInputActions;
                if (pia != null) group = keyboard ? pia.MouseandKeyboardScheme.bindingGroup : pia.GamepadScheme.bindingGroup;
            }
            catch { /* the group only helps the icon pick the binding */ }

            // a placeholder path; the real one is applied as a binding override (see Refresh)
            var action = _map.AddAction("bind" + (_actionCounter++), InputActionType.Button, keyboard ? "<Keyboard>/space" : "<Gamepad>/buttonSouth", null, null, group);
            var reference = InputActionReference.Create(action);
            Actions[key] = (action, reference);
            return (action, reference);
        }

        // ------------------------------------------------------------------ display

        /// <summary>Redraws label and icon of the row from the setting's current value.</summary>
        internal static void Refresh(Row row)
        {
            if (row == null || row.Go == null) return;

            if (row.Entry == null)                              // the mod has no binding for this device: locked, like a fixed game binding
            {
                HideGlyph(row);
                ShowLock(row, true);
                row.IconOk = true;
                SetText(row.Label, row.Title);
                return;
            }
            ShowLock(row, false);

            string path = ControlPath(row.Entry);              // null = unbound
            string value = Convert.ToString(row.Entry.Entry.BoxedValue) ?? "None";

            bool ok = false;
            if (row.Icon != null || row.Element != null)
            {
                if (path == null) ok = HideGlyph(row);
                else
                {
                    ok = !_nativeBroken && row.Icon != null && TryNativeIcon(row, path);
                    if (!ok) ok = TryManualIcon(row, path);
                }
                if (!ok && row.Icon != null) row.Icon.gameObject.SetActive(false);      // never leave the template's glyph (R1) showing
            }

            row.IconOk = ok;
            SetText(row.Label, ok ? row.Title : $"{row.Title}:  {(path == null ? "-" : value)}");
        }

        private static bool HideGlyph(Row row)
        {
            try
            {
                if (row.Icon != null) { row.Icon.gameObject.SetActive(true); row.Icon.SetInvisible(); }
                else if (row.Element != null) row.Element.gameObject.SetActive(false);
                return true;
            }
            catch (Exception e) { LogFailure("hide glyph", e); return false; }
        }

        /// <summary>The game's own icon update on our mirrored action.</summary>
        private static bool TryNativeIcon(Row row, string path)
        {
            try
            {
                var icon = row.Icon;
                icon.gameObject.SetActive(true);
                icon.useFixedControlScheme = true;
                icon.fixedControlScheme = row.Entry.Kind == EntryKind.KeyCapture ? GameInput.ControlSchemeType.KeyboardAndMouse : GameInput.ControlSchemeType.Gamepad;
                icon.disableOnPC = icon.disableOnXBOX = icon.disableOnPS4 = false;
                if (icon.layoutElement == null) icon.layoutElement = icon.GetComponent<LayoutElement>() ?? icon.gameObject.AddComponent<LayoutElement>();
                icon.inputActionReference = row.Reference;
                row.Action.ApplyBindingOverride(0, path);
                icon.UpdateIcon();
                return true;
            }
            catch (Exception e)
            {
                _nativeBroken = true;                           // don't retry for every row
                LogFailure("native icon", e);
                return false;
            }
        }

        /// <summary>
        /// Resolves the glyph the same way the game does (display string -> the game's icon sets: keyboard keys, PlayStation or
        /// Xbox pad glyphs) and draws it into the row's first icon element.
        /// </summary>
        private static bool TryManualIcon(Row row, string path)
        {
            try
            {
                var util = _utilIcon;
                var icon = row.Icon;
                if (util == null) return false;

                var element = row.Element;
                if (element == null)
                {
                    var elements = icon != null ? icon.elements : null;
                    if (elements == null || elements.Length == 0) return false;
                    element = elements[0];
                    for (int i = 1; i < elements.Length; i++) if (elements[i] != null) elements[i].gameObject.SetActive(false);
                    if (icon != null)
                    {
                        UnityEngine.Object.Destroy(icon);      // its own refresh callbacks would put the template's glyph back
                        row.Icon = null;
                    }
                    row.Element = element;
                }

                var icons = util.inputIcons;
                bool keyboard = row.Entry.Kind == EntryKind.KeyCapture;

                var temp = new InputAction("OTH.Display", InputActionType.Button, path);
                string layout, ctrl;
                string display;
                try
                {
                    display = temp.GetBindingDisplayString(0, out layout, out ctrl, util.displayStringOptions);
                    display = util.GetLocalisedBinding(ctrl, display);
                }
                finally { temp.Dispose(); }

                Sprite sprite;
                bool showText;
                if (keyboard)
                {
                    sprite = icons.PC.GetSprite(ctrl, display) ?? icons.PC.DefaultKey;
                    showText = sprite == icons.PC.DefaultKey || sprite == icons.PC.LargeDefaultKey;
                }
                else
                {
                    var pad = Gamepad.current;
                    bool ps = pad != null && pad.TryCast<DualShockGamepad>() != null;
                    sprite = (ps ? icons.PS : icons.XBOX).GetSprite(ctrl);
                    showText = sprite == null;
                }

                element.gameObject.SetActive(true);
                if (element.SeparatorText != null) element.SeparatorText.gameObject.SetActive(false);
                if (element.HoldPanel != null) element.HoldPanel.gameObject.SetActive(false);

                const float h = 30f;
                float w = h;
                if (element.IconImage != null)
                {
                    element.IconImage.sprite = sprite;
                    element.IconImage.enabled = sprite != null;
                    element.IconImage.gameObject.SetActive(true);
                    if (sprite != null && !showText) w = h * sprite.rect.width / Mathf.Max(1f, sprite.rect.height);
                }
                if (element.IconText != null)
                {
                    element.IconText.gameObject.SetActive(showText);
                    if (showText) { element.IconText.text = display; w = Mathf.Max(h, 14f + display.Length * 15f); }
                }
                var le = element.IconLayoutElement != null ? element.IconLayoutElement
                       : element.IconImage != null ? element.IconImage.GetComponent<LayoutElement>() : null;
                if (le != null) { le.minWidth = w; le.preferredWidth = w; le.minHeight = h; le.preferredHeight = h; }
                if (element.IconImage != null) element.IconImage.rectTransform.sizeDelta = new Vector2(w, h);
                return true;
            }
            catch (Exception e)
            {
                LogFailure("manual icon", e);
                return false;
            }
        }

        private static void LogFailure(string what, Exception e)
        {
            if (_loggedFailure) return;
            _loggedFailure = true;
            Mod.Log.Warning($"Binding icon ({what}) failed: {e}");
        }

        /// <summary>"&lt;Keyboard&gt;/f8" style control path for the entry's current value, or null for None / unknown.</summary>
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
            if (row.Entry == null) return;                          // not supported on this device
            if (_capturing != null || Capture.Active) return;
            if (Now - _lastFinishAt < 0.3f) return;                // the press that just bound a button must not start a new capture
            _capturing = row;
            _popOwner = popOwner;
            Capture.Begin(row.Entry);
            if (_popOwner != null) { try { _popOwner.CanPop = false; } catch { /* optional */ } }   // Esc / B must cancel the capture, not leave the menu
            _restorePopAt = -1f;

            ShowWaiting(row, true);
            if (!row.IconOk) SetText(row.Label, $"{row.Title}:  {Capture.Prompt}");
        }

        /// <summary>
        /// The game's waiting circle lives inside the first icon element, next to the key glyph. While waiting only the circle is
        /// shown; afterwards <see cref="Refresh"/> brings the glyph back.
        /// </summary>
        private static void ShowWaiting(Row row, bool on)
        {
            try
            {
                var el = row.Element
                      ?? (row.WaitingGo != null ? row.WaitingGo.GetComponentInParent<UIInputActionIconElement>(true) : null)
                      ?? (row.Icon != null && row.Icon.elements != null && row.Icon.elements.Length > 0 ? row.Icon.elements[0] : null);
                if (on)
                {
                    if (row.Icon != null) row.Icon.gameObject.SetActive(true);
                    if (el != null)
                    {
                        el.gameObject.SetActive(true);
                        if (el.IconImage != null) el.IconImage.enabled = false;
                        if (el.IconText != null) el.IconText.gameObject.SetActive(false);
                        if (el.SeparatorText != null) el.SeparatorText.gameObject.SetActive(false);
                        if (el.HoldPanel != null) el.HoldPanel.gameObject.SetActive(false);
                    }
                    if (row.WaitingImage != null) { row.WaitingImage.fillAmount = 0f; row.WaitingImage.gameObject.SetActive(true); }
                }
                else if (row.WaitingGo != null) row.WaitingGo.SetActive(false);
            }
            catch (Exception e) { LogFailure("waiting circle", e); }
        }

        /// <summary>
        /// The game's "remove binding" input (F / a pad button): clears the selected row if it is one of ours. Returns true when
        /// it handled the press, so the page's own handler - which would act on the previously selected vanilla row - is skipped.
        /// </summary>
        internal static bool TryUnbindSelected()
        {
            var es = UnityEngine.EventSystems.EventSystem.current;
            var sel = es != null ? es.currentSelectedGameObject : null;
            if (sel == null) return false;
            foreach (var row in Rows)
            {
                if (row.Go == null || !row.Go.activeInHierarchy) continue;
                if (sel == row.Go || sel.transform.IsChildOf(row.Go.transform))
                {
                    if (_capturing == null && row.Entry != null)
                    {
                        row.Entry.SetText("None");
                        Refresh(row);
                        foreach (var other in Rows) if (other != row && other.Entry == row.Entry) Refresh(other);
                        row.Entry.Owner.Dirty = true;
                        row.Entry.Owner.LastChange = DateTime.UtcNow;
                    }
                    return true;
                }
            }
            return false;
        }

        /// <summary>Per frame; finishes a running capture and re-enables menu closing a moment later.</summary>
        internal static void Tick()
        {
            if (_capturing != null && Capture.Active && _capturing.WaitingImage != null)
                _capturing.WaitingImage.fillAmount = Capture.Progress;           // empty -> full, like the game's own

            if (_capturing != null && Capture.Tick())
            {
                var row = _capturing;
                var entry = row.Entry;
                _capturing = null;
                _lastFinishAt = Now;
                ShowWaiting(row, false);
                Refresh(row);
                if (entry != null)
                {
                    foreach (var other in Rows) if (other != row && other.Entry == entry) Refresh(other);
                    entry.Owner.Dirty = true;
                    entry.Owner.LastChange = DateTime.UtcNow;
                }
                _restorePopAt = Now + 0.3f;
            }

            if (_restorePopAt > 0f && Now >= _restorePopAt)
            {
                _restorePopAt = -1f;
                if (_popOwner != null) { try { _popOwner.CanPop = true; } catch { /* optional */ } }
                _popOwner = null;
            }
        }

        /// <summary>Re-enables menu closing immediately (the page is going away).</summary>
        internal static void Abort()
        {
            if (_capturing != null) { Capture.Cancel(); _capturing = null; }
            if (_popOwner != null) { try { _popOwner.CanPop = true; } catch { /* optional */ } _popOwner = null; }
            _restorePopAt = -1f;
        }

        // ------------------------------------------------------------------ text helpers

        private static TextMeshProUGUI FindLabel(GameObject go)
        {
            foreach (var t in go.GetComponentsInChildren<TextMeshProUGUI>(true))
            {
                if (t == null) continue;
                if (t.GetComponentInParent<UIInputActionIcon>(true) != null) continue;      // the icon's own key-name text
                if (t.GetComponentInParent<UIInputActionIconElement>(true) != null) continue;
                return t;
            }
            return null;
        }

        /// <summary>
        /// Sets a cloned label. The label is a localized FunTextTMP: its localization is detached (this instance only), otherwise it
        /// snaps back to the original row's text (e.g. "Hochschalten") whenever the row is re-enabled.
        /// </summary>
        internal static void SetText(TextMeshProUGUI t, string text)
        {
            if (t == null) return;
            var fun = t.TryCast<FunTextTMP>();
            if (fun != null)
            {
                try { fun.ClearTextChangeHandler(); } catch { /* not registered */ }
                try { fun.textLocalization = null; } catch { /* optional */ }
            }
            t.text = text;
        }
    }
}
