using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.World;

namespace RobotWin.Studio.Pages
{
    /// <summary>
    /// [objs] Ready wheels, casters and propellers (palette, snapping onto shafts, inspector, live
    /// thrust / roller readouts) and free-standing lights (palette, room light). WorldPage.xaml.cs
    /// only calls InitReady, ReadyMechPalette, ReadyLightsPalette, TryAttachReady, ReadySection,
    /// ReadyLive and ReadyKind.
    /// </summary>
    public partial class WorldPage
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private void InitReady()
        {
            Scene.ReadySnapRequested += (part, parent, port) => Dispatcher.BeginInvoke(new Action(() => AttachReady(part, parent, port)));
        }

        // ================================================================ palette

        private void ReadyMechPalette()
        {
            Palette.Children.Add(Caption(ReadyParts.GroupWheels));
            Palette.Children.Add(Note("Gerçek ölçü, kütle ve lastik. Bir motor seçiliyken eklerseniz miline takılır; sonradan da motor milinin yanına sürükleyip bırakmanız yeterli."));
            foreach (var s in ReadyParts.All.Where(x => x.Kind != ReadyPartKind.Propeller))
            {
                var id = s.Id;
                Palette.Children.Add(Row(s.Name, s.Description, () => AddReady(id)));
            }

            Palette.Children.Add(Caption(ReadyParts.GroupProps));
            Palette.Children.Add(Note("Motor miline takılınca dönerken itki T = C_T·ρ·n²·D⁴ üretir, sürükleme torku Q = C_Q·ρ·n²·D⁵ motoru yükler. Çapraz motorlarda CCW ve CW pervaneleri eşleyin."));
            foreach (var s in ReadyParts.All.Where(x => x.Kind == ReadyPartKind.Propeller))
            {
                var id = s.Id;
                Palette.Children.Add(Row(s.Name, s.Description, () => AddReady(id)));
            }
        }

        private void ReadyLightsPalette()
        {
            Palette.Children.Add(Caption("Işıklar (sahneyi aydınlatır)", 0));
            Palette.Children.Add(Note("Kabloyla beslenen LED'ler ve lambalar dünyayı gerçekten aydınlatır: rengi ve parlaklığı simüle edilen akımdan gelir."));
            foreach (var id in new[] { "led5-red", "led5-green", "led5-blue", "led5-white", "led3-red", "led-rgb-5mm", "led-1w-white", "led-3w-white", "led-3w-red", "led-strip-12v", "lamp-3v8-0w3", "lamp-6v-0w6", "lamp-12v-5w" })
            {
                var p = Lib.FindPreset(id);
                if (p == null) continue;
                var pid = p.Id;
                Palette.Children.Add(Row(p.Name, p.Description, () => AddComponent(pid)));
            }

            RoomLightButtons();
        }

        private void RoomLightButtons()
        {
            var world = World;
            if (world == null) return;
            Palette.Children.Add(Note($"Oda ışığı: %{world.RoomLight * 100:0}"));
            var row = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (label, value) in new[] { ("Aydınlık", 1.0), ("Loş", 0.35), ("Karanlık", 0.06) })
            {
                var b = new Button { Content = label, Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(6, 4, 6, 4) };
                double v = value;
                b.Click += (_, _) => Edit($"Oda ışığı %{v * 100:0}", () => world.RoomLight = v);
                row.Children.Add(b);
            }

