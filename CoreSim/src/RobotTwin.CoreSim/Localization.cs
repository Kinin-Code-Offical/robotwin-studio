using System;

namespace RobotTwin.CoreSim
{
    /// <summary>
    /// [lang] Texts the simulator writes into files people read (a BOM's columns, a report's headings) in the
    /// interface's language. The simulator itself speaks Turkish; the Studio sets <see cref="Translate"/> to its language
    /// pack at startup, and messages shown on screen are translated there anyway. Unset, a text stays as written.
    /// </summary>
    public static class Localization
    {
        /// <summary>Turkish text → the text in the language in use.</summary>
        public static Func<string, string> Translate { get; set; } = s => s;

        public static string T(string text) => string.IsNullOrEmpty(text) ? text : Translate(text);
    }
}
