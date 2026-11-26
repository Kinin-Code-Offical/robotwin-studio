using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RobotTwin.CoreSim.Design;

namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [fault] What can go wrong with the cell's equipment, as it goes wrong in a plant: a dirty lens, a welded contact, a
    /// leaking seal, a stuck spool, a low supply, a lost network. A part carries at most one, in its parameters
    /// (<c>fault</c>, <c>fault_at</c> s, <c>fault_value</c>), and the simulation plays it from that time on, so a PLC program's
    /// reaction (a step that waits forever, a timeout alarm) can be tried before it meets the real fault.
    /// </summary>
    public static class MachineFaults
    {
        public sealed class Fault
        {
            internal Fault(string id, string partId, string name, string description, double? value = null, string unit = "")
            {
                Id = id;
                PartId = partId;
                Name = name;
                Description = description;
                DefaultValue = value;
                Unit = unit;
            }

            public string Id { get; }

            public string PartId { get; }

            public string Name { get; }

            public string Description { get; }

            /// <summary>The fault's size when it has one (the lens's remaining range, the leak, the supply), else null.</summary>
            public double? DefaultValue { get; }

            public string Unit { get; }
        }

        public const string Key = "fault", AtKey = "fault_at", ValueKey = "fault_value";

        public static readonly IReadOnlyList<Fault> All = new[]
        {
            new Fault("lens_dirty", CellParts.PhotoId, "Kirli lens", "Lensteki toz ve yağ ışığı zayıflatır: difüz sensörün menzili düşer, ışın sensörü her zaman kesik görür.", 40, "% menzil"),
            new Fault("output_stuck_on", CellParts.PhotoId, "Çıkış takılı (hep açık)", "Çıkış transistörü kısa devre: OUT cisim olsa da olmasa da yüksek."),
            new Fault("output_dead", CellParts.PhotoId, "Çıkış ölü (hep kapalı)", "Çıkış transistörü yanmış: OUT hiç yükselmez."),
            new Fault("contact_welded", CellParts.LimitId, "Kontak yapışık", "Aşırı akımla kaynayan kontak: C–NO kapalı kalır, C–NC açık kalır."),
            new Fault("lever_broken", CellParts.LimitId, "Kol kırık", "Makaralı kol kırılmış: hiçbir cisim anahtarı basamaz."),
            new Fault("leak", CellParts.CylinderId, "Conta kaçağı", "Piston contası aşınmış: hava ön hazneden atmosfere kaçar; silindir yavaşlar, ileride basınç ve kuvvet düşer.", 0.2, "L/(s·bar)"),
            new Fault("valve_stuck", CellParts.CylinderId, "Valf takılı", "Sürgü kirden takılmış: bobin enerjilense de bırakılsa da valf olduğu yerde kalır."),
            new Fault("low_supply", CellParts.CylinderId, "Düşük besleme", "Kompresör ya da hat sorunu: besleme basıncı düşer, silindirin kuvveti azalır.", 2.5, "bar"),
            new Fault("network_down", CellParts.ModbusIoId, "Ağ kopuk", "Kablo çıkmış ya da anahtar arızalı: modül Modbus isteklerine yanıt vermez; çıkışları son durumunda kalır."),
            // [sensorfault] Sensors, as they fail on a robot: a drifting gyro, a frozen or silent chip, a magnet near the
            // compass, a lost sky, a dirty window or lens, a dead emitter (Sandbox/Sensing/WorldSensing.cs).
            new Fault("imu_gyro_drift", "mpu6050", "Jiroskop kayması", "Isınma ya da darbe sonrası sıfır noktası kayar: jiroskop Z ekseni durgunken de dönüyor der; yönü tümleyen hesap kayar.", 5, "°/s"),
            new Fault("imu_vibration", "mpu6050", "Titreşim gürültüsü", "Motor ve pervane titreşimi ivmeölçere geçer: ölçümlere büyük gürültü eklenir, ivmeden bulunan eğim titrer.", 4, "m/s²"),
            new Fault("imu_frozen", "mpu6050", "Ölçüm donmuş", "Çip takılmış: yazmaçlar son değerde kalır, I²C yanıt verse de ölçüm değişmez."),
            new Fault("imu_offline", "mpu6050", "I²C yanıt vermiyor", "Gevşek kablo ya da yanmış çip: adresine yanıt (ACK) gelmez; okuma başarısız olur."),
            new Fault("compass_magnet", SensingParts.CompassId, "Yakında mıknatıs", "Motor mıknatısı ya da güç kablosu alanı bozar: X ekseninde sabit bir sapma, pusula yönü kayar.", 40, "µT"),
            new Fault("compass_offline", SensingParts.CompassId, "I²C yanıt vermiyor", "Gevşek kablo ya da yanmış çip: adresine yanıt (ACK) gelmez."),
            new Fault("gps_no_fix", SensingParts.GpsId, "Uydu kaybı", "Tünel, bina ya da ağaç altı: alıcı konum bulamaz; NMEA sürer ama konum geçersizdir."),
            new Fault("gps_multipath", SensingParts.GpsId, "Yansıma (çok yollu)", "Binalardan yansıyan sinyal konumu kaydırır: doğuya sabit bir sapma.", 8, "m"),
            new Fault("lidar_dirty", SensingParts.LidarId, "Kirli pencere", "Toz ve leke lazeri zayıflatır: menzil kısalır, uzaktaki engeller görünmez.", 30, "% menzil"),
            new Fault("lidar_stalled", SensingParts.LidarId, "Motor takıldı", "Dönen kafa takılmış: yeni tarama gelmez."),
            new Fault("line_dirty", SensingParts.Tcrt5000Id, "Kirli lens", "Lensteki toz yansıyan ışığı zayıflatır: sinyal düşer, siyah ile beyaz arası daralır.", 40, "% sinyal"),
            new Fault("line_led_dead", SensingParts.Tcrt5000Id, "IR LED yanmış", "Verici LED ışık vermez: alıcı yalnızca ortam ışığını görür."),
            new Fault("line_dirty", SensingParts.Qtr8aId, "Kirli lens", "Lensteki toz yansıyan ışığı zayıflatır: sinyal düşer, siyah ile beyaz arası daralır.", 40, "% sinyal"),
            new Fault("line_dirty", SensingParts.Qtr8rcId, "Kirli lens", "Lensteki toz yansıyan ışığı zayıflatır: sinyal düşer, siyah ile beyaz arası daralır.", 40, "% sinyal"),
            new Fault("line_dirty", SensingParts.Line5Id, "Kirli lens", "Lensteki toz yansıyan ışığı zayıflatır: sinyal düşer, siyah ile beyaz arası daralır.", 40, "% sinyal"),
        };

        /// <summary>The faults a part of <paramref name="partId"/> can have.</summary>
        public static IEnumerable<Fault> For(string? partId) => All.Where(f => f.PartId == partId);

        /// <summary>The fault an object carries (and its start time and size), or null.</summary>
        public static (Fault Fault, double At, double Value)? Of(WorldObject o)
        {
            if (!o.Parameters.TryGetValue(Key, out var id) || string.IsNullOrWhiteSpace(id)) return null;
            var f = All.FirstOrDefault(x => x.Id == id.Trim() && x.PartId == o.PartId);
            if (f == null) return null;
            double at = Number(o, AtKey, 0), value = Number(o, ValueKey, f.DefaultValue ?? 0);
            return (f, Math.Max(0, at), value);
        }

        public static void Set(WorldObject o, Fault f, double at = 0)
        {
            o.Parameters[Key] = f.Id;
            o.Parameters[AtKey] = at.ToString(CultureInfo.InvariantCulture);
            if (f.DefaultValue is { } v) o.Parameters[ValueKey] = v.ToString(CultureInfo.InvariantCulture);
            else o.Parameters.Remove(ValueKey);
        }

        public static void Clear(WorldObject o)
        {
            o.Parameters.Remove(Key);
            o.Parameters.Remove(AtKey);
            o.Parameters.Remove(ValueKey);
        }

        private static double Number(WorldObject o, string key, double fallback) =>
            o.Parameters.TryGetValue(key, out var s) && double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
    }
}
