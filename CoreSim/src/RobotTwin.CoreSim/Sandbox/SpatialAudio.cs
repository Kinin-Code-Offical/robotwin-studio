using System;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>Where a listener (the 3D camera) is and which way it looks.</summary>
    public readonly struct AudioListener
    {
        public AudioListener(Vec3 position, Vec3 forward, Vec3 up, Vec3 velocity = default)
        {
            Position = position;
            Forward = forward.LengthSquared > 1e-12 ? forward.Normalized() : Vec3.UnitY;
            var u = up.LengthSquared > 1e-12 ? up.Normalized() : Vec3.UnitZ;
            var right = Forward.Cross(u);
            Right = right.LengthSquared > 1e-12 ? right.Normalized() : Vec3.UnitX;
            Up = Right.Cross(Forward).Normalized();
            Velocity = velocity;
        }

        public Vec3 Position { get; }
        public Vec3 Forward { get; }
        public Vec3 Right { get; }
        public Vec3 Up { get; }
        public Vec3 Velocity { get; }
    }

    /// <summary>How one positioned sound reaches the two ears.</summary>
    public readonly struct SpatialMix
    {
        public SpatialMix(double left, double right, double cutoffLeft, double cutoffRight, double pitch, double distance, double pan, double front)
        {
            Left = left;
            Right = right;
            CutoffLeft = cutoffLeft;
            CutoffRight = cutoffRight;
            Pitch = pitch;
            Distance = distance;
            Pan = pan;
            Front = front;
        }

        /// <summary>Channel gains (distance attenuation × panning; 1/1 = centred at the reference distance).</summary>
        public double Left { get; }
        public double Right { get; }

        /// <summary>Low-pass cut-off per ear (Hz): the far ear and sounds from behind lose their highs.</summary>
        public double CutoffLeft { get; }
        public double CutoffRight { get; }

        /// <summary>Doppler pitch factor (1 = still).</summary>
        public double Pitch { get; }

        public double Distance { get; }

        /// <summary>−1 hard left … +1 hard right.</summary>
        public double Pan { get; }

        /// <summary>+1 straight ahead … −1 straight behind.</summary>
        public double Front { get; }

        /// <summary>Not positioned: both channels at full level, no filtering.</summary>
        public static SpatialMix Centre { get; } = new SpatialMix(1, 1, 20000, 20000, 1, 0, 0, 1);
    }

    /// <summary>
    /// Positional audio maths: inverse-distance attenuation with a reference distance
    /// (OpenAL "inverse distance clamped"), constant-power stereo panning from the
    /// camera-relative direction, head shadow (the far ear and sounds from behind are
    /// muffled), air absorption with distance, and a gentle Doppler shift.
    /// </summary>
    public static class SpatialAudio
    {
        public const double SpeedOfSound = 343.0;

        /// <summary>Distance at which a sound plays at its nominal level (m).</summary>
        public const double ReferenceDistance = 0.5;

        /// <summary>Gain at a distance: ref / (ref + rolloff·(d − ref)), clamped to [ref, max].</summary>
        public static double Attenuation(double distance, double reference = ReferenceDistance, double rolloff = 1.0, double max = 60.0)
        {
            double d = Math.Max(reference, Math.Min(max, distance));
            return reference / (reference + rolloff * (d - reference));
        }

        /// <summary>Constant-power pan law: pan −1 … +1 → (left, right) with L² + R² = 2 (1/1 at the centre).</summary>
        public static (double Left, double Right) Pan(double pan)
        {
            double p = Math.Max(-1, Math.Min(1, pan));
            double a = (p + 1) * Math.PI / 4;
            return (Math.Sqrt(2) * Math.Cos(a), Math.Sqrt(2) * Math.Sin(a));
        }

        /// <summary>Doppler factor for a source and listener moving with the given velocities (clamped to ±12 %).</summary>
        public static double Doppler(Vec3 toSource, Vec3 sourceVelocity, Vec3 listenerVelocity)
        {
            double len = toSource.Length;
            if (len < 1e-6) return 1;
            var dir = toSource / len;   // listener → source
            double vl = listenerVelocity.Dot(dir);   // listener moving toward the source raises pitch
            double vs = sourceVelocity.Dot(dir);     // source moving away (along dir) lowers it
            double f = (SpeedOfSound + vl) / Math.Max(1, SpeedOfSound + vs);
            return Math.Max(0.88, Math.Min(1.12, f));
        }

        /// <summary>How a sound at <paramref name="source"/> reaches the listener.</summary>
        public static SpatialMix Compute(in AudioListener listener, Vec3 source, Vec3 sourceVelocity = default, double reference = ReferenceDistance)
        {
            var to = source - listener.Position;
            double d = to.Length;
            if (d < 1e-6) return SpatialMix.Centre;
            var dir = to / d;
            double pan = dir.Dot(listener.Right);
            double front = dir.Dot(listener.Forward);
            double gain = Attenuation(d, reference);
            // Close to the head the source is heard by both ears; panning grows with distance.
            double spread = Math.Min(1, d / (reference * 0.6));
            var (l, r) = Pan(pan * spread);
            // Behind: a little quieter (pinna shadow).
            double behind = Math.Max(0, -front);
            gain *= 1 - 0.25 * behind;
            // Head shadow on the far ear, pinna shadow from behind, air absorption with distance.
            double air = 20000 / (1 + d / 15.0);
            double rear = 1 - 0.75 * behind;
            double cutL = Math.Max(600, air * rear * (1 - 0.7 * Math.Max(0, pan) * spread));
            double cutR = Math.Max(600, air * rear * (1 - 0.7 * Math.Max(0, -pan) * spread));
            double pitch = Doppler(to, sourceVelocity, listener.Velocity);
            return new SpatialMix(l * gain, r * gain, cutL, cutR, pitch, d, pan, front);
        }
    }
}
