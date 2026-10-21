using System;
using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using MelonLoader.Preferences;

namespace ModConfigurator
{
    internal enum EntryKind { Bool, Number, Choice, KeyCapture, ButtonCapture, Text }

    /// <summary>One editable MelonPreferences entry.</summary>
    internal sealed class EntryVm
    {
        internal readonly MelonPreferences_Entry Entry;
        internal readonly CategoryVm Owner;
        internal readonly EntryKind Kind;
        internal readonly Type ValueType;
        internal readonly string Name;
        internal readonly string Description;

        private readonly double _min = double.NegativeInfinity, _max = double.PositiveInfinity;
        private readonly double _step;
        private readonly bool _isInt;

        internal EntryVm(CategoryVm owner, MelonPreferences_Entry entry)
        {
            Owner = owner;
            Entry = entry;
            ValueType = entry.GetReflectedType();
            Name = !string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.DisplayName : entry.Identifier;
            Description = entry.Description ?? "";

            var t = ValueType;
            if (t == typeof(bool)) Kind = EntryKind.Bool;
            else if (t.IsEnum) Kind = EntryKind.Choice;
            else if (t == typeof(float) || t == typeof(double) || t == typeof(int) || t == typeof(long) || t == typeof(short) || t == typeof(byte) || t == typeof(uint))
                Kind = EntryKind.Number;
            else if (t == typeof(string))
            {
                string id = entry.Identifier ?? "";
                if (id.EndsWith("Key", StringComparison.OrdinalIgnoreCase) || id.EndsWith("Hotkey", StringComparison.OrdinalIgnoreCase)) Kind = EntryKind.KeyCapture;
                else if (id.IndexOf("Button", StringComparison.OrdinalIgnoreCase) >= 0) Kind = EntryKind.ButtonCapture;
                else Kind = EntryKind.Text;
            }
            else Kind = EntryKind.Text;

            if (Kind == EntryKind.Number)
            {
                _isInt = !(t == typeof(float) || t == typeof(double));
                if (entry.Validator is IValueRange r)
                {
                    try { _min = Convert.ToDouble(r.MinValue, CultureInfo.InvariantCulture); _max = Convert.ToDouble(r.MaxValue, CultureInfo.InvariantCulture); }
                    catch { /* no usable range */ }
                }
                _step = ComputeStep();
            }
        }

        private double ComputeStep()
        {
            if (_isInt)
            {
                if (!double.IsInfinity(_min) && !double.IsInfinity(_max) && _max - _min > 100) return Math.Max(1, Math.Round((_max - _min) / 50));
                return 1;
            }
            if (!double.IsInfinity(_min) && !double.IsInfinity(_max)) return NiceStep((_max - _min) / 50.0);

            double.TryParse(Entry.GetDefaultValueAsString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double def);
            double m = Math.Max(Math.Abs(def), Math.Abs(AsDouble()));
            if (m < 1e-9) return 0.1;
            return NiceStep(m / 25.0);
        }

        private static double NiceStep(double raw)
        {
            if (raw <= 0) return 0.01;
            double pow = Math.Pow(10, Math.Floor(Math.Log10(raw)));
            double f = raw / pow;
            double nice = f < 1.5 ? 1 : f < 3.5 ? 2 : f < 7.5 ? 5 : 10;
            return Math.Max(0.001, nice * pow);
        }

        private double AsDouble() => Convert.ToDouble(Entry.BoxedValue, CultureInfo.InvariantCulture);

        internal bool IsDefault => Entry.GetValueAsString() == Entry.GetDefaultValueAsString();

        internal string ValueText()
        {
            try
            {
                switch (Kind)
                {
                    case EntryKind.Bool: return (bool)Entry.BoxedValue ? "ON" : "OFF";
                    case EntryKind.Number:
                        return _isInt ? AsDouble().ToString("0", CultureInfo.InvariantCulture)
                                      : AsDouble().ToString(_step < 0.01 ? "0.000" : "0.00", CultureInfo.InvariantCulture);
                    default:
                        string s = Convert.ToString(Entry.BoxedValue, CultureInfo.InvariantCulture);
                        return string.IsNullOrEmpty(s) ? "-" : s;
                }
            }
            catch { return "?"; }
        }

        /// <summary>Left/right adjustment. <paramref name="mult"/> speeds up numeric steps while a direction is held.</summary>
        internal void Step(int dir, float mult = 1f)
        {
            try
            {
                switch (Kind)
                {
                    case EntryKind.Bool: Set(!(bool)Entry.BoxedValue); break;
                    case EntryKind.Number:
                    {
                        double v = AsDouble() + dir * _step * mult;
                        v = Math.Round(v / _step) * _step;
                        v = Math.Min(_max, Math.Max(_min, v));
                        Set(Convert.ChangeType(Math.Round(v, 6), ValueType, CultureInfo.InvariantCulture));
                        break;
                    }
                    case EntryKind.Choice:
                    {
                        var names = Enum.GetNames(ValueType);
                        int i = Array.IndexOf(names, Entry.BoxedValue.ToString());
                        i = (i + dir + names.Length) % names.Length;
                        Set(Enum.Parse(ValueType, names[i]));
                        break;
                    }
                }
            }
            catch (Exception e) { Mod.Log.Warning($"Could not change {Owner.Name}/{Name}: {e.Message}"); }
        }

