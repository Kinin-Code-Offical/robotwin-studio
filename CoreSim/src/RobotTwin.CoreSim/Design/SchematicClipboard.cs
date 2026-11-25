using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// A piece of a schematic on the clipboard: parts, wires, labels and no-connect flags with a
    /// reference point (<see cref="Anchor"/>) that follows the cursor while pasting. Serialized
    /// as JSON tagged with <see cref="FormatId"/>, so it can be pasted into another tab or another
    /// running instance of the app.
    /// </summary>
    public sealed class SchematicFragment
    {
        public const string FormatId = "robotwin.schematic.fragment";
        public const int FormatVersion = 1;

        public string Format { get; set; } = FormatId;
        public int Version { get; set; } = FormatVersion;

        /// <summary>Title of the sheet it was copied from (for the status line).</summary>
        public string? Source { get; set; }

        /// <summary>Grid point the fragment is held by while it follows the cursor.</summary>
        public GridPoint Anchor { get; set; }

        public List<SchematicPart> Parts { get; set; } = new List<SchematicPart>();
        public List<SchematicWire> Wires { get; set; } = new List<SchematicWire>();
        public List<NetLabel> Labels { get; set; } = new List<NetLabel>();
        public List<NoConnectFlag> NoConnects { get; set; } = new List<NoConnectFlag>();

        [JsonIgnore]
        public bool IsEmpty => Parts.Count == 0 && Wires.Count == 0 && Labels.Count == 0 && NoConnects.Count == 0;

        [JsonIgnore]
        public int Count => Parts.Count + Wires.Count + Labels.Count + NoConnects.Count;

        /// <summary>Every object of the fragment.</summary>
        [JsonIgnore]
        public IEnumerable<object> Items => Parts.Cast<object>().Concat(Wires).Concat(Labels).Concat(NoConnects);

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };

        /// <summary>Deep copies of the selected items; the anchor is the grid-snapped centre of their bounds.</summary>
        public static SchematicFragment FromItems(IEnumerable<object> items, PartLibrary library, string? source = null)
        {
            var list = items.ToList();
            var f = new SchematicFragment { Source = source };
            foreach (var item in list)
            {
                switch (item)
                {
                    case SchematicPart p: f.Parts.Add(Copy(p)); break;
                    case SchematicWire w when w.Points.Count >= 2: f.Wires.Add(new SchematicWire { Points = new List<GridPoint>(w.Points) }); break;
                    case NetLabel l: f.Labels.Add(new NetLabel { Name = l.Name, Position = l.Position, Global = l.Global }); break;
                    case NoConnectFlag nc: f.NoConnects.Add(new NoConnectFlag { Position = nc.Position }); break;
                }
            }

            var bounds = SchematicEditing.Bounds(f.Items, library);
            f.Anchor = bounds == null ? new GridPoint(0, 0) : SchematicEditing.Snap(bounds.Value.Center);
            return f;
        }

        public static SchematicPart Copy(SchematicPart p) => new SchematicPart
        {
            RefDes = p.RefDes,
            PartId = p.PartId,
            PresetId = p.PresetId,
            Position = p.Position,
            Rotation = p.Rotation,
            Mirror = p.Mirror,
            Parameters = new Dictionary<string, string>(p.Parameters, StringComparer.OrdinalIgnoreCase)
        };

        public SchematicFragment Clone() => FromJson(ToJson()) ?? new SchematicFragment();

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        /// <summary>Parses clipboard text; null when it is not a RobotWin schematic fragment.</summary>
        public static SchematicFragment? FromJson(string? text)
        {
            if (string.IsNullOrWhiteSpace(text) || text!.IndexOf(FormatId, StringComparison.Ordinal) < 0) return null;
            try
            {
                var f = JsonSerializer.Deserialize<SchematicFragment>(text, JsonOptions);
                if (f == null || f.Format != FormatId || f.Version > FormatVersion) return null;
                f.Parts ??= new List<SchematicPart>();
                f.Wires ??= new List<SchematicWire>();
                f.Labels ??= new List<NetLabel>();
                f.NoConnects ??= new List<NoConnectFlag>();
                foreach (var p in f.Parts)
                {
                    p.Parameters = new Dictionary<string, string>(p.Parameters ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase);
                }

                return f;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        /// <summary>Moves every item (and the anchor) by delta.</summary>
        public void Translate(GridPoint delta)
        {
            SchematicEditing.Translate(Items, delta);
            Anchor += delta;
        }

        /// <summary>Puts the anchor at <paramref name="at"/>.</summary>
        public void MoveAnchorTo(GridPoint at) => Translate(at - Anchor);

        public void Rotate(bool clockwise = false) => SchematicEditing.Rotate(Items, Anchor, clockwise);

        public void Mirror(bool vertical) => SchematicEditing.Mirror(Items, Anchor, vertical);

        /// <summary>
        /// Adds copies of the fragment to the sheet with the anchor at <paramref name="at"/>.
        /// Parts whose definition the library does not know are skipped; designators that would
        /// clash are re-annotated with the lowest free numbers.
        /// </summary>
        public PasteResult PasteInto(Schematic doc, PartLibrary library, GridPoint at)
        {
            var copy = Clone();
            copy.MoveAnchorTo(at);
            var result = new PasteResult();
            foreach (var p in copy.Parts)
            {
                if (library.Find(p.PartId) == null)
                {
                    result.SkippedParts.Add(p.RefDes + " (" + p.PartId + ")");
                    continue;
                }

                p.Uid = Schematic.NewPartUid();   // [integrity] a copy is a part of its own
                doc.Parts.Add(p);
                result.Parts.Add(p);
            }

            foreach (var w in copy.Wires)
            {
                doc.Wires.Add(w);
                result.Items.Add(w);
            }

            foreach (var l in copy.Labels)
            {
                doc.Labels.Add(l);
                result.Items.Add(l);
            }

            foreach (var nc in copy.NoConnects)
            {
                doc.NoConnects.Add(nc);
                result.Items.Add(nc);
            }

            result.Items.InsertRange(0, result.Parts);
            result.Renamed.AddRange(Annotator.ResolveConflicts(doc, library, result.Parts));
            return result;
        }
    }

    public sealed class PasteResult
    {
        public List<SchematicPart> Parts { get; } = new List<SchematicPart>();

        /// <summary>Everything that was added (parts first).</summary>
        public List<object> Items { get; } = new List<object>();

        public List<RefDesChange> Renamed { get; } = new List<RefDesChange>();
        public List<string> SkippedParts { get; } = new List<string>();
    }
}
