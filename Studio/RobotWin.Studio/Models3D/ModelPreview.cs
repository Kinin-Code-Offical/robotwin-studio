using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using RobotTwin.CoreSim.Design;

namespace RobotWin.Studio.Models3D
{
    /// <summary>Small interactive 3D view of one part model (library page); drag to turn it.</summary>
    public sealed class ModelPreview : Grid
    {
        private readonly Viewport3D _view = new Viewport3D { ClipToBounds = true };
        private readonly PerspectiveCamera _camera = new PerspectiveCamera { FieldOfView = 34 };
        private readonly ModelVisual3D _content = new ModelVisual3D();
        private double _yaw = -2.05, _pitch = 0.62, _distance = 50;
        private Point3D _target;
        private Point? _drag;

        public ModelPreview()
        {
            Background = new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x13));
            ClipToBounds = true;
            _view.Camera = _camera;
            _view.Children.Add(_content);
            Children.Add(_view);
            Render.GpuHost.Attach(this, _view, focus: () => _distance);   // [render] our GPU renderer draws the view
            MouseDown += (_, e) =>
            {
                _drag = e.GetPosition(this);
                CaptureMouse();
            };
            MouseUp += (_, _) =>
            {
                _drag = null;
                ReleaseMouseCapture();
            };
            MouseMove += (_, e) =>
            {
                if (_drag == null) return;
                var p = e.GetPosition(this);
                _yaw -= (p.X - _drag.Value.X) * 0.01;
                _pitch = Math.Max(-1.4, Math.Min(1.5, _pitch + (p.Y - _drag.Value.Y) * 0.008));
                _drag = p;
                UpdateCamera();
            };
            MouseWheel += (_, e) =>
            {
                _distance *= Math.Pow(0.999, e.Delta);
                UpdateCamera();
            };
        }

        /// <summary>Shows the standalone model of a part; returns false when it has no 3D shape.</summary>
        public bool Show(PartDefinition def, IReadOnlyDictionary<string, string> parameters)
        {
            ComponentInstance inst;
            try
            {
                inst = ComponentModels.Create(def, parameters);
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                _content.Content = null;
                return false;
            }

            var root = new Model3DGroup();
            root.Children.Add(new AmbientLight(Color.FromRgb(0x5C, 0x5C, 0x62)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0xE4, 0xE2, 0xDC), new Vector3D(-0.5, 0.7, -1)));
            root.Children.Add(new DirectionalLight(Color.FromRgb(0x58, 0x5C, 0x68), new Vector3D(0.6, -0.4, 0.4)));
            root.Children.Add(inst.Root);
            _content.Content = root;
            var b = inst.Bounds;
            if (b.IsEmpty || b.SizeX + b.SizeY + b.SizeZ < 0.5) return false;
            _target = new Point3D(b.X + b.SizeX / 2, b.Y + b.SizeY / 2, b.Z + b.SizeZ / 2);
            double radius = new Vector3D(b.SizeX, b.SizeY, b.SizeZ).Length / 2;
            _distance = radius / Math.Sin(_camera.FieldOfView * Math.PI / 360) * 1.3;
            UpdateCamera();
            return true;
        }

        private void UpdateCamera()
        {
            var dir = new Vector3D(Math.Cos(_pitch) * Math.Cos(_yaw), Math.Cos(_pitch) * Math.Sin(_yaw), Math.Sin(_pitch));
            _camera.Position = _target + dir * _distance;
            _camera.LookDirection = -dir;
            _camera.UpDirection = new Vector3D(0, 0, 1);
            _camera.NearPlaneDistance = _distance * 0.02;
            _camera.FarPlaneDistance = _distance * 20;
        }
    }
}
