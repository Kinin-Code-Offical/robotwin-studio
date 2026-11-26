using System;
using System.Globalization;
using System.Windows;
using RobotTwin.CoreSim.Modeling;
using RobotWin.Studio.Shell;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [section] SolidWorks' Section View and Fusion's Section Analysis in the part editor: the model cut by a plane
    /// across X, Y or Z, the front taken away and the cut shown in its own colour, so walls, holes and ribs can be seen;
    /// a slider moves the plane through the model, Ters shows the other half. Display only.
    /// </summary>
    public partial class ModelPage
    {
        private bool _sectionSync;
        private bool _sectionInput;

        private void OnSection(object sender, RoutedEventArgs e)
        {
            if (SectionToggle.IsChecked == true) ShowSection(_vp.Section?.Axis ?? SectionAxis.X, null);
            else CloseSection();
        }

        /// <summary>Turns the section on along an axis, at a place (mm; null: the middle of the model).</summary>
        private void ShowSection(SectionAxis axis, double? at)
        {
            if (!_sectionInput)
            {
                // [slider-input] the plane's place typed to the 0.01 mm beside the slider
                RobotWin.Studio.Theme.SliderInput.AttachLength(SectionSlider, SectionValue);
                _sectionInput = true;
            }

            var (lo, hi) = SectionView.Range(_ed.Doc, axis);
            double pad = Math.Max(0.5, (hi - lo) * 0.02);
            _sectionSync = true;
            try
            {
                SectionToggle.IsChecked = true;
                SectionBar.Visibility = Visibility.Visible;
                SectionX.IsChecked = axis == SectionAxis.X;
                SectionY.IsChecked = axis == SectionAxis.Y;
                SectionZ.IsChecked = axis == SectionAxis.Z;
                SectionSlider.Minimum = lo - pad;
                SectionSlider.Maximum = hi + pad;
                SectionSlider.Value = Math.Max(SectionSlider.Minimum, Math.Min(SectionSlider.Maximum, at ?? (lo + hi) / 2));
            }
            finally
            {
                _sectionSync = false;
            }

            // the half nearer the camera goes, so the cut faces the viewer; Ters shows the other half
            var plane = new SectionPlane { Axis = axis, Offset = SectionSlider.Value };
            bool cameraBehind = (_vp.Cam.Position - plane.Point).Dot(plane.AxisVector) < 0;
            plane.Flip = cameraBehind ^ (SectionFlip.IsChecked == true);
            _vp.Section = plane;
            UpdateSection();
        }

        private void UpdateSection()
        {
            if (_vp.Section == null) return;
            _vp.Rebuild();
            _ed.Status(Lang.T($"Kesit: {_vp.Section.Axis} = {LengthUnits.Format(_vp.Section.Offset)} (yalnız görüntü)"));
        }

        private void CloseSection()
        {
            _vp.Section = null;
            SectionToggle.IsChecked = false;
            SectionBar.Visibility = Visibility.Collapsed;
            _vp.Rebuild();
        }

        private void OnSectionAxis(object sender, RoutedEventArgs e)
        {
            if (_sectionSync || sender is not FrameworkElement { Tag: string tag } || !Enum.TryParse(tag, out SectionAxis axis)) return;
            ShowSection(axis, null);
        }

        private void OnSectionMoved(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_sectionSync || _vp.Section == null) return;
            _vp.Section.Offset = SectionSlider.Value;
            UpdateSection();
        }

        private void OnSectionFlip(object sender, RoutedEventArgs e)
        {
            if (_vp.Section == null) return;
            _vp.Section.Flip = !_vp.Section.Flip;
            UpdateSection();
        }

        private void OnSectionClose(object sender, RoutedEventArgs e) => CloseSection();
    }
}
