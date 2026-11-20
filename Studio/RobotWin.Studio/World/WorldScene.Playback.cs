using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.World
{
    /// <summary>Poses of a recorded / rendered run at the replay's current time.</summary>
    public interface IScenePlayback
    {
        (Vec3 Position, Quat Orientation)? PoseOf(string objectId);

        double PortAngle(string objectId);
    }

    // [render] The scene can show a rendered run: objects take their recorded poses and an overlay
    // (stress meshes, force arrows, flow field) is drawn in world coordinates.
    public sealed partial class WorldScene
    {
        private readonly ModelVisual3D _replay = new ModelVisual3D();

        /// <summary>Recorded poses to show instead of the live simulation (null = normal scene).</summary>
        public IScenePlayback? Playback { get; set; }

        private readonly System.Collections.Generic.Dictionary<string, Model3D?> _hidden = new System.Collections.Generic.Dictionary<string, Model3D?>();

        /// <summary>Hides the normal model of these objects (their stress mesh is drawn instead); null shows all again.</summary>
        public void SetHidden(System.Collections.Generic.ICollection<string>? ids)
        {
            foreach (var kv in _hidden)
                if (_items.TryGetValue(kv.Key, out var it) && (ids == null || !ids.Contains(kv.Key))) it.Visual.Content = kv.Value;
            var keep = new System.Collections.Generic.Dictionary<string, Model3D?>();
            foreach (var kv in _hidden)
                if (ids != null && ids.Contains(kv.Key)) keep[kv.Key] = kv.Value;
            _hidden.Clear();
            foreach (var kv in keep) _hidden[kv.Key] = kv.Value;
            if (ids == null) return;
            foreach (var id in ids)
            {
                if (_hidden.ContainsKey(id) || !_items.TryGetValue(id, out var it)) continue;
                _hidden[id] = it.Visual.Content;
                it.Visual.Content = null;
            }
        }

        /// <summary>Tints objects (emissive) during a replay, e.g. by temperature; null clears every tint.</summary>
        public void SetTints(System.Collections.Generic.IReadOnlyDictionary<string, System.Windows.Media.Color>? tints)
        {
            foreach (var kv in _items)
            {
                if (kv.Value.Model.Heat.Brush is not System.Windows.Media.SolidColorBrush b || b.IsFrozen) continue;
                b.Color = tints != null && tints.TryGetValue(kv.Key, out var c) ? c : System.Windows.Media.Colors.Black;
            }
        }

        /// <summary>Moves every object to the playback pose and replaces the replay overlay.</summary>
        public void ShowPlayback(Model3D? overlay)
        {
            UpdateKinematics();
            RebuildCables();
            _replay.Content = overlay;
        }
    }
}
