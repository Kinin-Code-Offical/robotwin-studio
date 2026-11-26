using System.Collections.Generic;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// [select-filter] Which kinds of board items a click, a box or Ctrl+A picks up, as KiCad's Selection Filter (studied
    /// in place): parts (and among them the locked ones), tracks, vias, texts, mounting holes, copper zones, dimensions
    /// and the board outline. A kind left out is still drawn and still checked; it just cannot be picked.
    /// </summary>
    public sealed class PcbSelectionFilter
    {
        public bool Parts { get; set; } = true;
        public bool Locked { get; set; } = true;
        public bool Tracks { get; set; } = true;
        public bool Vias { get; set; } = true;
        public bool Texts { get; set; } = true;
        public bool Holes { get; set; } = true;
        public bool Zones { get; set; } = true;
        public bool Dimensions { get; set; } = true;
        public bool Outline { get; set; } = true;

        /// <summary>Every kind on (the filter lets everything through).</summary>
        public bool All => Parts && Locked && Tracks && Vias && Texts && Holes && Zones && Dimensions && Outline;

        public void SetAll(bool on) => Parts = Locked = Tracks = Vias = Texts = Holes = Zones = Dimensions = Outline = on;

        /// <summary>The kinds with their labels, in the order the panel lists them, each with its getter and setter.</summary>
        public IEnumerable<(string Label, System.Func<bool> Get, System.Action<bool> Set)> Kinds()
        {
            yield return ("Parçalar", () => Parts, v => Parts = v);
            yield return ("Kilitli olanlar", () => Locked, v => Locked = v);
            yield return ("Yollar", () => Tracks, v => Tracks = v);
            yield return ("Vialar", () => Vias, v => Vias = v);
            yield return ("Yazılar", () => Texts, v => Texts = v);
            yield return ("Montaj delikleri", () => Holes, v => Holes = v);
            yield return ("Dolgu alanları", () => Zones, v => Zones = v);
            yield return ("Ölçüler", () => Dimensions, v => Dimensions = v);
            yield return ("Kart kenarı", () => Outline, v => Outline = v);
        }

        /// <summary>Whether an item may be picked.</summary>
        public bool Allows(object item) => item switch
        {
            BoardComponent c => Parts && (Locked || !c.Locked),
            Track => Tracks,
            Via => Vias,
            BoardText => Texts,
            MountingHole => Holes,
            Zone => Zones,
            BoardDimension => Dimensions,
            BoardCanvas.OutlineSelection => Outline,
            _ => true
        };
    }
}
