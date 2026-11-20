using System;

namespace RobotWin.Studio.Shell
{
    /// <summary>
    /// Cross-probing between pages: the schematic announces the net or part the user is looking
    /// at (net highlight with the ` key, "PCB'de göster"), other pages (PCB, instruments) may
    /// listen and show the same thing.
    /// </summary>
    public static class CrossProbe
    {
        /// <summary>The net highlighted in the schematic (null when the highlight was cleared).</summary>
        public static string? HighlightedNet { get; private set; }

        public static event EventHandler<string?>? NetHighlighted;

        /// <summary>A part was picked for cross-probing (its designator).</summary>
        public static event EventHandler<string>? PartProbed;

        public static void HighlightNet(object? sender, string? net)
        {
            if (string.Equals(HighlightedNet, net, StringComparison.OrdinalIgnoreCase)) return;
            HighlightedNet = net;
            NetHighlighted?.Invoke(sender, net);
        }

        public static void ProbePart(object? sender, string refDes) => PartProbed?.Invoke(sender, refDes);
    }
}