            Palette.Children.Add(row);
        }

        // ================================================================ adding and attaching

        private void AddReady(string id)
        {
            EnsureWorld();
            var spec = ReadyParts.Find(id);
            if (spec == null) return;
            var selected = World!.Find(Scene.Selected);
            WorldObject? added = null;
            string status = spec.Name + " eklendi";
            Edit("Eklendi", () =>
            {
                added = ReadyParts.Add(World!, id, NewPose());
                // A motor (or servo) is selected and its output is free: mount the new part right away.
                if (selected != null && spec.MountsOnShaft && Scene.PortOf(selected.Id) is { } pf &&
                    !World.Joints.Any(j => j.ObjectB == selected.Id && j.PortB == pf.Name))
                {
                    ReadyParts.AttachToPort(World, added, selected, pf.Name, ToVec(pf.Pivot), ToVec(pf.Axis));
                    status = $"{spec.Name} → {selected.Name} {pf.Label.ToLowerInvariant()}ne takıldı";
                }
            });
            if (added != null)
            {
                Scene.Select(added.Id);
                W.Status = status;
            }
        }

        private static Vec3 ToVec(System.Windows.Media.Media3D.Point3D p) => new Vec3(p.X, p.Y, p.Z);

        private static Vec3 ToVec(System.Windows.Media.Media3D.Vector3D v) => new Vec3(v.X, v.Y, v.Z);

        /// <summary>Mounts a ready part on a port: moved onto the shaft, fixed joint to the rotating part.</summary>
        private void AttachReady(string partId, string parentId, string port)
        {
            var world = World;
            var part = world?.Find(partId);
            var parent = world?.Find(parentId);
            if (world == null || part == null || parent == null || Scene.PortOf(parentId) is not { } pf || pf.Name != port) return;
            Edit($"{part.Name} → {parent.Name} {pf.Label.ToLowerInvariant()}ne takıldı",
                () => ReadyParts.AttachToPort(world, part, parent, port, ToVec(pf.Pivot), ToVec(pf.Axis)));
            Scene.Select(partId);
            W.Status = $"{part.Name}, {parent.Name} {pf.Label.ToLowerInvariant()}ne takıldı";
        }

        /// <summary>Bağla tool on a port with a ready wheel / propeller: snap it onto the shaft instead of joining it where it lies.</summary>
        private bool TryAttachReady(string childId, string parentId, WorldJointKind kind, string port)
        {
            if (kind != WorldJointKind.Fixed || string.IsNullOrEmpty(port)) return false;
            var spec = ReadyParts.Of(World?.Find(childId));
            if (spec == null || !spec.MountsOnShaft) return false;
            AttachReady(childId, parentId, port);
            SetTool(WorldTool.Select);
            return true;
        }

        // ================================================================ inspector

        private string? ReadyKind(WorldObject o) => ReadyParts.Of(o) is { } s ? "Hazır parça · " + s.Name : null;

        private void ReadySection(WorldDocument world, WorldObject o)
        {
            var spec = ReadyParts.Of(o);
            if (spec != null)
            {
                ReadyPartInfo(world, o, spec);
                return;
            }

            // A motor or servo with a free output: offer the parts that fit it.
            if (o.Kind != WorldObjectKind.Component || Scene.PortOf(o.Id) is not { } pf) return;
            if (world.Joints.Any(j => j.ObjectB == o.Id && j.PortB == pf.Name)) return;
            string model = ((o.PresetId ?? string.Empty) + " " + (o.Parameters.TryGetValue("model", out var m) ? m : string.Empty)).ToLowerInvariant();
            string[] fits = model.Contains("8520") || model.Contains("0720") ? new[] { "prop-55-ccw", "prop-55-cw", "prop-65-ccw", "prop-65-cw" }
                : model.Contains("n20") ? new[] { "wheel-n20-34", "wheel-43" }
                : model.Contains("130") ? new[] { "prop-5045-ccw", "prop-5040x3-ccw" }
                : model.Contains("25ga") || model.Contains("jga") ? new[] { "wheel-offroad-85", "omni-60" }
                : o.PartId == "dc-motor" ? new[] { "wheel-tt-65", "mecanum-65-a", "mecanum-65-b", "omni-60" }
                : Array.Empty<string>();
            if (fits.Length == 0) return;
            Inspector.Children.Add(Caption(pf.Label + "ne hazır parça tak"));
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 6) };
            foreach (var id in fits)
            {
                var s = ReadyParts.Find(id);
                if (s == null) continue;
                var b = new Button { Content = s.Name.Replace("Pervane ", string.Empty).Replace("tekerlek ", string.Empty), Margin = new Thickness(0, 0, 4, 4), Padding = new Thickness(6, 4, 6, 4), ToolTip = s.Description };
                var pid = id;
                b.Click += (_, _) =>
                {
                    Scene.Select(o.Id);
                    AddReady(pid);
                };
                grid.Children.Add(b);
            }

            Inspector.Children.Add(grid);
        }

        private void ReadyPartInfo(WorldDocument world, WorldObject o, ReadyPartSpec s)
        {
            Inspector.Children.Add(Caption("Hazır parça"));
            Inspector.Children.Add(Note(s.Description));
            string size = s.Kind switch
            {
                ReadyPartKind.Propeller => $"Ø{s.Diameter * 1000:0} mm ({s.Diameter / 0.0254:0.#}\") · adım {s.Pitch * 1000:0} mm ({s.Pitch / 0.0254:0.#}\") · {s.Blades} kanat · {s.MassKg * 1000:0.##} g",
                ReadyPartKind.BallCaster => $"Bilye Ø{s.Diameter * 1000:0} mm · yükseklik {s.Width * 1000:0} mm · {s.MassKg * 1000:0} g",
                _ => $"Ø{s.Diameter * 1000:0} × {s.Width * 1000:0} mm · {s.MassKg * 1000:0} g" + (s.Rollers > 0 ? $" · {s.Rollers} makara, {s.RollerAngleDeg:0}°" : string.Empty)
            };
            Inspector.Children.Add(Note(size + (s.BoreText.Length > 0 ? " · " + s.BoreText : string.Empty)));
            if (s.Kind == ReadyPartKind.Propeller)
            {
                double rpm = 10000;
                double t = PropellerAero.Thrust(s, rpm), q = PropellerAero.Torque(s, rpm);
                Inspector.Children.Add(Note(string.Format(Inv, "C_T {0:0.###} · C_P {1:0.###} · {2:0} rpm'de {3:0.00} N ({4:0} g) itki, {5:0.00} mN·m tork, {6:0.0} W",
                    s.ThrustCoefficient, s.PowerCoefficient, rpm, t, t / 9.81 * 1000, q * 1000, PropellerAero.Power(s, rpm)) + " · " +
                    (s.Hand > 0 ? "CCW: üstten bakınca saat yönünün tersine dönerken iter" : "CW (R): üstten bakınca saat yönünde dönerken iter")));
            }
            else if (s.Kind == ReadyPartKind.MecanumWheel)
            {
                Inspector.Children.Add(Note("Makaralar yalnızca kendi eksenleri boyunca iter: A ve B tekerlekler çapraz takılınca araç yana kayabilir ve yerinde dönebilir."));
            }
            else if (s.Kind == ReadyPartKind.OmniWheel)
            {
                Inspector.Children.Add(Note("Makaralar dik: tekerlek dönüş yönünde iter, ekseni boyunca serbestçe kayar."));
            }

            if (!s.MountsOnShaft) return;
            var mount = world.Joints.FirstOrDefault(j => j.ObjectA == o.Id && j.ObjectB != null && !string.IsNullOrEmpty(j.PortB));
            if (mount != null)
            {
                Inspector.Children.Add(Note($"Takılı: {world.Find(mount.ObjectB)?.Name} · {Scene.PortOf(mount.ObjectB!)?.Label ?? mount.PortB}"));
                return;
            }

            // Free outputs, nearest first.
            var candidates = world.Objects
                .Where(x => x.Id != o.Id && Scene.PortOf(x.Id) is { } p && !world.Joints.Any(j => j.ObjectB == x.Id && j.PortB == p.Name))
                .OrderBy(x => (x.Pose.Position - o.Pose.Position).Length)
                .Take(4)
                .ToList();
            if (candidates.Count == 0)
            {
                Inspector.Children.Add(Note("Takılabilecek boş motor mili / servo kolu yok."));
                return;
            }

            foreach (var c in candidates)
            {
                var pf = Scene.PortOf(c.Id)!.Value;
                string pid = c.Id, port = pf.Name;
                Inspector.Children.Add(Action($"{c.Name} · {pf.Label.ToLowerInvariant()}ne tak", () => AttachReady(o.Id, pid, port)));
            }
        }

        /// <summary>Live lines of a ready part or a light.</summary>
        private void ReadyLive(WorldObject o, List<string> lines)
        {
            var physics = _session.Physics;
            var spec = ReadyParts.Of(o);
            if (physics != null && spec != null)
            {
                if (physics.PropReadout(o.Id) is { } p)
                {
                    lines.Add(string.Format(Inv, "Pervane        : {0:0} rpm", p.Rpm));
                    lines.Add(string.Format(Inv, "İtki           : {0:0.000} N ({1:0.0} g)", p.Thrust, p.Thrust / 9.81 * 1000));
                    lines.Add(string.Format(Inv, "Sürükleme torku: {0:0.000} mN·m · {1:0.00} W", p.Torque * 1000, p.Power));
                }
                else
                {
                    double w = physics.HubSpeed(o.Id);
                    if (!double.IsNaN(w)) lines.Add(string.Format(Inv, "Dönüş          : {0:0} rpm", w * 60 / (2 * Math.PI)));
                    if (physics.RollerReadout(o.Id) is { } r)
                    {
                        lines.Add(r.Contact
                            ? string.Format(Inv, "Makara         : yük {0:0.00} N · çekiş {1:0.00} N", r.Normal, r.Traction)
                            : "Makara         : zemine değmiyor");
                    }
                }
            }

            if (o.Kind == WorldObjectKind.Component && WorldSession.IsLight(o.PartId != null ? Lib.Find(o.PartId) : null))
            {
                var l = _session.LightOutput(o);
                lines.Add(string.Format(Inv, "Işık akısı     : {0:0.##} lm", l.Lumens));
            }
        }
    }
}
