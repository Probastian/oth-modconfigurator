using System;
using System.IO;
using System.Text;
using Il2CppTMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigurator
{
    /// <summary>
    /// Writes the hierarchy of a UI object (names, components, sizes, colours, texts) to UserData\OTH-ui-settings-dump.txt.
    /// Used to see how the game's own settings pages are built, so the native "Mods" page can be matched to them.
    /// </summary>
    internal static class UiDump
    {
        private const string FileName = "OTH-ui-settings-dump.txt";
        private static bool _fresh = true;

        internal static void Dump(string title, Transform root, int maxDepth = 9, int maxNodes = 450)
        {
            try
            {
                if (root == null) return;
                var sb = new StringBuilder();
                sb.AppendLine($"===== {title}  ({DateTime.Now:HH:mm:ss}) root='{root.name}' =====");
                int count = 0;
                Walk(root, 0, maxDepth, sb, ref count, maxNodes);
                if (count >= maxNodes) sb.AppendLine("... (truncated)");
                sb.AppendLine();

                string path = Path.Combine("UserData", FileName);
                if (_fresh) { File.WriteAllText(path, sb.ToString()); _fresh = false; }
                else File.AppendAllText(path, sb.ToString());
                Mod.Log.Msg($"UI hierarchy '{title}' written to UserData\\{FileName} ({count} nodes)");
            }
            catch (Exception e) { Mod.Log.Warning($"UiDump failed: {e.Message}"); }
        }

        private static void Walk(Transform t, int depth, int maxDepth, StringBuilder sb, ref int count, int maxNodes)
        {
            if (t == null || count >= maxNodes) return;
            count++;
            var line = new StringBuilder();
            line.Append(' ', depth * 2).Append(t.name).Append(t.gameObject.activeSelf ? "" : " [inactive]");

            var parts = new StringBuilder();
            try
            {
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) continue;
                    string n = c.GetIl2CppType().Name;
                    if (n == "Transform" || n == "CanvasRenderer") continue;
                    parts.Append(' ').Append(n);
                    var rt = c.TryCast<RectTransform>();
                    if (rt != null) parts.Append($"({rt.sizeDelta.x:0}x{rt.sizeDelta.y:0} pos {rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0})");
                    var img = c.TryCast<Image>();
                    if (img != null) parts.Append($"(color {img.color} sprite {(img.sprite != null ? img.sprite.name : "-")} {img.type})");
                    var tmp = c.TryCast<TMP_Text>();
                    if (tmp != null) parts.Append($"(\"{Trunc(tmp.text)}\" size {tmp.fontSize:0.#} font {(tmp.font != null ? tmp.font.name : "-")} color {tmp.color})");
                    var sel = c.TryCast<Selectable>();
                    if (sel != null) parts.Append($"(nav {sel.navigation.mode})");
                    var vlg = c.TryCast<HorizontalOrVerticalLayoutGroup>();
                    if (vlg != null) parts.Append($"(spacing {vlg.spacing:0})");
                }
            }
            catch (Exception e) { parts.Append(" <err ").Append(e.Message).Append('>'); }

            sb.AppendLine(line.Append(" :").Append(parts).ToString());
            if (depth >= maxDepth) return;
            for (int i = 0; i < t.childCount; i++) Walk(t.GetChild(i), depth + 1, maxDepth, sb, ref count, maxNodes);
        }

        private static string Trunc(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\n", " ");
            return s.Length > 40 ? s.Substring(0, 40) + "..." : s;
        }
    }
}
