using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ModConfigurator
{
    /// <summary>
    /// "Press the new key / button" capture for key and gamepad-button settings. Shared by the native settings page and the
    /// overlay window. Esc cancels (Start cancels a gamepad capture), Backspace/Delete clears to "None", 10 s timeout.
    /// </summary>
    internal static class Capture
    {
        internal static EntryVm Target { get; private set; }
        private static float _until;
        private static int _startFrame;

        internal static bool Active => Target != null;

        internal static void Begin(EntryVm entry)
        {
            Target = entry;
            _until = Time.realtimeSinceStartup + 10f;
            _startFrame = Time.frameCount;
        }

        internal static void Cancel() => Target = null;

        internal static string Prompt =>
            Target == null ? "" : Target.Kind == EntryKind.KeyCapture ? "Press a key..." : "Press a button...";

        /// <summary>Polls the devices; returns true on the frame the capture finished (new value set, or cancelled).</summary>
        internal static bool Tick()
        {
            var e = Target;
            if (e == null) return false;
            if (Time.frameCount == _startFrame) return false;     // ignore the press that started the capture

            var kb = Keyboard.current;
            bool cancel = Time.realtimeSinceStartup > _until || (kb != null && kb.escapeKey.wasPressedThisFrame);
            if (cancel) { Target = null; return true; }

            bool clear = kb != null && (kb.backspaceKey.wasPressedThisFrame || kb.deleteKey.wasPressedThisFrame);
            if (clear) { e.SetText("None"); Target = null; return true; }

            if (e.Kind == EntryKind.KeyCapture && kb != null)
            {
                foreach (var v in Enum.GetValues(typeof(Key)))
                {
                    var k = (Key)v;
                    if (k == Key.None || k == Key.IMESelected) continue;
                    var ctrl = kb[k];
                    if (ctrl != null && ctrl.wasPressedThisFrame) { e.SetText(k.ToString()); Target = null; return true; }
                }
            }
            else if (e.Kind == EntryKind.ButtonCapture)
            {
                var all = Gamepad.all;
                for (int i = 0; i < all.Count; i++)
                {
                    var pad = all[i];
                    if (pad == null) continue;
                    if (pad.startButton.wasPressedThisFrame) { Target = null; return true; }
                    foreach (var v in Enum.GetValues(typeof(GamepadButton)))
                    {
                        var b = (GamepadButton)v;
                        var ctrl = pad[b];
                        if (ctrl != null && ctrl.wasPressedThisFrame) { e.SetText(b.ToString()); Target = null; return true; }
                    }
                }
            }
            return false;
        }
    }
}
