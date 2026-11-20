using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>
    /// Copper layer helpers. A board has 2, 4 or 6 copper layers; in stack order they are
    /// F.Cu (Top), In1.Cu .. In4.Cu, B.Cu (Bottom).
    /// </summary>
    public static class PcbLayers
    {
        public const int MaxCopperLayers = 6;

        private static readonly CopperLayer[] Inner = { CopperLayer.In1, CopperLayer.In2, CopperLayer.In3, CopperLayer.In4 };

        /// <summary>Supported copper layer counts.</summary>
        public static IReadOnlyList<int> LayerCounts { get; } = new[] { 2, 4, 6 };

        public static int Normalize(int count) => count >= 6 ? 6 : count >= 4 ? 4 : 2;

        /// <summary>Copper layers of a board in stack order (top first).</summary>
        public static IReadOnlyList<CopperLayer> Stack(int count)
        {
            count = Normalize(count);
            var list = new List<CopperLayer>(count) { CopperLayer.Top };
            for (int i = 0; i < count - 2; i++) list.Add(Inner[i]);
            list.Add(CopperLayer.Bottom);
            return list;
        }

        /// <summary>Position of a layer in the stack (0 = top, count-1 = bottom); -1 when the board does not have it.</summary>
        public static int StackIndex(CopperLayer layer, int count)
        {
            count = Normalize(count);
            switch (layer)
            {
                case CopperLayer.Top: return 0;
                case CopperLayer.Bottom: return count - 1;
                default:
                    int k = (int)layer - (int)CopperLayer.In1 + 1;
                    return k <= count - 2 ? k : -1;
            }
        }

        public static bool Exists(CopperLayer layer, int count) => StackIndex(layer, count) >= 0;

        public static bool IsOuter(CopperLayer layer) => layer == CopperLayer.Top || layer == CopperLayer.Bottom;

        public static CopperLayer Opposite(CopperLayer side) => side == CopperLayer.Bottom ? CopperLayer.Top : CopperLayer.Bottom;

        /// <summary>KiCad layer name ("F.Cu", "In1.Cu", "B.Cu").</summary>
        public static string KiCadName(CopperLayer layer)
        {
            switch (layer)
            {
                case CopperLayer.Top: return "F.Cu";
                case CopperLayer.Bottom: return "B.Cu";
                default: return "In" + ((int)layer - (int)CopperLayer.In1 + 1).ToString(CultureInfo.InvariantCulture) + ".Cu";
            }
        }

        /// <summary>KiCad 8 layer ordinal: F.Cu 0, In1.Cu 1 .. In30.Cu 30, B.Cu 31.</summary>
        public static int KiCadOrdinal(CopperLayer layer) =>
            layer == CopperLayer.Top ? 0 : layer == CopperLayer.Bottom ? 31 : (int)layer - (int)CopperLayer.In1 + 1;

        public static CopperLayer? FromKiCadName(string name)
        {
            foreach (CopperLayer l in Enum.GetValues(typeof(CopperLayer)))
            {
                if (string.Equals(KiCadName(l), name, StringComparison.OrdinalIgnoreCase)) return l;
            }

            return null;
        }

        /// <summary>File-name friendly form ("F_Cu", "In1_Cu").</summary>
        public static string FileName(CopperLayer layer) => KiCadName(layer).Replace('.', '_');

        /// <summary>Turkish display name.</summary>
        public static string DisplayName(CopperLayer layer)
        {
            switch (layer)
            {
                case CopperLayer.Top: return "Üst bakır";
                case CopperLayer.Bottom: return "Alt bakır";
                default: return "İç " + ((int)layer - (int)CopperLayer.In1 + 1).ToString(CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Layers spanned between two layers (inclusive), in stack order.</summary>
        public static IEnumerable<CopperLayer> Between(CopperLayer a, CopperLayer b, int count)
        {
            var stack = Stack(count);
            int i = StackIndex(a, count), j = StackIndex(b, count);
            if (i < 0 || j < 0) yield break;
            if (i > j) (i, j) = (j, i);
            for (int k = i; k <= j; k++) yield return stack[k];
        }
    }

    /// <summary>One layer of the physical stackup (copper or dielectric).</summary>
    public sealed class StackupLayer
    {
        public string Name { get; set; } = string.Empty;

        /// <summary>"copper", "core" or "prepreg".</summary>
        public string Type { get; set; } = "copper";

        public double Thickness { get; set; }
        public CopperLayer? Copper { get; set; }
        public string Material { get; set; } = string.Empty;
        public double EpsilonR { get; set; }
        public double LossTangent { get; set; }
    }

    /// <summary>
    /// Board stackup: copper layer count, finished thickness, copper weights and the
    /// dielectric. Dielectrics alternate prepreg / core from the outside in; prepregs
    /// have a fixed thickness and the cores take up the rest of the board thickness.
    /// Outer copper thickness is <see cref="DesignRules.CopperThicknessUm"/>.
    /// </summary>
    public sealed class BoardStackup
    {
        private int _layers = 2;

        public int CopperLayers
        {
            get => _layers;
            set => _layers = PcbLayers.Normalize(value);
        }

        /// <summary>Finished board thickness (mm).</summary>
        public double Thickness { get; set; } = 1.6;

        /// <summary>Inner layer copper (µm); 17.5 µm = 0.5 oz, 35 µm = 1 oz.</summary>
        public double InnerCopperUm { get; set; } = 17.5;

        public string Dielectric { get; set; } = "FR4";
        public double DielectricConstant { get; set; } = 4.5;
        public double LossTangent { get; set; } = 0.02;

        /// <summary>Prepreg thickness of multilayer boards (mm).</summary>
        public double PrepregThickness { get; set; } = 0.2;

        public double SolderMaskThickness { get; set; } = 0.01;

        /// <summary>KiCad copper finish name: "HAL SnPb", "HAL lead-free", "ENIG", "OSP", "None".</summary>
        public string Finish { get; set; } = "HAL lead-free";

        /// <summary>Allows blind and buried vias (4+ layers).</summary>
        public bool AllowBlindBuriedVias { get; set; }

        public static double OzToUm(double oz) => oz * 35.0;
        public static double UmToOz(double um) => um / 35.0;

        /// <summary>The full stack, top to bottom (copper and dielectrics, no mask/silk).</summary>
        public List<StackupLayer> Build(double outerCopperUm)
        {
            var copper = PcbLayers.Stack(CopperLayers);
            int n = copper.Count;
            double cuTotal = 2 * outerCopperUm / 1000.0 + (n - 2) * InnerCopperUm / 1000.0;
            int dielectrics = n - 1;
            int prepregs = n == 2 ? 0 : n / 2;
            int cores = dielectrics - prepregs;
            double rest = Math.Max(0.05, Thickness - 2 * SolderMaskThickness - cuTotal - prepregs * PrepregThickness);
            double core = cores > 0 ? rest / cores : 0;
            var result = new List<StackupLayer>();
            for (int i = 0; i < n; i++)
            {
                var l = copper[i];
                result.Add(new StackupLayer
                {
                    Name = PcbLayers.KiCadName(l), Type = "copper", Copper = l,
                    Thickness = (PcbLayers.IsOuter(l) ? outerCopperUm : InnerCopperUm) / 1000.0
                });
                if (i == n - 1) break;

                // Outermost gaps (and every other one) are prepreg on multilayer boards.
                bool prepreg = n > 2 && i % 2 == 0;
                result.Add(new StackupLayer
                {
                    Name = "dielectric " + (i + 1).ToString(CultureInfo.InvariantCulture),
                    Type = prepreg ? "prepreg" : "core",
                    Thickness = n == 2 ? rest : prepreg ? PrepregThickness : core,
                    Material = Dielectric, EpsilonR = DielectricConstant, LossTangent = LossTangent
                });
            }

            return result;
        }

        public BoardStackup Clone() => (BoardStackup)MemberwiseClone();
    }

    /// <summary>
    /// A net class: the copper rules for a group of nets (KiCad semantics). The clearance
    /// between two items is the larger of their classes' clearances.
    /// </summary>
    public sealed class NetClass
    {
        public const string DefaultName = "Default";
        public const string PowerName = "Power";

        public string Name { get; set; } = DefaultName;
        public double Clearance { get; set; } = 0.2;
        public double TrackWidth { get; set; } = 0.3;
        public double ViaDiameter { get; set; } = 0.9;
        public double ViaDrill { get; set; } = 0.45;

        public NetClass Clone() => (NetClass)MemberwiseClone();

        public override string ToString() => Name;
    }
}
