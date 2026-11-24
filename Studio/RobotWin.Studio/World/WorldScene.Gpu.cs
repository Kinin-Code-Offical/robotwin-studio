using RobotWin.Studio.Render;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [render] The world view drawn by our own GPU renderer (Render/GpuRenderer.cs): physically based materials, the sun's
    /// soft shadow, lamps per pixel, 4× multisampling. The WPF view stays in place but unseen (picking and dragging still
    /// use it); when the GPU cannot be used, or Settings → 3D renderer says WPF, WPF draws it as before.
    /// </summary>
    public sealed partial class WorldScene
    {
        private GpuHost? _gpu;

        /// <summary>The GPU draws this view now.</summary>
        public bool GpuDrawn => _gpu?.Drawn == true;

        /// <summary>At the end of the frame, once the scene has moved: drawn then, never a frame behind.</summary>
        private void GpuFrame()
        {
            _gpu ??= GpuHost.Attach(this, _view, auto: false);
            _gpu.Draw();
        }
    }
}
