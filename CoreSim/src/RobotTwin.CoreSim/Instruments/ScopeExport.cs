using System.Globalization;
using System.Text;

namespace RobotTwin.CoreSim.Instruments
{
    /// <summary>
    /// [scope] The screen's samples as CSV: a time column (seconds from the start of the run) and one column per trace on the
    /// screen, the math trace last, each headed by its channel, the name the user gave it and its unit
    /// ("CH1 Motor akımı (A)"); invariant numbers with a dot, so any spreadsheet or script reads them.
    /// </summary>
    public static class ScopeExport
    {
        public static string ToCsv(ScopeFrame frame)
        {
            var sb = new StringBuilder();
            var columns = new System.Collections.Generic.List<ScopeTrace>(frame.Traces);
            if (frame.Math != null) columns.Add(frame.Math);
            sb.Append("t (s)");
            foreach (var t in columns)
            {
                string name = t.Channel != null
                    ? t.Channel.Name + (t.Channel.Title.Length > 0 ? " " + t.Channel.Title : string.Empty)
                    : "M " + t.Name;
                sb.Append(',').Append(Quote($"{name} ({t.Unit})"));
            }

            sb.Append('\n');
            for (int i = 0; i < frame.Length; i++)
            {
                sb.Append(frame.TimeAt(i).ToString("R", CultureInfo.InvariantCulture));
                foreach (var t in columns)
                {
                    sb.Append(',');
                    if (i < t.Values.Length && !double.IsNaN(t.Values[i])) sb.Append(t.Values[i].ToString("R", CultureInfo.InvariantCulture));
                }

                sb.Append('\n');
            }

            return sb.ToString();
        }

        private static string Quote(string s) => s.IndexOfAny(new[] { ',', '"', '\n' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";
    }
}