        // ---- helpers for option-list style editors (the native settings page) ----

        internal bool HasRange => !double.IsInfinity(_min) && !double.IsInfinity(_max);
        internal bool IsInt => _isInt;

        /// <summary>Selectable values for a numeric entry: the whole range (at most <paramref name="maxCount"/> steps),
        /// or - for unbounded entries - a window of steps around the current value.</summary>
        internal List<double> NumberChoices(int maxCount, out int currentIndex)
        {
            var list = new List<double>();
            double cur = AsDouble();
            if (HasRange)
            {
                double step = _step;
                while ((_max - _min) / step + 1 > maxCount) step *= 2;
                for (double v = _min; v <= _max + step * 0.001; v += step) list.Add(Math.Round(v, 6));
                if (list.Count == 0 || list[list.Count - 1] < _max - 1e-9) list.Add(_max);
            }
            else
            {
                int half = maxCount / 2;
                for (int k = -half; k <= half; k++) list.Add(Math.Round(cur + k * _step, 6));
            }
            currentIndex = 0;
            double best = double.MaxValue;
            for (int i = 0; i < list.Count; i++)
            {
                double d = Math.Abs(list[i] - cur);
                if (d < best) { best = d; currentIndex = i; }
            }
            return list;
        }

        internal string FormatNumber(double v) =>
            _isInt ? v.ToString("0", CultureInfo.InvariantCulture) : v.ToString(_step < 0.01 ? "0.000" : "0.00", CultureInfo.InvariantCulture);

        internal void SetNumber(double v)
        {
            try { Set(Convert.ChangeType(v, ValueType, CultureInfo.InvariantCulture)); }
            catch (Exception e) { Mod.Log.Warning($"Could not set {Owner.Name}/{Name}: {e.Message}"); }
        }

        internal string[] ChoiceNames => Kind == EntryKind.Choice ? Enum.GetNames(ValueType) : new string[0];

        internal void SetChoice(int index)
        {
            try { var names = Enum.GetNames(ValueType); Set(Enum.Parse(ValueType, names[Math.Max(0, Math.Min(names.Length - 1, index))])); }
            catch (Exception e) { Mod.Log.Warning($"Could not set {Owner.Name}/{Name}: {e.Message}"); }
        }

        internal void SetBool(bool value)
        {
            try { Set(value); }
            catch (Exception e) { Mod.Log.Warning($"Could not set {Owner.Name}/{Name}: {e.Message}"); }
        }

        internal void SetText(string value)
        {
            try { Set(value); }
            catch (Exception e) { Mod.Log.Warning($"Could not set {Owner.Name}/{Name}: {e.Message}"); }
        }

        internal void Reset()
        {
            try { Entry.ResetToDefault(); Owner.Dirty = true; }
            catch (Exception e) { Mod.Log.Warning($"Could not reset {Owner.Name}/{Name}: {e.Message}"); }
        }

        private void Set(object value)
        {
            Entry.BoxedValue = value;
            Owner.Dirty = true;
            Owner.LastChange = DateTime.UtcNow;
        }
    }

    internal sealed class CategoryVm
    {
        internal readonly MelonPreferences_Category Category;
        internal readonly string Name;
        internal readonly List<EntryVm> Entries = new List<EntryVm>();
        internal bool Dirty;
        internal DateTime LastChange;

        internal CategoryVm(MelonPreferences_Category cat)
        {
            Category = cat;
            Name = !string.IsNullOrWhiteSpace(cat.DisplayName) ? cat.DisplayName : cat.Identifier;
        }

        internal void Save()
        {
            if (!Dirty) return;
            try { Category.SaveToFile(false); Dirty = false; }
            catch (Exception e) { Mod.Log.Warning($"Could not save '{Name}': {e.Message}"); }
        }
    }

    internal static class ConfigModel
    {
        /// <summary>Snapshot of all visible categories of all loaded mods.</summary>
        internal static List<CategoryVm> Discover()
        {
            var hidden = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in (Settings.HiddenCategories.Value ?? "").Split(','))
                if (!string.IsNullOrWhiteSpace(s)) hidden.Add(s.Trim());

            var list = new List<CategoryVm>();
            foreach (var cat in MelonPreferences.Categories)
            {
                if (cat == null || cat.IsHidden || hidden.Contains(cat.Identifier)) continue;
                var vm = new CategoryVm(cat);
                foreach (var e in cat.Entries)
                {
                    if (e == null || e.IsHidden) continue;
                    try { vm.Entries.Add(new EntryVm(vm, e)); }
                    catch (Exception ex) { Mod.Log.Warning($"Skipping {cat.Identifier}/{e.Identifier}: {ex.Message}"); }
                }
                if (vm.Entries.Count > 0) list.Add(vm);
            }
            list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            return list;
        }
    }
}
