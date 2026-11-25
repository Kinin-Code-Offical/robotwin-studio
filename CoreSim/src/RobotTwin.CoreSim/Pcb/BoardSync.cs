using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;

namespace RobotTwin.CoreSim.Pcb
{
    /// <summary>[integrity] What bringing a board up to its schematic changes (<see cref="BoardSync.Preview"/>).</summary>
    public sealed class BoardChanges
    {
        /// <summary>Parts of the schematic the board does not have yet: their footprints are added (placed again where they were, if they were on it before).</summary>
        public List<string> Added { get; } = new List<string>();

        /// <summary>Parts the schematic no longer has: their footprints leave the board.</summary>
        public List<string> Removed { get; } = new List<string>();

        /// <summary>Parts whose footprint changed (the part's type or package): their pads change, the placement stays.</summary>
        public List<(string RefDes, string From, string To)> Footprints { get; } = new List<(string, string, string)>();

        /// <summary>Pads that go to another net ("U1.3: N$4 → VCC"; null: none).</summary>
        public List<(string RefDes, string Pad, string? From, string? To)> Nets { get; } = new List<(string, string, string?, string?)>();

        /// <summary>Tracks and vias on nets that no longer exist: they are deleted.</summary>
        public int TracksRemoved { get; set; }

        public double TrackLengthRemoved { get; set; }
        public int ViasRemoved { get; set; }

        public bool IsEmpty => Added.Count == 0 && Removed.Count == 0 && Footprints.Count == 0 && Nets.Count == 0 && TracksRemoved == 0 && ViasRemoved == 0;

        /// <summary>Something laid out is lost or redone: footprints leave or change, tracks or vias are deleted.</summary>
        public bool Destructive => Removed.Count > 0 || Footprints.Count > 0 || TracksRemoved > 0 || ViasRemoved > 0;

        /// <summary>
        /// What changes, a short line each ("2 parça eklenecek (U3, R5)", "3 iz silinecek (42 mm)"); <paramref name="done"/>:
        /// said of what was just done ("eklendi"). One per line, so a language pack translates each on its own.
        /// </summary>
        public List<string> SummaryLines(bool done = false)
        {
            string len = TrackLengthRemoved.ToString("0", CultureInfo.InvariantCulture);
            var lines = new List<string>();
            if (Added.Count > 0) lines.Add($"{Added.Count} parça {(done ? "eklendi" : "eklenecek")} ({Names(Added)})");
            if (Removed.Count > 0) lines.Add($"{Removed.Count} parça {(done ? "kalktı" : "kalkacak")} ({Names(Removed)})");
            if (Footprints.Count > 0) lines.Add($"{Footprints.Count} parçanın ayak izi {(done ? "değişti" : "değişecek")} ({Names(Footprints.Select(f => f.RefDes).ToList())})");
            if (Nets.Count > 0) lines.Add($"{Nets.Count} ped başka hatta {(done ? "geçti" : "geçecek")}");
            if (TracksRemoved > 0) lines.Add($"{TracksRemoved} iz {(done ? "silindi" : "silinecek")} ({len} mm)");
            if (ViasRemoved > 0) lines.Add($"{ViasRemoved} via {(done ? "silindi" : "silinecek")}");
            return lines;
        }

        /// <summary>The changes in a few lines (<see cref="SummaryLines"/>).</summary>
        public string Summary(bool done = false) => string.Join("\n", SummaryLines(done));

        /// <summary>Every change, a line each (the details).</summary>
        public List<string> Lines(bool done = false)
        {
            string len = TrackLengthRemoved.ToString("0", CultureInfo.InvariantCulture);
            var lines = new List<string>();
            foreach (var r in Added) lines.Add(done ? $"{r}: karta eklendi" : $"{r}: karta eklenecek");
            foreach (var r in Removed) lines.Add(done ? $"{r}: karttan kalktı (yeri hatırlanıyor; şemaya dönerse oraya konur)" : $"{r}: karttan kalkacak (yeri hatırlanır; şemaya dönerse oraya konur)");
            foreach (var (r, from, to) in Footprints) lines.Add($"{r}: ayak izi {from} → {to}");
            foreach (var (r, pad, from, to) in Nets) lines.Add($"{r}.{pad}: {from ?? "bağlantısız"} → {to ?? "bağlantısız"}");
            if (TracksRemoved > 0) lines.Add(done ? $"{TracksRemoved} iz ({len} mm) artık olmayan hatlardaydı: silindi" : $"{TracksRemoved} iz ({len} mm) artık olmayan hatlarda: silinecek");
            if (ViasRemoved > 0) lines.Add(done ? $"{ViasRemoved} via artık olmayan hatlardaydı: silindi" : $"{ViasRemoved} via artık olmayan hatlarda: silinecek");
            return lines;
        }

        private static string Names(List<string> refs) => refs.Count <= 4 ? string.Join(", ", refs) : string.Join(", ", refs.Take(4)) + " …";
    }

    /// <summary>
    /// [integrity] A board against its schematic: what <see cref="BoardDocument.Synchronize"/> will change, worked out on a
    /// copy so the answer is exactly what synchronising does. The PCB page says so when it brings the board up to date,
    /// and the project check when a project's board is behind its schematic.
    /// </summary>
    public static class BoardSync
    {
        public static BoardChanges Preview(Schematic schematic, PartLibrary library, BoardDocument board)
        {
            var copy = Clone(board);
            BoardDocument.Synchronize(schematic, library, copy);
            return Diff(board, copy);
        }

        public static BoardChanges Diff(BoardDocument before, BoardDocument after)
        {
            var changes = new BoardChanges();
            foreach (var c in after.Components)
                if (before.Find(c.RefDes) == null) changes.Added.Add(c.RefDes);
            foreach (var c in before.Components)
            {
                var now = after.Find(c.RefDes);
                if (now == null)
                {
                    changes.Removed.Add(c.RefDes);
                    continue;
                }

                if (!string.Equals(c.FootprintId, now.FootprintId, StringComparison.OrdinalIgnoreCase)) changes.Footprints.Add((c.RefDes, c.FootprintId, now.FootprintId));
                else
                {
                    foreach (string pad in c.PadNets.Keys.Union(now.PadNets.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                    {
                        c.PadNets.TryGetValue(pad, out var from);
                        now.PadNets.TryGetValue(pad, out var to);
                        if (!string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) changes.Nets.Add((c.RefDes, pad, from, to));
                    }
                }
            }

            // Synchronising only deletes tracks and vias (those on nets that are gone).
            var tracksLeft = new HashSet<string>(after.Tracks.Select(Key));
            foreach (var t in before.Tracks)
            {
                if (tracksLeft.Contains(Key(t))) continue;
                changes.TracksRemoved++;
                changes.TrackLengthRemoved += t.Length;
            }

            changes.ViasRemoved = Math.Max(0, before.Vias.Count - after.Vias.Count);
            changes.Added.Sort(StringComparer.OrdinalIgnoreCase);
            changes.Removed.Sort(StringComparer.OrdinalIgnoreCase);
            return changes;
        }

        private static string Key(Track t) => string.Create(CultureInfo.InvariantCulture, $"{t.Net}|{t.Layer}|{t.X1:0.####}|{t.Y1:0.####}|{t.X2:0.####}|{t.Y2:0.####}");

        private static BoardDocument Clone(BoardDocument board) =>
            JsonSerializer.Deserialize<BoardDocument>(JsonSerializer.Serialize(board, RobotWinFormats.Json), RobotWinFormats.Json) ?? new BoardDocument();
    }
}
