using System.Windows;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [tokens] The type scale from code: each step read from the theme's tokens (Theme/Tokens.xaml, T.Caption … T.Hero),
    /// so a size set in C# follows the same scale as the XAML (and a theme that changes it). The numbers are the fallbacks.
    /// </summary>
    public static class TypeScale
    {
        private static double Of(string key, double fallback) =>
            Application.Current?.TryFindResource(key) is double d ? d : fallback;

        /// <summary>Notes, units, table cells, status lines (12).</summary>
        public static double Caption => Of("T.Caption", 12);

        /// <summary>Running text (13).</summary>
        public static double Body => Of("T.Body", 13);

        /// <summary>A panel's or a row's heading (14).</summary>
        public static double Subtitle => Of("T.Subtitle", 14);

        /// <summary>A window's or a dialog's heading (17).</summary>
        public static double Heading => Of("T.Heading", 17);

        /// <summary>A page's title (20).</summary>
        public static double Title => Of("T.Title", 20);

        /// <summary>A document's first heading (24).</summary>
        public static double Headline => Of("T.Headline", 24);

        /// <summary>The start page's name (30).</summary>
        public static double Hero => Of("T.Hero", 30);
    }
}
