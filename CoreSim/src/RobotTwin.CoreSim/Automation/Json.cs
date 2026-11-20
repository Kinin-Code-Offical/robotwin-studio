using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using RobotTwin.CoreSim.Mechanics;

namespace RobotTwin.CoreSim.Automation
{
    /// <summary>Error returned to an API client (JSON-RPC error object).</summary>
    public sealed class ApiException : Exception
    {
        public const int ParseError = -32700, InvalidRequest = -32600, MethodNotFound = -32601, InvalidParams = -32602, InternalError = -32603;

        /// <summary>No world/vehicle is loaded.</summary>
        public const int NotLoaded = -32001;

        /// <summary>The named object, sensor, actuator, board or pin does not exist.</summary>
        public const int NotFound = -32002;

        /// <summary>The loaded simulation cannot do this (e.g. flight commands on a ground robot).</summary>
        public const int Unsupported = -32003;

        /// <summary>Authentication token missing or wrong.</summary>
        public const int Unauthorized = -32004;

        public ApiException(int code, string message) : base(message)
        {
            Code = code;
        }

        public int Code { get; }

        public static ApiException Missing(string what) => new ApiException(NotFound, what + " bulunamadı / not found");

        public static ApiException BadParam(string name, string why) => new ApiException(InvalidParams, "'" + name + "': " + why);
    }

    /// <summary>Small JSON helpers shared by the runner, sensors and the dispatcher.</summary>
    public static class J
    {
        /// <summary>Compact output that keeps Turkish characters readable (no HTML escaping needed on this wire).</summary>
        public static readonly System.Text.Json.JsonSerializerOptions Wire = new System.Text.Json.JsonSerializerOptions
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        /// <summary>A finite number, or null (JSON has no NaN/Infinity: non-finite values travel as null).</summary>
        public static JsonNode? N(double v) => IsFinite(v) ? JsonValue.Create(Math.Round(v, 9)) : null;

        public static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        /// <summary>Full-precision number (for files that must reproduce a run exactly), or null.</summary>
        public static JsonNode? Exact(double v) => IsFinite(v) ? JsonValue.Create(v) : null;

        public static JsonArray V(Vec3 v) => new JsonArray(N(v.X), N(v.Y), N(v.Z));

        public static JsonArray VExact(Vec3 v) => new JsonArray(Exact(v.X), Exact(v.Y), Exact(v.Z));

        /// <summary>Quaternion in ROS order [x, y, z, w].</summary>
        public static JsonArray Q(Quat q) => new JsonArray(N(q.X), N(q.Y), N(q.Z), N(q.W));

        public static JsonArray Arr(IEnumerable<double> values)
        {
            var a = new JsonArray();
            foreach (var v in values) a.Add(N(v));
            return a;
        }

        public static JsonArray Strs(IEnumerable<string> values)
        {
            var a = new JsonArray();
            foreach (var v in values) a.Add(JsonValue.Create(v));
            return a;
        }

        // ------------------------------------------------------------------ parameters

        public static JsonNode? Get(JsonObject? p, string name) => p != null && p.TryGetPropertyValue(name, out var n) ? n : null;

        public static bool Has(JsonObject? p, string name) => Get(p, name) != null;

