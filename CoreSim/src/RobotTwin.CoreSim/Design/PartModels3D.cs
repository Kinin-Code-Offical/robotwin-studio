using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// A user-supplied 3D model of a part (an STL/OBJ/PLY file packed as an
    /// <see cref="EmbeddedMesh"/>, metres) and how it sits on the part's footprint.
    /// Offsets are millimetres in the 3D model frame: x right, y up the board
    /// (opposite to layout Y), z up from the board surface.
    /// </summary>
    public sealed class PartModel3D
    {
        public EmbeddedMesh Mesh { get; set; } = new EmbeddedMesh();

        /// <summary>Original file name (for display).</summary>
        public string Name { get; set; } = string.Empty;

        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        public double OffsetZ { get; set; }

        /// <summary>Rotation in degrees about X, then Y, then Z.</summary>
        public double RotationX { get; set; }

        public double RotationY { get; set; }
        public double RotationZ { get; set; }

        public double Scale { get; set; } = 1.0;

        /// <summary>"#RRGGBB" used when the mesh carries no colours; null = neutral grey.</summary>
        public string? Color { get; set; }
    }

    /// <summary>
    /// Custom 3D models of user parts. A part with a model gets the parameter
    /// <see cref="ParameterKey"/> = its id, which the 3D views resolve here.
    /// </summary>
    public static class PartModels3D
    {
        public const string ParameterKey = "model3d";

        private static readonly Dictionary<string, PartModel3D> Models = new Dictionary<string, PartModel3D>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Gate = new object();

        /// <summary>Raised when a model is added, replaced or removed (views drop their caches).</summary>
        public static event EventHandler? Changed;

        public static void Register(string key, PartModel3D model)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Model key is empty.", nameof(key));
            lock (Gate) Models[key] = model ?? throw new ArgumentNullException(nameof(model));
            Changed?.Invoke(null, EventArgs.Empty);
        }

        public static bool Unregister(string key)
        {
            bool removed;
            lock (Gate) removed = Models.Remove(key);
            if (removed) Changed?.Invoke(null, EventArgs.Empty);
            return removed;
        }

        public static PartModel3D? Find(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return null;
            lock (Gate) return Models.TryGetValue(key, out var m) ? m : null;
        }

        /// <summary>The custom model a part's resolved parameters point at, if any.</summary>
        public static PartModel3D? For(IReadOnlyDictionary<string, string>? parameters) =>
            parameters != null && parameters.TryGetValue(ParameterKey, out var key) ? Find(key) : null;

        /// <summary>
        /// Attaches (or with null, detaches) a model to a registered preset: updates the
        /// registry and the preset's <see cref="ParameterKey"/> parameter.
        /// </summary>
        public static bool Attach(PartLibrary library, string presetId, PartModel3D? model)
        {
            var preset = library.FindPreset(presetId);
            if (preset == null) return false;
            if (model == null) Unregister(presetId);
            else Register(presetId, model);
            if (preset.Parameters is IDictionary<string, string> editable && !editable.IsReadOnly)
            {
                if (model == null) editable.Remove(ParameterKey);
                else editable[ParameterKey] = presetId;
                return true;
            }

            return model == null || preset.Parameters.TryGetValue(ParameterKey, out var k) && string.Equals(k, presetId, StringComparison.OrdinalIgnoreCase);
        }
    }
}
