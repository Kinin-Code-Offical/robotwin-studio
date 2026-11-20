using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using RobotTwin.CoreSim.Analysis.Cfd;
using RobotTwin.CoreSim.Analysis.Geometry;
using RobotTwin.CoreSim.Analysis.Jobs;
using RobotTwin.CoreSim.Analysis.Numerics;
using RobotTwin.CoreSim.Analysis.Recording;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Analysis
{
    /// <summary>
    /// [analysis] Offline "render/analysis mode": runs a world for a chosen duration at a chosen
    /// accuracy (not tied to the screen), records every signal, then computes the requested
    /// post-processing — FEM stress of selected parts under the recorded loads and a lattice-Boltzmann
    /// flow around a part. Real-time play may use shortcuts; this path computes everything.
    /// </summary>
    public static class AnalysisRun
    {
        /// <summary>Kinematic viscosity of air at about 20 °C (m²/s).</summary>
        public const double AirViscosity = 1.5e-5;

        public static AnalysisResult Run(WorldDocument world, AnalysisSettings settings, IWorldGeometry? geometry = null,
            CancellationToken cancel = default, Action<string, double>? progress = null)
        {
            var s = settings.Clone();
            var result = new AnalysisResult { Settings = s, WorldTitle = world.Title };
            var watch = Stopwatch.StartNew();
            var acc = s.AccuracyValues;
            var options = new RunnerOptions { Seed = s.Seed, Firmware = s.Firmware, MaxStep = acc.AnalogMaxStep, RelTol = acc.RelTol };
            var stressIds = s.Stress ? s.StressObjects.Where(id => world.Find(id) != null).ToList() : new List<string>();

            using (var runner = new WorldRunner(world, options, geometry))
            {
                var scene = runner.Physics?.Scene;
                if (scene != null)
                {
                    scene.MaxSubstep = acc.MaxSubstep;
                    scene.Iterations = Math.Max(scene.Iterations, acc.Iterations);
                    scene.SleepEnabled = acc.Sleep;
                }

                var probe = new WorldProbe(runner, s, result, stressIds);
                int n = s.SampleCount;
                double dt = 1.0 / s.OutputRate;
                probe.Sample();
                for (int i = 1; i <= n; i++)
                {
                    if (cancel.IsCancellationRequested)
                    {
                        result.Cancelled = true;
                        break;
                    }

                    runner.Step(dt);
                    probe.Sample();
                    if (runner.Halted)
                    {
                        result.HaltReason = runner.HaltReason;
                        break;
                    }

                    if ((i & 15) == 0) progress?.Invoke("Kayıt", (double)i / n);
                }

                probe.Detach();
                result.SimulatedSeconds = runner.Time;

                foreach (var id in stressIds)
                {
                    if (cancel.IsCancellationRequested) break;
                    var part = PartOf(world, scene, id);
                    if (part == null)
                    {
                        result.Note($"{id}: gerilme için şekil bulunamadı.");
                        continue;
                    }

                    result.Loads.TryGetValue(id, out var loads);
                    string label = "Gerilme: " + part.Name;
                    result.Stress.Add(StressJob.Run(part, s, (IReadOnlyList<LoadSample>?)loads ?? Array.Empty<LoadSample>(), cancel, p => progress?.Invoke(label, p)));
                }

                if (s.Aero && !cancel.IsCancellationRequested && s.AeroObject is { } aeroId && PartOf(world, scene, aeroId) is { } aeroPart)
                    result.Aero = Aero2D(aeroPart, s, scene?.AirDensity ?? 1.2, cancel, p => progress?.Invoke("Akış: " + aeroPart.Name, p));

                if (s.Thermal) result.Note("Isı analizi: kart güç haritası bu sürümde kayıtlı sinyallerden (sıcaklık kanalları) okunur.");
            }

            result.WallSeconds = watch.Elapsed.TotalSeconds;
            return result;
        }

        /// <summary>Geometry, material and pose of an object's physical body.</summary>
        public static PartGeometry? PartOf(WorldDocument world, PhysicsScene? scene, string objectId)
        {
            var o = world.Find(objectId);
            var body = scene?.FindBody(objectId);
            if (o == null || body == null) return null;
            var shape = body.EffectiveShape;
            var solid = Solid.FromShape(shape);
            if (solid == null || shape == null) return null;
            string kind = shape.Kind switch { ShapeKind.Box => "box", ShapeKind.Cylinder => "cylinder", ShapeKind.Sphere => "sphere", _ => "hull" };
            Vec3 size = shape.Kind switch
            {
                ShapeKind.Box => shape.HalfExtents,
                ShapeKind.Cylinder => new Vec3(shape.Radius, shape.Radius, shape.HalfHeight),
                ShapeKind.Sphere => new Vec3(shape.Radius, shape.Radius, shape.Radius),
                _ => solid.Bounds.Size * 0.5
            };
            return new PartGeometry
            {
                ObjectId = o.Id,
                Name = o.Name,
                Solid = solid,
                MaterialName = o.Material,
                IsStatic = body.IsStatic,
                Position = body.Position,
                Orientation = body.Orientation,
                Mass = body.Mass,
                Shape = kind,
                Size = size
            };
        }

        /// <summary>
        /// 2D lattice-Boltzmann flow in the vertical section through the part along the flow axis:
        /// drag/lift coefficients over time, vortex shedding frequency and the final velocity,
        /// pressure and vorticity fields. Above lattice Re ≈ 1000 a Smagorinsky model stands in for
        /// the unresolved turbulence, so the result is a large-eddy estimate there.
        /// </summary>
        public static CfdResult Aero2D(PartGeometry part, AnalysisSettings s, double airDensity, CancellationToken cancel, Action<double>? progress = null)
        {
            Vec3 flow = AxisVector(s.AeroAxis);
            Vec3 up = Vec3.UnitZ;
            Vec3 side = up.Cross(flow);
            var world = part.Solid.Transformed(part.Position, part.Orientation);
            var b = world.Bounds;
            Vec3 c = (b.Min + b.Max) * 0.5;
            Vec3 size = b.Size;
            double length = Math.Abs(size.Dot(flow));
            double height = size.Z;
            double width = Math.Abs(size.Dot(side));
            double d = Math.Max(Math.Max(height, length * 0.25), 1e-3);
            int res = Math.Max(8, s.AeroResolution);
            double dx = d / res;
            int up0 = 4 * res;
            int nx = up0 + (int)Math.Ceiling(length / dx) + 12 * res;
            int ny = 6 * res;
            double u = 0.06;
            double rePhys = s.AeroSpeed * d / AirViscosity;
            double reLat = Math.Min(rePhys, 1000);
            double tau = 3 * u * res / reLat + 0.5;
            var lbm = new Lbm2D(nx, ny, tau, u) { Smagorinsky = rePhys > reLat ? s.AeroSmagorinsky : 0 };
            double x0 = up0 * dx + length / 2;   // distance from the domain's left edge to the part's centre
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    var p = c + flow * ((x + 0.5) * dx - x0) + up * ((y + 0.5 - ny / 2.0) * dx);
                    lbm.Solid[lbm.Index(x, y)] = world.Contains(p);
                }
            }

            int frontal = 0;
            for (int y = 0; y < ny; y++)
            {
                for (int x = 0; x < nx; x++)
                {
                    if (lbm.Solid[lbm.Index(x, y)])
                    {
                        frontal++;
                        break;
                    }
                }
            }

            int steps = Math.Max(2000, (int)(s.AeroPasses * res / u));
            int record = steps / 2;
            var r = new CfdResult
            {
                ObjectId = part.ObjectId,
                Name = part.Name,
                Axis = s.AeroAxis,
                Nx = nx,
                Ny = ny,
                Dx = dx,
                Speed = s.AeroSpeed,
                ReferenceLength = d,
                ReferenceArea = Math.Max(frontal * dx, 1e-9) * Math.Max(width, dx),
                ReynoldsPhysical = rePhys,
                ReynoldsLattice = reLat,
                Tau = tau,
                LatticeSpeed = u,
                AirDensity = airDensity,
                TimeStep = dx * u / s.AeroSpeed
            };
            double h = Math.Max(frontal, 1);
            for (int step = 0; step < steps; step++)
            {
                if (cancel.IsCancellationRequested) break;
                lbm.Step();
                if (step >= record && (step & 7) == 0)
                {
                    r.Times.Add(step * r.TimeStep);
                    r.Cd.Add(lbm.BodyFx / (0.5 * u * u * h));
                    r.Cl.Add(lbm.BodyFy / (0.5 * u * u * h));
                }

                if ((step & 255) == 0) progress?.Invoke((double)step / steps);
            }

            r.Ux = (float[])lbm.Ux.Clone();
            r.Uy = (float[])lbm.Uy.Clone();
            r.Pressure = lbm.Rho.Select(x => (float)((x - 1) / 3)).ToArray();
            r.Vorticity = lbm.Vorticity();
            r.Solid = (bool[])lbm.Solid.Clone();
            if (r.Cd.Count > 0)
            {
                r.MeanCd = r.Cd.Average();
                r.MeanCl = r.Cl.Average();
                var sp = Spectral.AmplitudeSpectrum(r.Cl, 1.0 / 8);
                double f = sp.PeakFrequency(1e-5);   // cycles per lattice step
                r.Strouhal = f * h / u;
            }

            if (rePhys > reLat) r.Notes.Add($"Gerçek Re {rePhys:0} — çözünürlük için ağda Re {reLat:0} + Smagorinsky türbülans modeli kullanıldı.");
            r.Notes.Add("2B kesit akışı: sonsuz uzun profil varsayımı; Cd, ön yüz yüksekliğine göre.");
            return r;
        }

        private static Vec3 AxisVector(string axis) => (axis ?? "+X").ToUpperInvariant() switch
        {
            "-X" => new Vec3(-1, 0, 0),
            "+Y" or "Y" => Vec3.UnitY,
            "-Y" => new Vec3(0, -1, 0),
            _ => Vec3.UnitX
        };
    }
}
