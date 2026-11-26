using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>[bom] Where one BOM line is bought: the maker's part number, a supplier's and the price there.</summary>
    public sealed class BomSource
    {
        public string Manufacturer { get; set; } = string.Empty;

        /// <summary>The manufacturer's part number (MPN).</summary>
        public string Mpn { get; set; } = string.Empty;

        /// <summary>LCSC, Digi-Key, Mouser, … (free text; the known ones get their own order file).</summary>
        public string Supplier { get; set; } = string.Empty;

        public string SupplierPn { get; set; } = string.Empty;

        /// <summary>Price of one piece at the supplier (0 = not known).</summary>
        public double UnitPrice { get; set; }

        /// <summary>The fewest pieces the supplier sells (1 = any number).</summary>
        public int MinimumOrder { get; set; } = 1;

        public bool IsEmpty => Manufacturer.Length == 0 && Mpn.Length == 0 && Supplier.Length == 0 && SupplierPn.Length == 0 && UnitPrice <= 0 && MinimumOrder <= 1;
    }

    /// <summary>[bom] A BOM line costed for a number of boards.</summary>
    public sealed class BomCostLine
    {
        public BomLine Line { get; set; } = null!;
        public BomSource Source { get; set; } = new BomSource();
        public string Key { get; set; } = string.Empty;

        /// <summary>Pieces the boards need, and pieces to order (at least the supplier's minimum).</summary>
        public int Needed { get; set; }

        public int Order { get; set; }

        /// <summary>Order × unit price (0 without a price).</summary>
        public double Cost { get; set; }

        public bool Priced => Source.UnitPrice > 0;
    }

    /// <summary>[bom] The costed list: the lines, the cost of the boards, and how much of it is known.</summary>
    public sealed class BomCost
    {
        public List<BomCostLine> Lines { get; } = new List<BomCostLine>();
        public int Boards { get; set; } = 1;
        public string Currency { get; set; } = "USD";
        public double Total => Lines.Sum(l => l.Cost);
        public double PerBoard => Boards > 0 ? Total / Boards : 0;
        public int Unpriced => Lines.Count(l => !l.Priced);
    }

    /// <summary>
    /// [bom] The bill of materials made ready to buy: each line's source (kept with the schematic by the line's identity,
    /// so ten identical resistors share one), the order for a number of boards with the suppliers' minimum quantities,
    /// the cost per board and in all, and the order files the suppliers take (JLCPCB/LCSC assembly BOM, Digi-Key and
    /// Mouser bulk add) besides a full CSV.
    /// </summary>
    public static class BomSourcing
    {
        /// <summary>The key a line's source is kept under: part, preset, value and footprint, as the BOM groups them.</summary>
        public static string KeyOf(BomLine l) => string.Join("|", l.PartId, l.PresetId ?? string.Empty, l.Value, l.Footprint);

        /// <summary>A line's source: what the schematic keeps for it, or the MPN its model parameter gives.</summary>
        public static BomSource SourceOf(Schematic doc, PartLibrary library, BomLine line)
        {
            if (doc.Sourcing.TryGetValue(KeyOf(line), out var kept)) return kept;
            var s = new BomSource();
            var part = doc.Parts.FirstOrDefault(p => p.RefDes == line.References.FirstOrDefault());
            if (part != null && library.Find(part.PartId) is { } def)
            {
                var parameters = doc.ResolveParameters(library, part);
                if (parameters.TryGetValue("model", out var model) && !string.IsNullOrWhiteSpace(model) && def.ValueKey != "model") s.Mpn = model.Trim();
                else if (def.ValueKey == "model" && line.Value.Length > 0) s.Mpn = line.Value;
            }

            return s;
        }

        /// <summary>Keeps <paramref name="source"/> for the line (an empty one is forgotten).</summary>
        public static void Keep(Schematic doc, BomLine line, BomSource source)
        {
            string key = KeyOf(line);
            if (source.IsEmpty) doc.Sourcing.Remove(key);
            else doc.Sourcing[key] = source;
        }

        public static BomCost Cost(Schematic doc, PartLibrary library, IEnumerable<BomLine> lines, int boards)
        {
            var cost = new BomCost { Boards = Math.Max(1, boards), Currency = string.IsNullOrWhiteSpace(doc.BomCurrency) ? "USD" : doc.BomCurrency };
            foreach (var line in lines)
            {
                var source = SourceOf(doc, library, line);
                int needed = line.Quantity * cost.Boards;
                int order = Math.Max(needed, Math.Max(1, source.MinimumOrder));
                cost.Lines.Add(new BomCostLine
                {
                    Line = line, Source = source, Key = KeyOf(line), Needed = needed, Order = order,
                    Cost = source.UnitPrice > 0 ? order * source.UnitPrice : 0,
                });
            }

            return cost;
        }

        private static string Csv(string s, char sep = ',') =>
            s.IndexOf(sep) < 0 && s.IndexOf('"') < 0 && s.IndexOf('\n') < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\"";

        private static string Money(double v) => v.ToString("0.####", CultureInfo.InvariantCulture);

        /// <summary>The full list: references, quantities for the boards, maker, MPN, supplier, prices.</summary>
        public static string ToCsv(BomCost cost)
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", new[] { "Referans", "Adet/kart", "Sipariş", "Değer", "Kılıf", "Üretici", "Üretici parça no", "Tedarikçi", "Tedarikçi parça no", "Birim fiyat", "Tutar" }.Select(Localization.T)))
              .Append(" (").Append(cost.Currency).Append(")\r\n");
            foreach (var l in cost.Lines)
            {
                sb.Append(string.Join(",", new[]
                {
                    Csv(l.Line.ReferenceText), l.Line.Quantity.ToString(CultureInfo.InvariantCulture), l.Order.ToString(CultureInfo.InvariantCulture),
                    Csv(l.Line.Value), Csv(l.Line.Footprint), Csv(l.Source.Manufacturer), Csv(l.Source.Mpn), Csv(l.Source.Supplier), Csv(l.Source.SupplierPn),
                    l.Priced ? Money(l.Source.UnitPrice) : string.Empty, l.Priced ? Money(l.Cost) : string.Empty,
                })).Append("\r\n");
            }

            sb.Append(Localization.T("Toplam")).Append(",,,,,,,,,,").Append(Money(cost.Total)).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>JLCPCB's assembly BOM: Comment, Designator, Footprint, LCSC Part # (the lines with an LCSC number).</summary>
        public static string ToJlcpcb(BomCost cost)
        {
            var sb = new StringBuilder("Comment,Designator,Footprint,LCSC Part #\r\n");
            foreach (var l in cost.Lines.Where(l => Is(l.Source, "LCSC") || Is(l.Source, "JLCPCB")))
                sb.Append(Csv(l.Line.Value)).Append(',').Append(Csv(l.Line.ReferenceText)).Append(',').Append(Csv(l.Line.Footprint)).Append(',').Append(Csv(l.Source.SupplierPn)).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>Digi-Key's bulk add: Quantity, Part Number, Customer Reference.</summary>
        public static string ToDigiKey(BomCost cost)
        {
            var sb = new StringBuilder("Quantity,Part Number,Customer Reference\r\n");
            foreach (var l in cost.Lines.Where(l => Is(l.Source, "Digi-Key") || Is(l.Source, "DigiKey")))
                sb.Append(l.Order.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(l.Source.SupplierPn.Length > 0 ? l.Source.SupplierPn : l.Source.Mpn)).Append(',').Append(Csv(l.Line.ReferenceText)).Append("\r\n");
            return sb.ToString();
        }

        /// <summary>Mouser's BOM import: Mouser Part Number, Quantity, Customer Part Number.</summary>
        public static string ToMouser(BomCost cost)
        {
            var sb = new StringBuilder("Mouser Part Number,Quantity,Customer Part Number\r\n");
            foreach (var l in cost.Lines.Where(l => Is(l.Source, "Mouser")))
                sb.Append(Csv(l.Source.SupplierPn.Length > 0 ? l.Source.SupplierPn : l.Source.Mpn)).Append(',').Append(l.Order.ToString(CultureInfo.InvariantCulture)).Append(',').Append(Csv(l.Line.ReferenceText)).Append("\r\n");
            return sb.ToString();
        }

        private static bool Is(BomSource s, string supplier) => s.Supplier.Replace(" ", string.Empty).Equals(supplier.Replace(" ", string.Empty), StringComparison.OrdinalIgnoreCase);
    }
}
