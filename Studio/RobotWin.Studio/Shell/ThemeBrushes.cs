using System.Windows;
using System.Windows.Media;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// [tokens] The theme's brushes for code that makes plain objects (list rows bound in XAML) before any element can look a
    /// resource up: read when the object is made, so a row built after a theme change takes the new theme.
    /// </summary>
    public static class ThemeBrushes
    {
        /// <summary>The theme's text colour (B.Text).</summary>
        public static Brush Text => Brush("B.Text", Brushes.White);

        /// <summary>The theme's secondary text colour (B.TextSecondary).</summary>
        public static Brush Secondary => Brush("B.TextSecondary", Brushes.Gray);

        private static Brush Brush(string key, Brush fallback) => Application.Current?.TryFindResource(key) as Brush ?? fallback;
    }
}
