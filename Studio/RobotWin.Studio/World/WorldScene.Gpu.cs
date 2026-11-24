using System.Windows.Media;
using RobotWin.Studio.Render;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [render] The world view drawn by our own GPU renderer (Render/GpuRenderer.cs): physically based materials, the sun's
    /// soft shadow, 4× multisampling. The WPF view stays in place but unseen (picking and dragging still use it); when
    /// the GPU cannot be used, or Settings → 3D view says WPF, WPF draws it as before.
    /// </summary>
    public sealed partial class WorldScene
    {
        private GpuViewport? _gpu;
        private bool _gpuFailed;

        /// <summary>The GPU draws this view now.</summary>
        public bool GpuDrawn { get; private set; }

        private void GpuFrame()
        {
            bool want = !_gpuFailed && Shell.AppSettings.Current.Renderer3D != "wpf";
            if (!want)
            {
                if (_gpu != null)
                {
                    Children.Remove(_gpu);
                    _gpu = null;
                }

                ShowWpf(true);
                return;
            }

            if (_gpu == null)
            {
                _gpu = new GpuViewport(_view);
                _gpu.Failed += reason =>
                {
                    _gpuFailed = true;
                    Shell.Workspace.Current.Status = "3B GPU çizimi kullanılamıyor, WPF çizimine dönüldü: " + reason;
                };
                Children.Insert(Children.IndexOf(_view) + 1, _gpu);
            }

            _gpu.Clear = Background is SolidColorBrush b ? b.Color : Color.FromRgb(0x0B, 0x0B, 0x0D);
            ShowWpf(!_gpu.Draw());
        }

        private void ShowWpf(bool show)
        {
            GpuDrawn = !show;
            double opacity = show ? 1 : 0;
            if (_view.Opacity != opacity) _view.Opacity = opacity;
            if (_gpu != null) _gpu.Opacity = show ? 0 : 1;
        }
    }
}
