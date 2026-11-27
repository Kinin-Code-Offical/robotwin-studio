using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace RobotWin.Studio.Firmware
{
    /// <summary>
    /// [crossprobe] Which board pin the code under the caret means (the roadmap's "kod satırı ↔ fiziksel parça"): a number
    /// or an A-pin given to a pin call (<c>digitalWrite(9, …)</c>, <c>analogRead(A0)</c>, <c>servo.attach(5)</c>), or a name
    /// that stands for one (<c>#define LED 13</c>, <c>const int TRIG = 9;</c>) wherever the caret is on it: at its
    /// definition or where it is used. A name counts as a pin only when the sketch gives it to a pin call somewhere, so
    /// <c>int speed = 9</c> is not taken for pin 9.
    /// </summary>
    public static class PinProbe
    {
        /// <summary>Calls whose first argument is a pin (Arduino, ESP32's LEDC / touch / DAC, Servo's attach).</summary>
        private static readonly string[] PinCalls =
        {
            "pinMode", "digitalWrite", "digitalRead", "analogRead", "analogWrite", "tone", "noTone", "pulseIn", "pulseInLong",
            "attach", "ledcAttachPin", "ledcAttach", "ledcWrite", "touchRead", "dacWrite", "digitalPinToInterrupt", "analogReadMilliVolts"
        };

        private static readonly Regex CallArg = new Regex(@"\b(" + string.Join("|", PinCalls) + @")\s*\(\s*([A-Za-z_]\w*|\d+)", RegexOptions.Compiled);
        private static readonly Regex Define = new Regex(@"^\s*#\s*define\s+([A-Za-z_]\w*)\s+\(?\s*(A?\d+)\s*\)?\s*(?://.*)?$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex Constant = new Regex(@"\b(?:static\s+)?(?:const(?:expr)?\s+)?(?:unsigned\s+)?(?:int|byte|uint8_t|uint16_t|int8_t|int16_t|short|long|gpio_num_t)\s+(?:const\s+)?([A-Za-z_]\w*)\s*=\s*(?:\(\s*gpio_num_t\s*\)\s*)?(A?\d+)\s*[;,]", RegexOptions.Compiled);

        /// <summary>
        /// The pin the caret is on ("9", "A0"), or null: the token under the caret, read as a number given to a pin call or
        /// as a name the sketch gives to one.
        /// </summary>
        public static string? PinAt(string text, int caret)
        {
            if (string.IsNullOrEmpty(text)) return null;
            caret = Math.Max(0, Math.Min(caret, text.Length));
            int start = caret, end = caret;
            while (start > 0 && IsWord(text[start - 1])) start--;
            while (end < text.Length && IsWord(text[end])) end++;
            if (end <= start) return null;
            string token = text.Substring(start, end - start);

            var pinNames = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match m in CallArg.Matches(text)) pinNames.Add(m.Groups[2].Value);

            if (IsPinLiteral(token))
            {
                // a literal counts only as a pin call's own argument, on this line
                int lineStart = text.LastIndexOf('\n', Math.Max(0, start - 1)) + 1;
                if (start == 0) lineStart = 0;
                int lineEnd = text.IndexOf('\n', end);
                string line = text.Substring(lineStart, (lineEnd < 0 ? text.Length : lineEnd) - lineStart);
                int at = start - lineStart;
                foreach (Match m in CallArg.Matches(line))
                    if (m.Groups[2].Index == at && m.Groups[2].Length == token.Length) return token;
                return null;
            }

            if (!pinNames.Contains(token)) return null;
            return ValueOf(text, token);
        }

        /// <summary>What a name stands for ("13", "A0") from its #define or constant; null when it has no plain value.</summary>
        public static string? ValueOf(string text, string name)
        {
            foreach (Match m in Define.Matches(text))
                if (m.Groups[1].Value == name) return m.Groups[2].Value;
            foreach (Match m in Constant.Matches(text))
                if (m.Groups[1].Value == name) return m.Groups[2].Value;
            return null;
        }

        /// <summary>
        /// The board's pin names a pin number may go by, in the order to try: "D9"/"9" on an AVR board (pins 14 and up are
        /// A0 … on an Uno or Nano), "IO9"/"GPIO9"/"GP9"/"D9" on an ESP32 or RP2040; an A-pin as itself.
        /// </summary>
        public static IEnumerable<string> BoardPinNames(string pin)
        {
            if (pin.StartsWith("A", StringComparison.Ordinal))
            {
                yield return pin;
                yield break;
            }

            yield return "D" + pin;
            yield return "IO" + pin;
            yield return "GPIO" + pin;
            yield return "GP" + pin;
            yield return "G" + pin;
            yield return pin;
            if (int.TryParse(pin, out int n) && n >= 14 && n <= 21) yield return "A" + (n - 14);   // Uno / Nano: 14 is A0
        }

        private static bool IsPinLiteral(string token) =>
            token.All(char.IsDigit) || (token.Length > 1 && token[0] == 'A' && token.Skip(1).All(char.IsDigit));

        private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
    }
}
