using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [rigidbody] Unity's Rigidbody settings on the Fizik card: Is Kinematic (with its velocity and spin), the centre of
    /// mass and inertia given by hand, linear and angular damping, and its Constraints (frozen position and rotation
    /// axes). They live in the part's parameters (rb.kinematic, rb.kinvel, rb.kinspin, rb.com, rb.inertia, rb.damping,
    /// rb.angdamping, rb.lockpos, rb.lockrot) and reach the solver through WorldPhysics.
    /// </summary>
    public partial class WorldPage
    {
        /// <summary>Whether the "Kısıtlar ve sönüm" fold is open (kept while the app runs).</summary>
        private static bool _rigidbodyOpen;

        private void RigidbodyFields(WorldObject o)
        {
            if (o.IsStatic) return;   // a fixed part does not move: nothing to damp or freeze
            bool used = new[] { "rb.damping", "rb.angdamping", "rb.lockpos", "rb.lockrot", WorldPhysics.KinematicKey, WorldPhysics.RigidbodyCentreKey, WorldPhysics.RigidbodyInertiaKey, WorldPhysics.InterpolationKey }.Any(k => o.Parameters.TryGetValue(k, out var v) && !string.IsNullOrEmpty(v) && v != "0");
            bool open = _rigidbodyOpen || used;

            var head = new Button
            {
                Content = (open ? "▾ " : "▸ ") + "Gelişmiş fizik",
                Style = (Style)FindResource("Btn.Ghost"),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 2, 0, 4),
                ToolTip = "Kinematik, elle kütle merkezi ve eylemsizlik, hızın ve dönüşün saniyede ne kadarının söndüğü (yağ, sıvı, fren) ve parçanın hareket edemeyeceği ya da dönemeyeceği dünya eksenleri"
            };
            head.Click += (_, _) =>
            {
                _rigidbodyOpen = !open;
                BuildInspector();
            };
            Inspector.Children.Add(head);
            if (!open) return;

            KinematicFields(o);
            MassFields(o);
            InterpolationField(o);
            Inspector.Children.Add(NumberField("Doğrusal sönüm (1/s)", Param(o, "rb.damping"), v => Edit("Sönüm değişti", () => SetParam(o, "rb.damping", v)), 0, 1000, "0.###"));
            Inspector.Children.Add(NumberField("Açısal sönüm (1/s)", Param(o, "rb.angdamping"), v => Edit("Sönüm değişti", () => SetParam(o, "rb.angdamping", v)), 0, 1000, "0.###"));
            Inspector.Children.Add(Labeled("Konumu dondur", AxisChips(o, "rb.lockpos", "Bu dünya ekseni boyunca hareket etmez")));
            Inspector.Children.Add(Labeled("Dönüşü dondur", AxisChips(o, "rb.lockrot", "Bu dünya ekseni çevresinde dönmez")));
        }

        /// <summary>
        /// [interpolate] Unity's Interpolate: how the part is drawn between the 1 ms physics steps (the physics, the sensors
        /// and the scripts are not changed by it).
        /// </summary>
        private void InterpolationField(WorldObject o)
        {
            string now = o.Parameters.TryGetValue(WorldPhysics.InterpolationKey, out var v) ? v : "interpolate";
            var box = new ComboBox { MinWidth = 150, ToolTip = "Çalışırken parçanın fizik adımları arasında nasıl çizildiği (fiziği, sensörleri ve betikleri değiştirmez)" };
            foreach (var (key, label) in new[] { ("none", "Yok"), ("interpolate", "Ara değerle"), ("extrapolate", "Öngör") })
                box.Items.Add(new ComboBoxItem { Content = label, Tag = key, IsSelected = key == now });
            box.SelectionChanged += (_, _) =>
            {
                if (box.SelectedItem is not ComboBoxItem { Tag: string key } || key == now) return;
                Edit("Ara değerleme değişti", () =>
                {
                    if (key == "interpolate") o.Parameters.Remove(WorldPhysics.InterpolationKey);
                    else o.Parameters[WorldPhysics.InterpolationKey] = key;
                });
            };
            Inspector.Children.Add(Labeled("Ara değerleme", box));
        }

        /// <summary>[rigidbody] Unity's Is Kinematic: the part goes only with the velocity given here (or a behaviour's) and pushes what it meets.</summary>
        private void KinematicFields(WorldObject o)
        {
            bool kinematic = Param(o, WorldPhysics.KinematicKey) > 0;
            var box = new CheckBox
            {
                Content = "Kinematik (yalnız kendi hızıyla gider)", IsChecked = kinematic, Margin = new Thickness(0, 0, 0, 6),
                ToolTip = "Yerçekimi, kuvvetler, çarpmalar ve eklemler onu itmez; aşağıdaki hızla (ya da bir davranışın verdiğiyle) gider ve önündekini sonsuz ağırmış gibi iter: asansör, piston, taşıyıcı tabla"
            };
            box.Click += (_, _) => Edit("Kinematik değişti", () =>
            {
                if (box.IsChecked == true) o.Parameters[WorldPhysics.KinematicKey] = "1";
                else
                    foreach (var k in new[] { WorldPhysics.KinematicKey, WorldPhysics.KinematicVelocityKey, WorldPhysics.KinematicSpinKey })
                        o.Parameters.Remove(k);
            });
            Inspector.Children.Add(box);
            if (!kinematic) return;
            VectorParam(o, WorldPhysics.KinematicVelocityKey, "Hız (m/s) X · Y · Z", "Kinematik hız değişti");
            VectorParam(o, WorldPhysics.KinematicSpinKey, "Dönüş (°/s) X · Y · Z", "Kinematik dönüş değişti");
        }

        /// <summary>
        /// [rigidbody] Unity's Automatic Center Of Mass / Automatic Tensor turned off: the centre of mass (the part's frame, mm)
        /// and the moments of inertia (about the part's axes through that centre, g·cm²) given by hand, starting from the
        /// values the part's shape gives.
        /// </summary>
        private void MassFields(WorldObject o)
        {
            bool manualCentre = o.Parameters.ContainsKey(WorldPhysics.RigidbodyCentreKey);
            var centre = new CheckBox
            {
                Content = "Kütle merkezini elle ver", IsChecked = manualCentre, Margin = new Thickness(0, 4, 0, 6),
                ToolTip = "Kütle merkezi parçanın kendi çerçevesinde (mm). Gizli bir ağırlık (pil, karşı ağırlık) parçayı dengeler ya da devirir; parçanın şekli yerinde kalır."
            };
            centre.Click += (_, _) => Edit("Kütle merkezi değişti", () =>
            {
                if (centre.IsChecked == true && AutomaticMass(o) is { } auto) o.Parameters[WorldPhysics.RigidbodyCentreKey] = WorldPhysics.VectorText(auto.Centre);
                else o.Parameters.Remove(WorldPhysics.RigidbodyCentreKey);
            });
            Inspector.Children.Add(centre);
            if (manualCentre && WorldPhysics.TryVector(o.Parameters[WorldPhysics.RigidbodyCentreKey], out var c))
                Inspector.Children.Add(Vector3Row("Kütle merkezi (mm) X · Y · Z", c.X * 1000, c.Y * 1000, c.Z * 1000, (x, y, z) => Edit("Kütle merkezi değişti", () =>
                    o.Parameters[WorldPhysics.RigidbodyCentreKey] = WorldPhysics.VectorText(new Vec3(x, y, z) / 1000)), "0.##"));

            bool manualInertia = o.Parameters.ContainsKey(WorldPhysics.RigidbodyInertiaKey);
            var inertia = new CheckBox
            {
                Content = "Eylemsizliği elle ver", IsChecked = manualInertia, Margin = new Thickness(0, 4, 0, 6),
                ToolTip = "Parçanın eksenleri çevresindeki eylemsizlik momentleri (g·cm²), kütle merkezinden geçen eksenlere göre. Ölçülmüş ya da CAD'den alınmış değerler için."
            };
            inertia.Click += (_, _) => Edit("Eylemsizlik değişti", () =>
            {
                if (inertia.IsChecked == true && AutomaticMass(o) is { } auto) o.Parameters[WorldPhysics.RigidbodyInertiaKey] = WorldPhysics.VectorText(auto.Inertia);
                else o.Parameters.Remove(WorldPhysics.RigidbodyInertiaKey);
            });
            Inspector.Children.Add(inertia);
            if (manualInertia && WorldPhysics.TryVector(o.Parameters[WorldPhysics.RigidbodyInertiaKey], out var i))
                Inspector.Children.Add(Vector3Row("Eylemsizlik (g·cm²) X · Y · Z", i.X * 1e7, i.Y * 1e7, i.Z * 1e7, (x, y, z) => Edit("Eylemsizlik değişti", () =>
                {
                    if (x > 0 && y > 0 && z > 0) o.Parameters[WorldPhysics.RigidbodyInertiaKey] = WorldPhysics.VectorText(new Vec3(x, y, z) * 1e-7);
                }), "0.###"));
        }

        /// <summary>The centre of mass (the part's frame, m) and the moments of inertia about its axes (kg·m²) its shape and mass give.</summary>
        private (Vec3 Centre, Vec3 Inertia)? AutomaticMass(WorldObject o)
        {
            var keep = new[] { WorldPhysics.RigidbodyCentreKey, WorldPhysics.RigidbodyInertiaKey, WorldPhysics.KinematicKey }
                .Where(o.Parameters.ContainsKey).ToDictionary(k => k, k => o.Parameters[k]);
            foreach (var k in keep.Keys) o.Parameters.Remove(k);
            try
            {
                var alone = new WorldDocument();
                alone.Objects.Add(o);
                var physics = WorldPhysics.Build(alone, Scene.GeometryOf, null);
                var body = physics.Scene.Bodies.FirstOrDefault(b => ReferenceEquals(b.Tag, o));
                if (body == null) return null;
                var q = o.Pose.Orientation;
                var centre = q.Conjugate.Rotate(body.Position - o.Pose.Position);
                var turn = q.Conjugate * body.Orientation;   // the body's axes in the part's frame
                var tensor = body.InertiaTensor().Rotated(turn);
                return (centre, new Vec3(tensor.M00, tensor.M11, tensor.M22));
            }
            finally
            {
                foreach (var (k, v) in keep) o.Parameters[k] = v;
            }
        }

        private static double Param(WorldObject o, string key) =>
            o.Parameters.TryGetValue(key, out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static void SetParam(WorldObject o, string key, double v)
        {
            if (v <= 0) o.Parameters.Remove(key);
            else o.Parameters[key] = v.ToString("0.######", CultureInfo.InvariantCulture);
        }

        /// <summary>X · Y · Z chips for one axis mask parameter ("xz").</summary>
        private FrameworkElement AxisChips(WorldObject o, string key, string tip)
        {
            string now = o.Parameters.TryGetValue(key, out var s) ? s.ToLowerInvariant() : "";
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (char axis in "xyz")
            {
                var chip = new CheckBox   // Unity's Constraints: one box per axis
                {
                    Content = char.ToUpperInvariant(axis).ToString(),
                    IsChecked = now.Contains(axis),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 14, 0),
                    ToolTip = tip + " (" + char.ToUpperInvariant(axis) + ")"
                };
                char a = axis;
                chip.Click += (_, _) => Edit("Kısıt değişti", () =>
                {
                    string cur = o.Parameters.TryGetValue(key, out var c) ? c.ToLowerInvariant() : "";
                    cur = chip.IsChecked == true ? (cur.Contains(a) ? cur : cur + a) : cur.Replace(a.ToString(), "");
                    cur = new string("xyz".Where(cur.Contains).ToArray());
                    if (cur.Length == 0) o.Parameters.Remove(key);
                    else o.Parameters[key] = cur;
                });
                row.Children.Add(chip);
            }

            return row;
        }
    }
}
