using RobotTwin.CoreSim.Mechanics;

namespace RobotWin.Studio.Assembly3D
{
    // [phys] A stopped vehicle is shown at rest: wheels and propellers still (blades drawn, no
    // spin blur) and pushable arena objects back at their start poses instead of where the last
    // run left them.
    public sealed partial class AssemblyScene
    {
        private void ResetMotion()
        {
            foreach (var (rot, _, _) in _spinners) rot.Angle = 0;
            foreach (var (prop, blades, blur, _) in _props)
            {
                if (!prop.Children.Contains(blades)) prop.Children.Add(blades);
                blur.Material = Transparent;
                blur.BackMaterial = Transparent;
            }

            if (_bodyVisuals.Count == 0) return;
            foreach (var t in _bodyVisuals.Values)
            {
                for (int i = Root.Children.Count - 1; i >= 0; i--)
                {
                    if (ReferenceEquals(Root.Children[i].Transform, t)) Root.Children.RemoveAt(i);
                }
            }

            _bodyVisuals.Clear();
            _previewMovables.Children.Clear();
            var preview = new World3D(Arena);
            _ = preview.Movables;
            foreach (var b in preview.Scene.Bodies)
            {
                if (b == preview.Body || b.IsStatic || Equals(b.Tag, World3D.LegacyArenaTag)) continue;
                var visual = BodyVisual(b);
                if (visual != null) _previewMovables.Children.Add(visual);
            }
        }
    }
}
