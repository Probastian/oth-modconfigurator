using System;
using System.Collections.Generic;
using Il2Cpp;
using Il2CppTMPro;
using OthShared;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace ModConfigurator
{
    /// <summary>
    /// The settings window: category list on the left, the selected category's entries on the right.
    /// Drawn with the game's HUD look (see GameUi). Keyboard, gamepad and mouse are all polled directly.
    /// </summary>
    internal static class ConfigWindow
    {
        private sealed class Row
        {
            public Image Bg;
            public TextMeshProUGUI Label;
            public TextMeshProUGUI Value;
            public string LabelText, ValueText;
            public int State = -1;           // 0 normal, 1 selected+focused, 2 selected unfocused
        }

        // ---- layout (reference pixels, relative to the window centre) ----
        private const float W = 1180f, H = 740f;
        private const float RowH = 50f, RowGap = 8f, RowStep = RowH + RowGap;
        private const int Visible = 9;
        private const float ListTopY = 205f;
        private const float CatX = -415f, CatW = 300f;
        private const float EntX = 150f, EntW = 800f;
        private const float ValueZoneW = 300f;

        private static GameObject _canvasGo;
        private static Canvas _canvas;
        private static RectTransform _window;
        private static readonly List<Row> CatRows = new List<Row>();
        private static readonly List<Row> EntRows = new List<Row>();
        private static TextMeshProUGUI _title, _desc, _hint, _pageInfo;

        // ---- state ----
        private static List<CategoryVm> _cats = new List<CategoryVm>();
        private static int _catIndex, _entryIndex, _catTop, _entryTop;
        private static bool _focusEntries;
        private static Il2CppSystem.Guid _mapId;
        private static bool _mapPushed;
        private static bool _cursorSaved;
        private static bool _prevCursorVisible;
        private static CursorLockMode _prevLock;
        private static readonly Dictionary<string, float> HoldStart = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> NextFire = new Dictionary<string, float>();

        internal static bool IsOpen { get; private set; }

        // ------------------------------------------------------------------ open / close

        internal static void Toggle() { if (IsOpen) Close(); else Open(); }

        internal static void Open()
        {
            if (IsOpen) return;
            try
            {
                string keepCat = _cats.Count > 0 && _catIndex < _cats.Count ? _cats[_catIndex].Category.Identifier : null;
                _cats = ConfigModel.Discover();
                _catIndex = Math.Max(0, _cats.FindIndex(c => c.Category.Identifier == keepCat));
                _entryIndex = _catTop = _entryTop = 0;
                _focusEntries = false;
                Capture.Cancel();

                GameUi.TryResolve(true);
                Build();
                IsOpen = true;
                if (Settings.BlockGameInput.Value) PushUiMap();
                ShowCursor();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"Open failed: {e}");
                Close();
            }
        }

        internal static void Close()
        {
            IsOpen = false;
            Capture.Cancel();
            foreach (var c in _cats) c.Save();
            if (_canvasGo != null) UnityEngine.Object.Destroy(_canvasGo);
            _canvasGo = null;
            CatRows.Clear();
            EntRows.Clear();
            PopUiMap();
            RestoreCursor();
        }

        /// <summary>Debounced autosave of edited categories (called every frame).</summary>
        internal static void AutoSave()
        {
            foreach (var c in _cats)
                if (c.Dirty && (DateTime.UtcNow - c.LastChange).TotalSeconds > 1.5) c.Save();
        }

        private static void PushUiMap()
        {
            try { _mapId = GameInput.PushMap(GameInput.Map.UI, 100); _mapPushed = true; }
            catch (Exception e) { Mod.Log.Warning($"Could not push the UI input map: {e.Message}"); }
        }

        private static void PopUiMap()
        {
            if (!_mapPushed) return;
            _mapPushed = false;
            try { GameInput.PopMap(_mapId); }
            catch (Exception e) { Mod.Log.Warning($"Could not pop the UI input map: {e.Message}"); }
        }

        private static void ShowCursor()
        {
            _prevCursorVisible = Cursor.visible;
            _prevLock = Cursor.lockState;
            _cursorSaved = true;
        }

        private static void RestoreCursor()
        {
            if (!_cursorSaved) return;
            _cursorSaved = false;
            // Leave cursor handling to the game's own input-map logic (PopMap re-evaluates it); only undo if we changed it.
        }

        // ------------------------------------------------------------------ build

        private static void Build()
        {
            var look = GameUi.Current;
            _canvasGo = GameUi.CreateCanvas("ModConfiguratorWindow", 5000);
            _canvas = _canvasGo.GetComponent<Canvas>();

            float s = Settings.UiScale.Value;
            _window = GameUi.Rect(_canvasGo.transform, "Window", GameUi.Center, Vector2.zero, new Vector2(W, H));
            _window.localScale = Vector3.one * s;

            // dim backdrop
            var dim = GameUi.Box(_canvasGo.transform, "Dim", Vector2.zero, new Vector2(4000f, 3000f), new Color(0f, 0f, 0f, 0.35f));
            dim.transform.SetAsFirstSibling();

            GameUi.Box(_window, "Panel", Vector2.zero, new Vector2(W, H), look.Panel, 14f);

            _title = GameUi.Label(_window, "Title", "MOD SETTINGS", 40f, look.Text, new Vector2(-W / 2f + 330f, H / 2f - 50f), new Vector2(600f, 56f), TextAlignmentOptions.MidlineLeft);
            _pageInfo = GameUi.Label(_window, "Page", "", 22f, Dim(look.Text, 0.6f), new Vector2(W / 2f - 220f, H / 2f - 50f), new Vector2(360f, 40f), TextAlignmentOptions.MidlineRight);

            GameUi.Box(_window, "TitleRule", new Vector2(0f, H / 2f - 90f), new Vector2(W - 60f, 3f), Dim(look.Text, 0.25f));

            for (int i = 0; i < Visible; i++)
            {
                float y = ListTopY - i * RowStep - RowH / 2f;
                CatRows.Add(MakeRow("Cat" + i, CatX, y, CatW, false));
                EntRows.Add(MakeRow("Ent" + i, EntX, y, EntW, true));
            }

            GameUi.Box(_window, "FooterRule", new Vector2(0f, -H / 2f + 150f), new Vector2(W - 60f, 3f), Dim(look.Text, 0.25f));
            _desc = GameUi.Label(_window, "Desc", "", 24f, Dim(look.Text, 0.85f), new Vector2(0f, -H / 2f + 100f), new Vector2(W - 80f, 80f), TextAlignmentOptions.TopLeft);
            _desc.textWrappingMode = TextWrappingModes.Normal;
            _hint = GameUi.Label(_window, "Hint", "", 20f, Dim(look.Text, 0.55f), new Vector2(0f, -H / 2f + 30f), new Vector2(W - 60f, 30f));
        }

        private static Row MakeRow(string name, float x, float y, float w, bool withValue)
        {
            var look = GameUi.Current;
            var row = new Row { Bg = GameUi.Box(_window, name, new Vector2(x, y), new Vector2(w, RowH), look.ChipOff, 8f) };
            row.Label = GameUi.Label(row.Bg.transform, "Label", "", 26f, look.Text, new Vector2(-12f, 0f), new Vector2(w - (withValue ? ValueZoneW + 30f : 40f), RowH), TextAlignmentOptions.MidlineLeft);
            if (withValue)
                row.Value = GameUi.Label(row.Bg.transform, "Value", "", 26f, look.Text, new Vector2(w / 2f - ValueZoneW / 2f - 14f, 0f), new Vector2(ValueZoneW, RowH), TextAlignmentOptions.MidlineRight);
            return row;
        }

        private static Color Dim(Color c, float a) => new Color(c.r, c.g, c.b, a);

        // ------------------------------------------------------------------ per-frame

        internal static void Tick()
        {
            if (!IsOpen) return;
            try
            {
                if (_cats.Count == 0) { HandleCloseOnly(); if (IsOpen) Refresh(); return; }
                if (Capture.Active) Capture.Tick(); else HandleInput();
                if (!IsOpen) return;     // closed by the input handler
                Refresh();
            }
            catch (Exception e)
            {
                Mod.Log.Error($"Window error: {e}");
                Close();
            }
        }

        private static void HandleCloseOnly()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame)) Close();
        }

        // ------------------------------------------------------------------ input

        /// <summary>True on the first frame a direction is held, then repeats (faster the longer it's held).</summary>
        private static bool Repeat(string id, bool held, out float speedMult)
        {
            speedMult = 1f;
            float now = Time.unscaledTime;
            if (!held) { HoldStart.Remove(id); NextFire.Remove(id); return false; }
            if (!HoldStart.TryGetValue(id, out float start))
            {
                HoldStart[id] = now;
                NextFire[id] = now + 0.38f;
                return true;
            }
            float heldFor = now - start;
            speedMult = heldFor > 2.2f ? 10f : heldFor > 1.1f ? 4f : 1f;
            if (now >= NextFire[id])
            {
                NextFire[id] = now + (heldFor > 1.1f ? 0.04f : 0.09f);
                return true;
            }
            return false;
        }

        private static void HandleInput()
        {
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;

            bool upHeld = (kb != null && kb.upArrowKey.isPressed) || (pad != null && (pad.dpad.up.isPressed || pad.leftStick.up.isPressed));
            bool downHeld = (kb != null && kb.downArrowKey.isPressed) || (pad != null && (pad.dpad.down.isPressed || pad.leftStick.down.isPressed));
            bool leftHeld = (kb != null && kb.leftArrowKey.isPressed) || (pad != null && (pad.dpad.left.isPressed || pad.leftStick.left.isPressed));
            bool rightHeld = (kb != null && kb.rightArrowKey.isPressed) || (pad != null && (pad.dpad.right.isPressed || pad.leftStick.right.isPressed));

            bool activate = (kb != null && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) || (pad != null && pad.buttonSouth.wasPressedThisFrame);
            bool back = (kb != null && kb.escapeKey.wasPressedThisFrame) || (pad != null && pad.buttonEast.wasPressedThisFrame);
            bool reset = (kb != null && (kb.deleteKey.wasPressedThisFrame || kb.backspaceKey.wasPressedThisFrame)) || (pad != null && pad.buttonWest.wasPressedThisFrame);
            bool tab = kb != null && kb.tabKey.wasPressedThisFrame;
            bool prevCat = pad != null && pad.leftShoulder.wasPressedThisFrame;
            bool nextCat = pad != null && pad.rightShoulder.wasPressedThisFrame;
            bool pageUp = kb != null && kb.pageUpKey.wasPressedThisFrame;
            bool pageDown = kb != null && kb.pageDownKey.wasPressedThisFrame;

            if (Repeat("up", upHeld, out _)) MoveSelection(-1);
            if (Repeat("down", downHeld, out _)) MoveSelection(1);
            if (pageUp) MoveSelection(-Visible);
            if (pageDown) MoveSelection(Visible);

            if (Repeat("left", leftHeld, out float lm))
            {
                if (_focusEntries && CurrentEntry() is EntryVm le && le.Kind != EntryKind.KeyCapture && le.Kind != EntryKind.ButtonCapture && le.Kind != EntryKind.Text) le.Step(-1, lm);
                else if (_focusEntries) _focusEntries = false;
            }
            if (Repeat("right", rightHeld, out float rm))
            {
                if (!_focusEntries) { if (CurrentCategory().Entries.Count > 0) _focusEntries = true; }
                else if (CurrentEntry() is EntryVm re && re.Kind != EntryKind.KeyCapture && re.Kind != EntryKind.ButtonCapture && re.Kind != EntryKind.Text) re.Step(1, rm);
            }

            if (tab) _focusEntries = !_focusEntries && CurrentCategory().Entries.Count > 0;
            if (prevCat) SwitchCategory(-1);
            if (nextCat) SwitchCategory(1);

            if (activate)
            {
                if (!_focusEntries) { if (CurrentCategory().Entries.Count > 0) _focusEntries = true; }
                else ActivateEntry(CurrentEntry());
            }
            if (reset && _focusEntries) CurrentEntry()?.Reset();

            if (back)
            {
                if (_focusEntries) _focusEntries = false;
                else Close();
            }

            if (IsOpen && mouse != null) HandleMouse(mouse);
        }

        private static void ActivateEntry(EntryVm e)
        {
            if (e == null) return;
            switch (e.Kind)
            {
                case EntryKind.Bool:
                case EntryKind.Choice:
                    e.Step(1);
                    break;
                case EntryKind.KeyCapture:
                case EntryKind.ButtonCapture:
                    Capture.Begin(e);
                    break;
            }
        }


        // ------------------------------------------------------------------ mouse

        private static Vector2 MouseToWindow(Mouse mouse)
        {
            Vector2 m = mouse.position.ReadValue();
            float scale = _canvas != null && _canvas.scaleFactor > 0f ? _canvas.scaleFactor : 1f;
            float ws = Settings.UiScale.Value;
            return new Vector2((m.x - Screen.width / 2f) / scale / ws, (m.y - Screen.height / 2f) / scale / ws);
        }

        private static void HandleMouse(Mouse mouse)
        {
            var p = MouseToWindow(mouse);
            float wheel = mouse.scroll.ReadValue().y;

            // hover test on rows
            for (int i = 0; i < Visible; i++)
            {
                float y = ListTopY - i * RowStep - RowH / 2f;
                bool inY = Mathf.Abs(p.y - y) <= RowH / 2f;
                if (!inY) continue;

                int ci = _catTop + i;
                if (ci < _cats.Count && Mathf.Abs(p.x - CatX) <= CatW / 2f && mouse.leftButton.wasPressedThisFrame)
                {
                    if (ci != _catIndex) { _cats[_catIndex].Save(); _catIndex = ci; _entryIndex = _entryTop = 0; }
                    _focusEntries = false;
                    return;
                }

                int ei = _entryTop + i;
                var cat = CurrentCategory();
                if (ei < cat.Entries.Count && Mathf.Abs(p.x - EntX) <= EntW / 2f && mouse.leftButton.wasPressedThisFrame)
                {
                    _focusEntries = true;
                    _entryIndex = ei;
                    var e = cat.Entries[ei];
                    float rowRight = EntX + EntW / 2f;
                    if (p.x > rowRight - ValueZoneW)
                    {
                        if (e.Kind == EntryKind.Number) e.Step(p.x < rowRight - ValueZoneW / 2f ? -1 : 1, 1f);
                        else ActivateEntry(e);
                    }
                    return;
                }
            }

            if (Mathf.Abs(wheel) > 0.01f)
            {
                bool overCats = p.x < (CatX + CatW / 2f + 20f);
                int dir = wheel > 0 ? -1 : 1;
                if (overCats) { _focusEntries = false; MoveSelection(dir); }
                else if (CurrentCategory().Entries.Count > 0) { _focusEntries = true; MoveSelection(dir); }
            }
        }

        // ------------------------------------------------------------------ selection helpers

        private static CategoryVm CurrentCategory() => _cats[Mathf.Clamp(_catIndex, 0, _cats.Count - 1)];
        private static EntryVm CurrentEntry()
        {
            var c = CurrentCategory();
            return c.Entries.Count == 0 ? null : c.Entries[Mathf.Clamp(_entryIndex, 0, c.Entries.Count - 1)];
        }

        private static void MoveSelection(int delta)
        {
            if (_focusEntries)
            {
                int n = CurrentCategory().Entries.Count;
                if (n == 0) return;
                _entryIndex = Mathf.Clamp(_entryIndex + delta, 0, n - 1);
                if (_entryIndex < _entryTop) _entryTop = _entryIndex;
                if (_entryIndex >= _entryTop + Visible) _entryTop = _entryIndex - Visible + 1;
            }
            else
            {
                int before = _catIndex;
                _catIndex = Mathf.Clamp(_catIndex + delta, 0, _cats.Count - 1);
                if (_catIndex < _catTop) _catTop = _catIndex;
                if (_catIndex >= _catTop + Visible) _catTop = _catIndex - Visible + 1;
                if (_catIndex != before) { _cats[before].Save(); _entryIndex = _entryTop = 0; }
            }
        }

        private static void SwitchCategory(int delta)
        {
            bool f = _focusEntries;
            _focusEntries = false;
            MoveSelection(delta);
            _focusEntries = f && CurrentCategory().Entries.Count > 0;
        }

        // ------------------------------------------------------------------ drawing

        private static void Refresh()
        {
            var look = GameUi.Current;

            if (_cats.Count == 0)
            {
                _pageInfo.text = "";
                _desc.text = "No mod settings found. Mods register their settings through MelonPreferences.";
                _hint.text = "[Esc / B] Close";
                foreach (var r in CatRows) SetRow(r, "", null, -1, look);
                foreach (var r in EntRows) SetRow(r, "", null, -1, look);
                return;
            }

            var cat = CurrentCategory();

            for (int i = 0; i < Visible; i++)
            {
                int ci = _catTop + i;
                if (ci < _cats.Count)
                    SetRow(CatRows[i], _cats[ci].Name, null, ci == _catIndex ? (_focusEntries ? 2 : 1) : 0, look);
                else SetRow(CatRows[i], "", null, -1, look);

                int ei = _entryTop + i;
                if (ei < cat.Entries.Count)
                {
                    var e = cat.Entries[ei];
                    string v = e == Capture.Target ? Capture.Prompt : e.ValueText();
                    if (e.Kind == EntryKind.Number && ei == _entryIndex && _focusEntries) v = "<  " + v + "  >";
                    else if (!e.IsDefault && e != Capture.Target) v += "  *";
                    SetRow(EntRows[i], e.Name, v, ei == _entryIndex ? (_focusEntries ? 1 : 2) : 0, look);
                }
                else SetRow(EntRows[i], "", null, -1, look);
            }

            _pageInfo.text = $"{cat.Name}   -   {cat.Entries.Count} setting{(cat.Entries.Count == 1 ? "" : "s")}";

            var cur = _focusEntries ? CurrentEntry() : null;
            if (Capture.Active)
            {
                _desc.text = Capture.Target.Kind == EntryKind.KeyCapture ? "Press the key to use. Esc cancels, Backspace clears." : "Press the gamepad button to use. Start cancels, Backspace clears.";
                _hint.text = "";
            }
            else
            {
                _desc.text = cur != null ? cur.Description : (_cats.Count > 0 ? $"{cat.Name}  ({cat.Category.Identifier})" : "");
                _hint.text = _focusEntries
                    ? (cur != null && (cur.Kind == EntryKind.KeyCapture || cur.Kind == EntryKind.ButtonCapture)
                        ? "[Enter / A] Rebind    [Del / X] Reset    [Esc / B] Back"
                        : "[Left / Right] Change    [Enter / A] Toggle    [Del / X] Reset to default    [Esc / B] Back")
                    : "[Up / Down] Mod    [Right / Enter / A] Settings    [LB / RB] Switch mod    [Esc / B] Close";
            }
        }

        private static void SetRow(Row r, string label, string value, int state, GameUi.Look look)
        {
            if (state < 0)
            {
                if (r.State != -1) { r.Bg.enabled = false; r.Label.text = ""; if (r.Value != null) r.Value.text = ""; r.LabelText = r.ValueText = null; r.State = -1; }
                return;
            }
            if (r.State != state)
            {
                r.State = state;
                r.Bg.enabled = true;
                Color bg = state == 1 ? look.ChipOn : state == 2 ? new Color(look.ChipOff.r + 0.12f, look.ChipOff.g + 0.12f, look.ChipOff.b + 0.12f, look.ChipOff.a) : look.ChipOff;
                Color fg = state == 1 ? look.TextDark : look.Text;
                r.Bg.color = bg;
                r.Label.color = fg;
                if (r.Value != null) r.Value.color = fg;
            }
            if (r.LabelText != label) { r.LabelText = label; r.Label.text = label; }
            if (r.Value != null && r.ValueText != value) { r.ValueText = value; r.Value.text = value ?? ""; }
        }
    }
}
