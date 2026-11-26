using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [units] The length unit the editors show and take (Settings → Birimler): millimetre, centimetre, inch or mil. Values
    /// stay in millimetres underneath; a box shows them in the chosen unit and reads a typed one in any unit written after
    /// the number ("25.4 mm", "1 in", "1\"", "2,5 cm", "40 mil"); a bare number is in the chosen unit.
    /// </summary>
    public static class LengthUnits
    {
        public static readonly (string Key, string Name)[] All =
        {
            ("mm", "Milimetre (mm)"), ("cm", "Santimetre (cm)"), ("in", "İnç (in)"), ("mil", "Mil (1/1000 inç)")
        };

        private static readonly Regex Typed = new Regex(@"^\s*(?<n>.*?)\s*(?<u>mm|cm|m|in|inç|inch|""|mil|thou)?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>The chosen unit's key ("mm", "cm", "in", "mil").</summary>
        public static string Current
        {
            get
            {
                string u = AppSettings.Current.Units;
                return u is "cm" or "in" or "mil" ? u : "mm";
            }
        }

        /// <summary>Raised when the chosen unit changes (boxes show their values again).</summary>
        public static event EventHandler? Changed;

        private static string _last = "mm";

        static LengthUnits()
        {
            _last = Current;
            AppSettings.Changed += (_, _) =>
            {
                if (Current == _last) return;
                _last = Current;
                Changed?.Invoke(null, EventArgs.Empty);
            };
        }

        /// <summary>How many of a unit make a millimetre.</summary>
        public static double PerMm(string unit) => unit switch
        {
            "cm" => 0.1,
            "m" => 0.001,
            "in" => 1 / 25.4,
            "mil" => 1 / 0.0254,
            _ => 1
        };

        public static double ToShown(double mm) => mm * PerMm(Current);

        public static double FromShown(double v) => v / PerMm(Current);

        /// <summary>The unit's suffix as shown after a value (" mm", " in" …).</summary>
        public static string Suffix => " " + Current;

        /// <summary>Decimals worth showing in a unit for a millimetre field shown to <paramref name="mmDecimals"/>.</summary>
        public static int Decimals(int mmDecimals = 2) => Current switch
        {
            "cm" => mmDecimals + 1,
            "in" => mmDecimals + 2,
            "mil" => Math.Max(0, mmDecimals - 1),
            _ => mmDecimals
        };

        /// <summary>A length in the chosen unit with its suffix ("12.5 mm", "0.492 in").</summary>
        public static string Format(double mm, int mmDecimals = 2, IFormatProvider? culture = null) =>
            ToShown(mm).ToString("0." + new string('#', Math.Max(1, Decimals(mmDecimals))), culture ?? CultureInfo.CurrentCulture) + Suffix;

        /// <summary>
        /// Reads a typed length into millimetres: a number (or arithmetic, through <paramref name="eval"/>) and an optional
        /// unit after it; without one, the chosen unit.
        /// </summary>
        public static bool TryParse(string text, out double mm, Func<string, (bool Ok, double Value)>? eval = null)
        {
            mm = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var m = Typed.Match(text.Trim());
            if (!m.Success) return false;
            string number = m.Groups["n"].Value.Trim();
            string unit = m.Groups["u"].Success && m.Groups["u"].Value.Length > 0 ? Normalise(m.Groups["u"].Value) : Current;
            double v;
            if (eval != null)
            {
                var (ok, value) = eval(number);
                if (!ok) return false;
                v = value;
            }
            else if (!double.TryParse(number.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v))
            {
                return false;
            }

            mm = v / PerMm(unit);
            return !double.IsNaN(mm) && !double.IsInfinity(mm);
        }

        private static string Normalise(string unit) => unit.ToLowerInvariant() switch
        {
            "inç" or "inch" or "\"" => "in",
            "thou" => "mil",
            var u => u
        };
    }
}
