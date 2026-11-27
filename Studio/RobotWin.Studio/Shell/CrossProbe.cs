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

        // ------------------------------------------------------------------ [crossprobe] one selection across the pages

        /// <summary>The part selected last on any page, by its identity (<see cref="RobotTwin.CoreSim.Design.SchematicPart.Uid"/>, a board part's PartUid).</summary>
        public static string? SelectedUid { get; private set; }

        /// <summary>Its designator (for parts from before identities, and for the status line).</summary>
        public static string? SelectedRefDes { get; private set; }

        /// <summary>
        /// Raised when a part was selected on one page (KiCad's cross-probing, here by the part's identity, so a renamed
        /// part is still the same one): the other pages select it too, without moving their view.
        /// </summary>
        public static event EventHandler? PartSelected;

        /// <summary>A page selected one part; the others follow (Ayarlar → Dünya ve düzenleme can turn it off).</summary>
        public static void SelectPart(object? sender, string? uid, string? refDes)
        {
            if (!AppSettings.Current.CrossSelect) return;
            if (string.IsNullOrEmpty(uid) && string.IsNullOrEmpty(refDes)) return;
            if (string.Equals(SelectedUid, uid, StringComparison.Ordinal) && string.Equals(SelectedRefDes, refDes, StringComparison.OrdinalIgnoreCase)) return;
            SelectedUid = uid;
            SelectedRefDes = refDes;
            PartSelected?.Invoke(sender, EventArgs.Empty);
        }

        /// <summary>Whether a part (identity, designator) is the one selected across the pages.</summary>
        public static bool IsSelected(string? uid, string? refDes) =>
            !string.IsNullOrEmpty(SelectedUid) && !string.IsNullOrEmpty(uid) ? string.Equals(SelectedUid, uid, StringComparison.Ordinal)
                : !string.IsNullOrEmpty(refDes) && string.Equals(SelectedRefDes, refDes, StringComparison.OrdinalIgnoreCase);
    }
}
