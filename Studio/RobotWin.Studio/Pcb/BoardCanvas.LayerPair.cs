using System;
using System.Linq;
using RobotTwin.CoreSim.Pcb;

namespace RobotWin.Studio.Pcb
{
    /// <summary>
    /// [layer-pair] KiCad's layer pair: V (and a via placed while routing) goes to the other layer of the pair, Üst ↔ Alt
    /// unless set otherwise (an inner pair on a 4- or 6-layer board). On a layer outside the pair V goes to the pair's first.
    /// A pair the board does not have falls back to Üst ↔ Alt.
    /// </summary>
    public sealed partial class BoardCanvas
    {
        public CopperLayer PairA { get; set; } = CopperLayer.Top;
        public CopperLayer PairB { get; set; } = CopperLayer.Bottom;

        /// <summary>The pair as the board has it (Üst ↔ Alt when either layer is missing or both are the same).</summary>
        public (CopperLayer A, CopperLayer B) LayerPair
        {
            get
            {
                var stack = Board?.CopperLayers() ?? PcbLayers.Stack(2);
                return stack.Contains(PairA) && stack.Contains(PairB) && PairA != PairB ? (PairA, PairB) : (CopperLayer.Top, CopperLayer.Bottom);
            }
        }

        /// <summary>The layer V goes to from <paramref name="layer"/>.</summary>
        public CopperLayer PairedWith(CopperLayer layer)
        {
            var (a, b) = LayerPair;
            return layer == a ? b : layer == b ? a : a;
        }
    }
}
