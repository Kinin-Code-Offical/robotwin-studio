using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace RobotWin.Studio.Shell
{
    /// <summary>[keys] A command the window binds to keys the user can change (Ayarlar → Klavye kısayolları).</summary>
    public sealed class ShortcutCommand
    {
        internal ShortcutCommand(string id, string group, string title, string defaults, Action? action, string? page = null)
        {
            Id = id;
            Group = group;
            Title = title;
            Defaults = defaults;
            Action = action;
            Page = page;
        }

        /// <summary>The page a page command works on ("devre", "pcb"); null for a window-wide command.</summary>
        public string? Page { get; }

        public string Id { get; }
        public string Group { get; }
        public string Title { get; }

        /// <summary>The default keys ("Ctrl+K, Ctrl+Shift+P": alternatives separated by commas).</summary>
        public string Defaults { get; }

        /// <summary>What a window-wide command does; null for a page command (its page runs it from its own keys).</summary>
        public Action? Action { get; }

        /// <summary>The keys in use: the user's choice, else the defaults ("" = no keys).</summary>
        public string Keys => AppSettings.Current.KeyBindings.TryGetValue(Id, out var k) ? k : Defaults;

        public bool IsChanged => AppSettings.Current.KeyBindings.ContainsKey(Id);
    }

    /// <summary>
    /// [keys] The window's rebindable shortcuts: registered once with their defaults, changed in the settings (kept in
    /// <see cref="AppSettings.KeyBindings"/>), checked for clashes, and turned into key bindings by the window.
    /// </summary>
    internal static class Shortcuts
    {
        private static readonly List<ShortcutCommand> Commands = new List<ShortcutCommand>();

        public static IReadOnlyList<ShortcutCommand> All => Commands;

        /// <summary>A shortcut changed: the window rebuilds its bindings, the settings page its list.</summary>
        public static event Action? Changed;

        public static ShortcutCommand Register(string id, string group, string title, string defaults, Action action)
        {
            Commands.RemoveAll(c => c.Id == id);
            var c = new ShortcutCommand(id, group, title, defaults, action);
            Commands.Add(c);
            return c;
        }

        /// <summary>
        /// A key of one page (a schematic tool, a PCB action): its page keeps handling its default key, and
        /// <see cref="Translate"/> turns the key the user chose into that default before the page sees it.
        /// </summary>
        public static ShortcutCommand RegisterPage(string page, string id, string group, string title, string defaults)
        {
            Commands.RemoveAll(c => c.Id == id);
            var c = new ShortcutCommand(id, group, title, defaults, null, page);
            Commands.Add(c);
            return c;
        }

        /// <summary>
        /// [keys] A key pressed on <paramref name="page"/>, as the page knows it: a key the user gave one of the page's
        /// commands becomes that command's default key; a default key the user moved elsewhere does nothing (false);
        /// any other key is left as it is.
        /// </summary>
        public static bool Translate(string page, ref Key key, ref ModifierKeys mods)
        {
            var pressed = Format(key, mods);
            ShortcutCommand? moved = null;
            foreach (var c in Commands)
            {
                if (c.Page != page || !c.IsChanged) continue;
                if (Gestures(c.Keys).Any(g => string.Equals(Format(g.Key, g.Modifiers), pressed, StringComparison.OrdinalIgnoreCase)))
                {
                    var target = Gestures(c.Defaults).FirstOrDefault();
                    if (target.Key == Key.None) return false;
                    key = target.Key;
                    mods = target.Modifiers;
                    return true;
                }

                if (Gestures(c.Defaults).Any(g => string.Equals(Format(g.Key, g.Modifiers), pressed, StringComparison.OrdinalIgnoreCase))) moved = c;
            }

            return moved == null;
        }

        public static void Set(string id, string keys)
        {
            var c = Commands.FirstOrDefault(x => x.Id == id);
            if (c == null) return;
            if (string.Equals(Normalize(keys), Normalize(c.Defaults), StringComparison.OrdinalIgnoreCase)) AppSettings.Current.KeyBindings.Remove(id);
            else AppSettings.Current.KeyBindings[id] = keys;
            AppSettings.Current.Save();
            Changed?.Invoke();
        }

        public static void Reset(string id)
        {
            if (!AppSettings.Current.KeyBindings.Remove(id)) return;
            AppSettings.Current.Save();
            Changed?.Invoke();
        }

        /// <summary>The settings were replaced (imported): bind the keys again.</summary>
        public static void Reapply() => Changed?.Invoke();

        public static void ResetAll()
        {
            AppSettings.Current.KeyBindings.Clear();
            AppSettings.Current.Save();
            Changed?.Invoke();
        }

        /// <summary>The other commands that use one of <paramref name="c"/>'s keys.</summary>
        public static IEnumerable<ShortcutCommand> Clashes(ShortcutCommand c)
        {
            var mine = Gestures(c.Keys).Select(g => Format(g.Key, g.Modifiers)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (mine.Count == 0) return Array.Empty<ShortcutCommand>();
            // A page's keys only meet the same page's keys (the window's take keys with modifiers, the pages' mostly plain ones).
            return Commands.Where(o => o != c && o.Page == c.Page && Gestures(o.Keys).Any(g => mine.Contains(Format(g.Key, g.Modifiers))));
        }

        /// <summary>The key chords of a keys text ("Ctrl+K, Ctrl+Shift+P"); unreadable parts are skipped.</summary>
        public static List<(Key Key, ModifierKeys Modifiers)> Gestures(string keys)
        {
            var list = new List<(Key, ModifierKeys)>();
            foreach (var part in (keys ?? string.Empty).Split(','))
            {
                if (TryParse(part.Trim(), out var k, out var m)) list.Add((k, m));
            }

            return list;
        }

        private static string Normalize(string keys) => string.Join(", ", Gestures(keys).Select(g => Format(g.Key, g.Modifiers)));

        private static readonly (Key Key, string Text)[] Names =
        {
            (Key.OemComma, ","), (Key.OemPeriod, "."), (Key.OemPlus, "+"), (Key.OemMinus, "-"), (Key.Oem5, "\\"), (Key.OemBackslash, "<"),
            (Key.Oem2, "/"), (Key.Oem1, ";"), (Key.Oem7, "'"), (Key.OemOpenBrackets, "["), (Key.Oem6, "]"), (Key.Oem3, "`"),
            (Key.Return, "Enter"), (Key.Escape, "Esc"), (Key.Back, "Backspace"), (Key.Delete, "Del"), (Key.Insert, "Ins"),
            (Key.PageUp, "PageUp"), (Key.PageDown, "PageDown"),
        };

        public static string Format(Key key, ModifierKeys mods)
        {
            var sb = new StringBuilder();
            if ((mods & ModifierKeys.Control) != 0) sb.Append("Ctrl+");
            if ((mods & ModifierKeys.Shift) != 0) sb.Append("Shift+");
            if ((mods & ModifierKeys.Alt) != 0) sb.Append("Alt+");
            if ((mods & ModifierKeys.Windows) != 0) sb.Append("Win+");
            var named = Names.FirstOrDefault(n => n.Key == key);
            if (named.Text != null) sb.Append(named.Text);
            else if (key >= Key.D0 && key <= Key.D9) sb.Append((char)('0' + (key - Key.D0)));
            else if (key >= Key.NumPad0 && key <= Key.NumPad9) sb.Append("Num").Append((char)('0' + (key - Key.NumPad0)));
            else sb.Append(key.ToString());
            return sb.ToString();
        }

        public static bool TryParse(string text, out Key key, out ModifierKeys mods)
        {
            key = Key.None;
            mods = ModifierKeys.None;
            if (string.IsNullOrWhiteSpace(text)) return false;
            // the last '+' that has something before it separates the key ("Ctrl++" is Ctrl and '+')
            string rest = text.Trim();
            while (true)
            {
                int plus = rest.IndexOf('+');
                if (plus <= 0 || plus == rest.Length - 1) break;
                string mod = rest.Substring(0, plus).Trim();
                if (mod.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || mod.Equals("Control", StringComparison.OrdinalIgnoreCase)) mods |= ModifierKeys.Control;
                else if (mod.Equals("Shift", StringComparison.OrdinalIgnoreCase)) mods |= ModifierKeys.Shift;
                else if (mod.Equals("Alt", StringComparison.OrdinalIgnoreCase)) mods |= ModifierKeys.Alt;
                else if (mod.Equals("Win", StringComparison.OrdinalIgnoreCase)) mods |= ModifierKeys.Windows;
                else break;
                rest = rest.Substring(plus + 1);
            }

            rest = rest.Trim();
            var named = Names.FirstOrDefault(n => string.Equals(n.Text, rest, StringComparison.OrdinalIgnoreCase));
            if (named.Text != null)
            {
                key = named.Key;
                return true;
            }

            if (rest.Length == 1 && rest[0] >= '0' && rest[0] <= '9')
            {
                key = Key.D0 + (rest[0] - '0');
                return true;
            }

            if (rest.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && rest.Length == 4 && char.IsDigit(rest[3]))
            {
                key = Key.NumPad0 + (rest[3] - '0');
                return true;
            }

            return Enum.TryParse(rest, true, out key) && key != Key.None;
        }
    }
}
