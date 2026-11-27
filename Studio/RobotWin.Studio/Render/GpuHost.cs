using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RobotWin.Studio.Render
{
    /// <summary>
    /// [render] Puts our GPU renderer in front of a WPF 3D view: a <see cref="GpuViewport"/> right above the view in its
    /// panel (so the panel's 2D overlays stay on top), drawn each frame the panel is shown. The view stays where it is,
    /// unseen, for picking; it is shown again whenever the GPU does not draw (Settings → 3D renderer → WPF, a hidden
    /// view, no GPU). The picture is drawn on a clear background, so whatever the panel shows behind the view (a sky,
    /// a gradient) shows behind the scene as before.
    /// </summary>
    internal sealed class GpuHost
    {
        private readonly Viewport3D _view;
        private readonly GpuViewport _gpu;
        private readonly bool _auto;
        private bool _failed, _hooked, _shown;
        private TimeSpan _lastRendering = TimeSpan.MinValue;
        private static bool _reported;

        private GpuHost(Panel host, Viewport3D view, bool auto, Func<double>? focus)
        {
            _view = view;
            _auto = auto;
            _gpu = new GpuViewport(view) { Opacity = 0, Clear = Colors.Transparent, FocusDistance = focus };
            _gpu.Failed += reason =>
            {
                _failed = true;
                Show(false);
                if (_reported) return;
                _reported = true;
                Shell.Workspace.Current.Status = "3B GPU çizimi kullanılamıyor, WPF çizimine dönüldü: " + reason;
            };

            // Where the view stands in its panel: the same cell, margins and alignment.
            Grid.SetRow(_gpu, Grid.GetRow(view));
            Grid.SetColumn(_gpu, Grid.GetColumn(view));
            Grid.SetRowSpan(_gpu, Grid.GetRowSpan(view));
            Grid.SetColumnSpan(_gpu, Grid.GetColumnSpan(view));
            _gpu.Margin = view.Margin;
            _gpu.HorizontalAlignment = view.HorizontalAlignment;
            _gpu.VerticalAlignment = view.VerticalAlignment;
            host.Children.Insert(host.Children.IndexOf(view) + 1, _gpu);
            if (auto)
            {
                host.IsVisibleChanged += (_, _) => Hook(host.IsVisible);
                Hook(host.IsVisible);
            }
        }

        /// <summary>
        /// The GPU draws <paramref name="view"/>, which must be a child of <paramref name="host"/>. With
        /// <paramref name="auto"/> it draws every frame the host is shown; else the owner calls <see cref="Draw"/> at the
        /// end of its own frame (after it moved things), so the picture is never a frame behind. <paramref name="focus"/>
        /// gives the view's orbit distance when it has one (the sun's shadow is sharpest there).
        /// </summary>
        public static GpuHost Attach(Panel host, Viewport3D view, bool auto = true, Func<double>? focus = null) => new GpuHost(host, view, auto, focus);

        /// <summary>The GPU draws the view now.</summary>
        public bool Drawn => _shown;

        /// <summary>Things drawn last frame.</summary>
        public int Items => _gpu.LastItems;

        private void Hook(bool on)
        {
            if (on == _hooked) return;
            _hooked = on;
            if (on) CompositionTarget.Rendering += OnRendering;
            else CompositionTarget.Rendering -= OnRendering;
        }

        private void OnRendering(object? sender, EventArgs e)
        {
            // Once per frame shown (WPF may raise Rendering again in the same frame).
            if (e is RenderingEventArgs re)
            {
                if (re.RenderingTime == _lastRendering) return;
                _lastRendering = re.RenderingTime;
            }

            Draw();
        }

        /// <summary>Draws the view if the GPU should; shows the WPF view instead when it does not. True when drawn.</summary>
        public bool Draw()
        {
            bool want = !_failed && Shell.AppSettings.Current.Renderer3D != "wpf" && _view.Visibility == Visibility.Visible;
            bool drawn = want && _gpu.Draw();
            Show(drawn);
            return drawn;
        }

        private void Show(bool gpu)
        {
            _shown = gpu;
            double view = gpu ? 0 : 1, picture = gpu ? 1 : 0;
            if (_view.Opacity != view) _view.Opacity = view;
            if (_gpu.Opacity != picture) _gpu.Opacity = picture;
        }
    }
}
