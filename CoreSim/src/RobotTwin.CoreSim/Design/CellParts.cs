using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [arm] Industrial robot cell equipment: robot arms with their controller I/O, end
    /// effectors (parallel gripper, vacuum cup), belt conveyor with photo-eye and part feeder,
    /// safety light curtain, pallets, fence panels and robot pedestals. Arms, conveyors and light
    /// curtains have real digital I/O (cables to boards or to each other); the rest are purely
    /// mechanical. Their motion and physics live in Sandbox/WorldPhysics.Machines.cs.
    /// </summary>
    public static class CellParts
    {
        public const string Category = "Robot Hücresi";

        public const string ArmId = "robot-arm";
        public const string GripperId = "gripper-parallel";
        public const string VacuumId = "vacuum-cup";
        public const string ConveyorId = "conveyor";
        public const string CurtainId = "light-curtain";
        public const string PalletId = "pallet";
        public const string FenceId = "fence-panel";
        public const string PedestalId = "robot-pedestal";

        /// <summary>Digital inputs and outputs of the arm controller, in pin order.</summary>
        public static readonly string[] ArmInputs = { "DI1", "DI2", "DI3", "DI4", "START", "STOP", "SAFE" };
        public static readonly string[] ArmOutputs = { "DO1", "DO2", "DO3", "DO4", "BUSY", "FAULT" };

        public static bool IsCellPart(string? partId) =>
            partId == ArmId || partId == GripperId || partId == VacuumId || partId == ConveyorId || partId == CurtainId ||
            partId == PalletId || partId == FenceId || partId == PedestalId;

        public static bool IsTool(string? partId) => partId == GripperId || partId == VacuumId;

        public static void Register(PartLibrary lib)
        {
            if (lib == null) throw new ArgumentNullException(nameof(lib));

            // ---------------------------------------------------------------- robot arm + controller
            var arm = Part(lib, ArmId, "Robot Kol (kontrolörlü)", "ROB",
                "Seri robot kol ve kontrolörü: eksen sınırları, hız/ivme ve tork değerleri gerçekçi; ileri/ters kinematik, eksen (MOVEJ) ve doğrusal (MOVEL) hareket, öğretme paneli programı. " +
                "G/Ç: DI1–DI4 genel giriş, START yükselen kenarda programı başlatır, STOP yüksekken duraklatır, SAFE bağlıysa güvenlik devresidir (düşükse kontrollü durur). DO1–DO4 genel çıkış, BUSY program çalışırken, FAULT arızada yüksek.",
                new ParameterDefinition("model", "Model", "RW6-850", "", new[] { "RW6-850", "RWS-500", "RWD-6" }),
                new ParameterDefinition("q0", "Başlangıç eksenleri", "", "°/mm"),
                new ParameterDefinition("autostart", "Açılışta programı başlat", "false", "", new[] { "true", "false" }),
                new ParameterDefinition("loop", "Programı döngüde çalıştır", "true", "", new[] { "true", "false" }),
                new ParameterDefinition("profile", "Hız profili", "S-eğrisi", "", new[] { "S-eğrisi", "trapez" }),
                new ParameterDefinition("override", "Hız ayarı", "100", "%"),
                new ParameterDefinition("safety_restart", "Güvenlik sonrası", "elle", "", new[] { "elle", "otomatik" }),
                new ParameterDefinition("io_v", "G/Ç gerilimi", "5", "V", new[] { "5", "24" }));
            arm.Build = c => BuildIo(c, ArmInputs, ArmOutputs);
            AddPins(arm, ArmInputs, ArmOutputs);
            Preset(lib, "arm-rw6-850", "6 Eksenli Endüstriyel Kol (5 kg · 850 mm)", ArmId, "Küresel bilekli 6 eksen; J1 370°/s … J6 1000°/s, ±0.03 mm", "model=RW6-850");
            Preset(lib, "arm-rws-500", "SCARA Robot (3 kg · 500 mm)", ArmId, "4 eksen, 150 mm Z stroku, hızlı al-bırak için", "model=RWS-500");
            Preset(lib, "arm-rwd-6", "Masaüstü 6 Eksen Servo Kol", ArmId, "Metal dişli servolar, 150 g yük, eğitim/hobi", "model=RWD-6");

            // ---------------------------------------------------------------- end effectors
            var gripper = Part(lib, GripperId, "Paralel Tutucu (2 parmak)", "EOAT",
                "Elektrikli paralel tutucu: robot flanşına takılır, parmaklar kuvvet sınırlı kapanır. Parça sürtünmeyle tutulur: ağır ya da kaygan parça kayar.",
                new ParameterDefinition("stroke", "Strok (toplam)", "60", "mm"),
                new ParameterDefinition("force", "Kavrama kuvveti (parmak başı)", "80", "N"),
                new ParameterDefinition("speed", "Kapanma hızı", "60", "mm/s"),
                new ParameterDefinition("pad", "Parmak yüzeyi", "kauçuk", "", new[] { "kauçuk", "alüminyum" }));
            Preset(lib, "gripper-2f-60", "Paralel Tutucu · 60 mm strok, 80 N", GripperId, "Kauçuk pedli iki parmak, kuvvet sınırlı");
            var vacuum = Part(lib, VacuumId, "Vakum Vantuz", "EOAT",
                "Körüklü vakum vantuzu: düz bir yüzeye oturup sızdırmazlık sağlanınca tutar; tutma kuvveti = vakum × vantuz alanı. Eğik/kenar temasta tutmaz.",
                new ParameterDefinition("diameter", "Vantuz çapı", "50", "mm"),
                new ParameterDefinition("vacuum", "Vakum seviyesi", "65", "kPa"),
                new ParameterDefinition("cups", "Vantuz sayısı", "1", "", new[] { "1", "2", "4" }));
            Preset(lib, "vacuum-cup-50", "Vakum Vantuz · Ø50 mm, −65 kPa", VacuumId, "Tek vantuz, ~100 N tutma (yaklaşık)");
            Preset(lib, "vacuum-cup-4x40", "Vakum Tutucu · 4 × Ø40 mm", VacuumId, "Kutu/plaka taşımak için dört vantuzlu plaka", "diameter=40;cups=4");

            // ---------------------------------------------------------------- conveyor
            var conveyor = Part(lib, ConveyorId, "Bantlı Konveyör", "CV",
                "Motorlu kauçuk bant: üstündeki parçaları sürtünmeyle taşır. Bant sonunda dayama ve fotosel (SENSOR yüksek = parça var), isteğe bağlı parça besleyici. RUN bağlıysa bant sadece RUN yüksekken döner.",
                new ParameterDefinition("length", "Boy", "1.6", "m"),
                new ParameterDefinition("width", "Bant genişliği", "0.30", "m"),
                new ParameterDefinition("height", "Bant yüksekliği", "0.60", "m"),
                new ParameterDefinition("speed", "Bant hızı", "0.20", "m/s"),
                new ParameterDefinition("running", "Çalışıyor", "true", "", new[] { "true", "false" }),
                new ParameterDefinition("stopper", "Sonunda dayama", "true", "", new[] { "true", "false" }),
                new ParameterDefinition("feed", "Parça besleyici", "yok", "", new[] { "yok", "kutu" }),
                new ParameterDefinition("feed_period", "Besleme aralığı", "6", "s"),
                new ParameterDefinition("box", "Kutu ölçüsü (mm)", "160x120x100", ""),
                new ParameterDefinition("box_mass", "Kutu kütlesi", "0.8", "kg"),
                new ParameterDefinition("io_v", "G/Ç gerilimi", "5", "V", new[] { "5", "24" }));
            conveyor.Build = c => BuildIo(c, new[] { "RUN" }, new[] { "SENSOR" });
            AddPins(conveyor, new[] { "RUN" }, new[] { "SENSOR" });
            Preset(lib, "conveyor-1600", "Bantlı Konveyör · 1.6 m, 0.2 m/s", ConveyorId, "Fotoselli, dayamalı, isteğe bağlı kutu besleyici");
            Preset(lib, "conveyor-feed", "Konveyör + Kutu Besleyici", ConveyorId, "Her 6 s'de bir 160×120×100 mm kutu", "feed=kutu");

            // ---------------------------------------------------------------- safety light curtain
            var curtain = Part(lib, CurtainId, "Güvenlik Işık Perdesi", "LC",
                "İki direk arasında kızılötesi ışın perdesi (tip 4 benzeri). Bir ışın kesilince OSSD düşük olur; robotun SAFE girişine bağlayın. Bağlı değilse aynı dünyadaki kollar perdeyi doğrudan izler.",
                new ParameterDefinition("width", "Koruma genişliği", "0.9", "m"),
                new ParameterDefinition("height", "Koruma yüksekliği", "1.2", "m"),
                new ParameterDefinition("beams", "Işın sayısı", "12", ""),
                new ParameterDefinition("io_v", "G/Ç gerilimi", "5", "V", new[] { "5", "24" }));
            curtain.Build = c => BuildIo(c, Array.Empty<string>(), new[] { "OSSD" });
            AddPins(curtain, Array.Empty<string>(), new[] { "OSSD" });
            Preset(lib, "light-curtain-900", "Güvenlik Işık Perdesi · 0.9 × 1.2 m", CurtainId, "12 ışın, OSSD çıkışı");

            // ---------------------------------------------------------------- passive equipment
            var pallet = Part(lib, PalletId, "Euro palet", "PAL",   // not "Palet" (the editor's palette)
                "Ahşap palet: robotun yerleştirdiği parçaları taşır. Dolunca program PALETSONRAKI ile değiştirir.",
                new ParameterDefinition("size_x", "Boy", "0.8", "m"),
                new ParameterDefinition("size_y", "En", "0.6", "m"),
                new ParameterDefinition("height", "Yükseklik", "0.144", "m"));
            Preset(lib, "pallet-800x600", "Euro palet · 800 × 600 mm", PalletId, "Yarım palet, ahşap");
            Preset(lib, "pallet-1200x800", "Euro palet · 1200 × 800 mm", PalletId, "Standart ölçü, ahşap", "size_x=1.2;size_y=0.8");
            var fence = Part(lib, FenceId, "Güvenlik Çiti Paneli", "FN",
                "Tel örgü çit paneli ve direkleri: robot hücresini çevreler, gerçek engel olarak çarpışır.",
                new ParameterDefinition("width", "Genişlik", "1.0", "m"),
                new ParameterDefinition("height", "Yükseklik", "1.6", "m"));
            Preset(lib, "fence-1000", "Çit Paneli · 1.0 × 1.6 m", FenceId, "Sarı direkli tel örgü");
            Preset(lib, "fence-1500", "Çit Paneli · 1.5 × 1.6 m", FenceId, "Sarı direkli tel örgü", "width=1.5");
            var pedestal = Part(lib, PedestalId, "Robot Kaidesi", "PED",
                "Çelik robot kaidesi: kolu yükseltir, zemine ankrajlıdır.",
                new ParameterDefinition("height", "Yükseklik", "0.40", "m"),
                new ParameterDefinition("size", "Taban kenarı", "0.40", "m"));
            Preset(lib, "robot-pedestal-400", "Robot Kaidesi · 400 mm", PedestalId, "Çelik, ankrajlı");
        }

        private static PartDefinition Part(PartLibrary lib, string id, string name, string prefix, string description, params ParameterDefinition[] parameters)
        {
            var part = new PartDefinition
            {
                Id = id,
                Name = name,
                Category = Category,
                RefPrefix = prefix,
                Symbol = SymbolKind.Module,
                Footprint = string.Empty,
                ValueKey = "model",
                Description = description,
                HalfWidth = 4,
                HalfHeight = 4
            };
            part.Parameters.AddRange(parameters);
            lib.Add(part);
            return part;
        }

        private static void AddPins(PartDefinition part, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs)
        {
            int pad = 1;
            for (int i = 0; i < inputs.Count; i++) part.Pins.Add(new PinDefinition(inputs[i], (pad++).ToString(System.Globalization.CultureInfo.InvariantCulture), -6, -6 + i * 2));
            for (int i = 0; i < outputs.Count; i++) part.Pins.Add(new PinDefinition(outputs[i], (pad++).ToString(System.Globalization.CultureInfo.InvariantCulture), 6, -6 + i * 2));
            part.Pins.Add(new PinDefinition("GND", (pad++).ToString(System.Globalization.CultureInfo.InvariantCulture), 0, 8));
            part.HalfHeight = Math.Max(4, Math.Max(inputs.Count, outputs.Count) + 3);
        }

        private static void Preset(PartLibrary lib, string id, string name, string partId, string description, string parameters = "")
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in parameters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) map[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }

            lib.AddPreset(new PartPreset(id, name, partId, Category, description, map));
        }

        /// <summary>
        /// Controller I/O: an internal I/O supply (5 V or 24 V), sinking inputs (10 kΩ load,
        /// Schmitt trigger at 30/60 % of the I/O supply) and push-pull outputs (50 Ω).
        /// </summary>
        private static void BuildIo(PartBuildContext c, IReadOnlyList<string> inputs, IReadOnlyList<string> outputs)
        {
            int gnd = c.Pin("GND");
            int vio = c.Internal("VIO");
            double v = c.Num("io_v", 5);
            c.Add(new VoltageSource(c.Sub("VIO"), vio, gnd, new DcWaveform(v <= 0 ? 5 : v)) { SeriesResistance = 0.05 });
            foreach (var name in inputs)
            {
                int pin = c.Pin(name);
                c.Add(new DigitalPin(c.Sub(name), pin, vio, gnd) { Mode = PinMode.Input, AbsoluteMaxCurrent = 1.0 });
                c.Add(new Resistor(c.Sub("R" + name), pin, gnd, 10e3));
            }

            foreach (var name in outputs)
            {
                c.Add(new DigitalPin(c.Sub(name), c.Pin(name), vio, gnd) { Mode = PinMode.OutputLow, DriveResistance = 50, AbsoluteMaxCurrent = 0.5 });
            }
        }

        // ------------------------------------------------------------------ helpers for world code

        /// <summary>Reads a numeric parameter ("1.6", "0,2") with a fallback.</summary>
        public static double Num(IReadOnlyDictionary<string, string> p, string key, double fallback)
        {
            if (!p.TryGetValue(key, out var s) || string.IsNullOrWhiteSpace(s)) return fallback;
            return SiValue.Parse(s.Replace(',', '.'), fallback);
        }

        public static bool Flag(IReadOnlyDictionary<string, string> p, string key, bool fallback)
        {
            if (!p.TryGetValue(key, out var s) || string.IsNullOrWhiteSpace(s)) return fallback;
            s = s.Trim().ToLowerInvariant();
            return s == "true" || s == "1" || s == "evet" || s == "açık" || s == "on";
        }

        /// <summary>"160x120x100" (mm) → metres.</summary>
        public static (double X, double Y, double Z) Size3(IReadOnlyDictionary<string, string> p, string key, (double, double, double) fallback)
        {
            if (!p.TryGetValue(key, out var s) || string.IsNullOrWhiteSpace(s)) return fallback;
            var parts = s.ToLowerInvariant().Replace('×', 'x').Split('x');
            if (parts.Length != 3) return fallback;
            var r = new double[3];
            for (int i = 0; i < 3; i++)
            {
                if (!double.TryParse(parts[i].Trim().Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r[i]) || r[i] <= 0) return fallback;
            }

            return (r[0] / 1000, r[1] / 1000, r[2] / 1000);
        }

        /// <summary>Approximate shipping masses (kg) of the cell parts (for the world's mass estimate).</summary>
        public static double? MassOf(string partId, IReadOnlyDictionary<string, string> p) => partId switch
        {
            ArmId => (p.TryGetValue("model", out var m) ? m : "") switch
            {
                "RWS-500" => 17.0,
                "RWD-6" => 0.65,
                _ => 31.0
            },
            GripperId => 0.9,
            VacuumId => 0.35,
            ConveyorId => 25 * Num(p, "length", 1.6),
            CurtainId => 4.0,
            PalletId => 25 * Num(p, "size_x", 0.8) * Num(p, "size_y", 0.6) / 0.96,
            FenceId => 12 * Num(p, "width", 1.0),
            PedestalId => 60.0,
            _ => null
        };
    }
}
