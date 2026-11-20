using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Libraries
{
    // [libs] Shared diagnostics of the external-library importers and the part creator.

    public enum IssueSeverity
    {
        /// <summary>Informational: what was done, how something was mapped.</summary>
        Info,

        /// <summary>Imported, but with an approximation or something left out.</summary>
        Warning,

        /// <summary>The item (or the line) could not be used.</summary>
        Error
    }

    /// <summary>One finding of an importer or validator, with its location when known.</summary>
    public sealed class ImportIssue
    {
        public ImportIssue(IssueSeverity severity, string message, string? file = null, int line = 0, string? item = null)
        {
            Severity = severity;
            Message = message ?? string.Empty;
            File = file;
            Line = line;
            Item = item;
        }

        public IssueSeverity Severity { get; }
        public string Message { get; }

        /// <summary>Source file (name only is enough for display).</summary>
        public string? File { get; }

        /// <summary>1-based line number, 0 when not applicable.</summary>
        public int Line { get; }

        /// <summary>The part / model / pin the finding is about.</summary>
        public string? Item { get; }

        public string SeverityText => Severity switch
        {
            IssueSeverity.Error => "HATA",
            IssueSeverity.Warning => "uyarı",
            _ => "bilgi"
        };

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(SeverityText).Append(": ");
            if (!string.IsNullOrEmpty(File))
            {
                sb.Append(System.IO.Path.GetFileName(File));
                if (Line > 0) sb.Append(':').Append(Line.ToString(CultureInfo.InvariantCulture));
                sb.Append(": ");
            }
            else if (Line > 0)
            {
                sb.Append("satır ").Append(Line.ToString(CultureInfo.InvariantCulture)).Append(": ");
            }

            if (!string.IsNullOrEmpty(Item)) sb.Append('[').Append(Item).Append("] ");
            sb.Append(Message);
            return sb.ToString();
        }
    }

    /// <summary>An ordered list of findings with helpers for the common cases.</summary>
    public sealed class ImportLog
    {
        public List<ImportIssue> Issues { get; } = new List<ImportIssue>();

        public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);

        public int ErrorCount => Issues.Count(i => i.Severity == IssueSeverity.Error);

        public int WarningCount => Issues.Count(i => i.Severity == IssueSeverity.Warning);

        public void Info(string message, string? file = null, int line = 0, string? item = null) =>
            Issues.Add(new ImportIssue(IssueSeverity.Info, message, file, line, item));

        public void Warn(string message, string? file = null, int line = 0, string? item = null) =>
            Issues.Add(new ImportIssue(IssueSeverity.Warning, message, file, line, item));

        public void Error(string message, string? file = null, int line = 0, string? item = null) =>
            Issues.Add(new ImportIssue(IssueSeverity.Error, message, file, line, item));

        public void AddRange(IEnumerable<ImportIssue> issues) => Issues.AddRange(issues);

        public IEnumerable<ImportIssue> For(string item) =>
            Issues.Where(i => string.Equals(i.Item, item, StringComparison.OrdinalIgnoreCase));

        public string Summary() =>
            $"{ErrorCount} hata, {WarningCount} uyarı, {Issues.Count - ErrorCount - WarningCount} bilgi";

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var i in Issues) sb.Append(i).Append('\n');
            return sb.ToString();
        }
    }

    internal static class Inv
    {
        public static string S(double v) => v.ToString("R", CultureInfo.InvariantCulture);

        public static string G(double v, int digits = 6) => v.ToString("G" + digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);

        public static string Mm(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