        public static string? Str(JsonObject? p, string name)
        {
            var n = Get(p, name);
            if (n == null) return null;
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out string? s)) return s;
                if (TryNumber(v, out double d)) return d.ToString(CultureInfo.InvariantCulture);
                if (v.TryGetValue(out bool b)) return b ? "true" : "false";
            }

            return n.ToJsonString();
        }

        public static string ReqStr(JsonObject? p, string name) =>
            Str(p, name) ?? throw ApiException.BadParam(name, "gerekli / required");

        /// <summary>Any numeric JSON value (parsed, or created from int/long/float/decimal in-process).</summary>
        public static bool TryNumber(JsonNode? n, out double d)
        {
            d = double.NaN;
            if (!(n is JsonValue v)) return false;
            if (v.TryGetValue(out d)) return true;
            if (v.TryGetValue(out int i)) { d = i; return true; }
            if (v.TryGetValue(out long l)) { d = l; return true; }
            if (v.TryGetValue(out float f)) { d = f; return true; }
            if (v.TryGetValue(out decimal m)) { d = (double)m; return true; }
            if (v.TryGetValue(out uint ui)) { d = ui; return true; }
            if (v.TryGetValue(out ulong ul)) { d = ul; return true; }
            if (v.TryGetValue(out short sh)) { d = sh; return true; }
            if (v.TryGetValue(out byte by)) { d = by; return true; }
            if (v.TryGetValue(out System.Text.Json.JsonElement e) && e.ValueKind == System.Text.Json.JsonValueKind.Number) { d = e.GetDouble(); return true; }
            return false;
        }

        public static double? Num(JsonObject? p, string name)
        {
            var n = Get(p, name);
            if (n is JsonValue v)
            {
                if (TryNumber(v, out double d)) return d;
                if (v.TryGetValue(out string? s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
                if (v.TryGetValue(out bool b)) return b ? 1 : 0;
            }

            if (n != null) throw ApiException.BadParam(name, "sayı bekleniyor / number expected");
            return null;
        }

        public static double Num(JsonObject? p, string name, double fallback) => Num(p, name) ?? fallback;

        public static double ReqNum(JsonObject? p, string name) =>
            Num(p, name) ?? throw ApiException.BadParam(name, "gerekli / required");

        public static int Int(JsonObject? p, string name, int fallback) => (int)Math.Round(Num(p, name) ?? fallback);

        public static bool Bool(JsonObject? p, string name, bool fallback)
        {
            var n = Get(p, name);
            if (n is JsonValue v)
            {
                if (v.TryGetValue(out bool b)) return b;
                if (TryNumber(v, out double d)) return d != 0;
                if (v.TryGetValue(out string? s)) return s == "true" || s == "1" || s == "on" || s == "high";
            }

            return fallback;
        }

        public static Vec3? Vec(JsonObject? p, string name)
        {
            if (!(Get(p, name) is JsonArray a)) return null;
            if (a.Count < 3) throw ApiException.BadParam(name, "[x, y, z] bekleniyor / expected");
            return new Vec3(Item(a, 0), Item(a, 1), Item(a, 2));
        }

        /// <summary>[x, y, z, w] quaternion.</summary>
        public static Quat? Quat(JsonObject? p, string name)
        {
            if (!(Get(p, name) is JsonArray a)) return null;
            if (a.Count < 4) throw ApiException.BadParam(name, "[x, y, z, w] bekleniyor / expected");
            return new Mechanics.Quat(Item(a, 3), Item(a, 0), Item(a, 1), Item(a, 2)).Normalized();
        }

        public static double Item(JsonArray a, int i)
        {
            return TryNumber(a[i], out double d) ? d : double.NaN;
        }

        public static List<string> StrList(JsonObject? p, string name)
        {
            var list = new List<string>();
            var n = Get(p, name);
            if (n is JsonArray a)
            {
                foreach (var x in a)
                {
                    if (x is JsonValue v && v.TryGetValue(out string? s) && s != null) list.Add(s);
                }
            }
            else if (n is JsonValue v1 && v1.TryGetValue(out string? one) && one != null)
            {
                list.Add(one);
            }

            return list;
        }

        /// <summary>Roll-pitch-yaw (radians, applied X then Y then Z like <see cref="Sandbox.WorldPose"/>) to a quaternion.</summary>
        public static Quat FromRpy(double roll, double pitch, double yaw) =>
            Mechanics.Quat.FromAxisAngle(Vec3.UnitZ, yaw) * Mechanics.Quat.FromAxisAngle(Vec3.UnitY, pitch) * Mechanics.Quat.FromAxisAngle(Vec3.UnitX, roll);

        /// <summary>Stable 32-bit FNV-1a hash (string.GetHashCode differs between processes).</summary>
        public static int StableHash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s)
                {
                    h ^= c;
                    h *= 16777619;
                }

                return (int)(h & 0x7FFFFFFF);
            }
        }
    }

    /// <summary>Deterministic Gaussian noise source (seeded per sensor).</summary>
    public sealed class NoiseSource
    {
        private readonly Random _random;
        private double _spare;
        private bool _hasSpare;

        public NoiseSource(int seed)
        {
            _random = new Random(seed);
        }

        public double Uniform() => _random.NextDouble();

        public double Gaussian(double sigma)
        {
            if (sigma <= 0) return 0;
            if (_hasSpare)
            {
                _hasSpare = false;
                return _spare * sigma;
            }

            double u, v, s;
            do
            {
                u = _random.NextDouble() * 2 - 1;
                v = _random.NextDouble() * 2 - 1;
                s = u * u + v * v;
            }
            while (s >= 1 || s == 0);
            double f = Math.Sqrt(-2 * Math.Log(s) / s);
            _spare = v * f;
            _hasSpare = true;
            return u * f * sigma;
        }
    }
}
