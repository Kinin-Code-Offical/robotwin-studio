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
        public const string ModbusIoId = "modbus-io";
        public const string PhotoId = "photo-sensor";
        public const string LimitId = "limit-switch";
        public const string CylinderId = "pneumatic-cylinder";
        public const string PlcId = "plc";

        /// <summary>[plc] The PLC's programming languages: its program is in "program" (ST) or "ladder" (LD, as JSON).</summary>
        public const string PlcStructuredText = "Yapısal Metin", PlcLadder = "Merdiven";

        /// <summary>[plc] The PLC's inputs and outputs, in %IX0.0… / %QX0.0… order.</summary>
        public static readonly string[] PlcInputs = { "I0", "I1", "I2", "I3", "I4", "I5", "I6", "I7" };
        public static readonly string[] PlcOutputs = { "Q0", "Q1", "Q2", "Q3", "Q4", "Q5", "Q6", "Q7" };

        /// <summary>[plc] The program a new PLC starts with: a lamp, a flasher and a pusher station's steps.</summary>
        public const string PlcExample =
            "(* Örnek: I0'a basılıyken Q0 yanar, Q1 saniyede bir yanıp söner.\n" +
            "   I1 parça var, I2 silindir ileride, I3 silindir geride: Q2 silindiri sürer. *)\n" +
            "VAR\n" +
            "    Dugme AT %IX0.0 : BOOL;\n" +
            "    Parca AT %IX0.1 : BOOL;\n" +
            "    Ileride AT %IX0.2 : BOOL;\n" +
            "    Geride AT %IX0.3 : BOOL;\n" +
            "    Lamba AT %QX0.0 : BOOL;\n" +
            "    Flas AT %QX0.1 : BOOL;\n" +
            "    Itici AT %QX0.2 : BOOL;\n" +
            "    T1 : TON;\n" +
            "    Adim : INT := 0;\n" +
            "    Sayi AT %MW0 : INT;\n" +
            "END_VAR\n" +
            "\n" +
            "Lamba := Dugme;\n" +
            "T1(IN := NOT T1.Q, PT := T#500ms);\n" +
            "IF T1.Q THEN Flas := NOT Flas; END_IF\n" +
            "\n" +
            "CASE Adim OF\n" +
            "    0:  IF Parca AND Geride THEN Adim := 10; END_IF\n" +
            "    10: Itici := TRUE;\n" +
            "        IF Ileride THEN Adim := 20; END_IF\n" +
            "    20: Itici := FALSE;\n" +
            "        IF Geride THEN Sayi := Sayi + 1; Adim := 0; END_IF\n" +
            "END_CASE\n";

        /// <summary>A photoelectric sensor's ways of seeing and its output's sense.</summary>
        public const string PhotoDiffuse = "difüz", PhotoRetro = "reflektörlü", PhotoThrough = "karşılıklı", PhotoOnPresent = "cisim varken", PhotoOnAbsent = "cisim yokken";

        /// <summary>Digital inputs and outputs of the arm controller, in pin order.</summary>
        public static readonly string[] ArmInputs = { "DI1", "DI2", "DI3", "DI4", "START", "STOP", "SAFE" };
        public static readonly string[] ArmOutputs = { "DO1", "DO2", "DO3", "DO4", "BUSY", "FAULT" };

        /// <summary>[modbus] The remote I/O module's inputs and outputs, in coil / discrete input order.</summary>
        public static readonly string[] ModbusInputs = { "DI1", "DI2", "DI3", "DI4", "DI5", "DI6", "DI7", "DI8" };
        public static readonly string[] ModbusOutputs = { "DO1", "DO2", "DO3", "DO4", "DO5", "DO6", "DO7", "DO8" };

        /// <summary>[modbus] Its 0–10 V analog inputs (input registers 1–4) and outputs (holding registers 1–2), in millivolts at 12 bits.</summary>
        public static readonly string[] ModbusAnalogInputs = { "AI1", "AI2", "AI3", "AI4" };
        public static readonly string[] ModbusAnalogOutputs = { "AO1", "AO2" };

        public static bool IsCellPart(string? partId) =>
            partId == ArmId || partId == GripperId || partId == VacuumId || partId == ConveyorId || partId == CurtainId ||
            partId == PalletId || partId == FenceId || partId == PedestalId || partId == ModbusIoId || partId == PhotoId || partId == LimitId || partId == CylinderId || partId == PlcId;

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
                new ParameterDefinition("pathcheck", "Yolda çarpışma denetimi", "uyar", "", new[] { "uyar", "durdur", "kapalı" }),
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

            // ---------------------------------------------------------------- [modbus] remote I/O on Modbus TCP
            var modbus = Part(lib, ModbusIoId, "Modbus Uzak G/Ç Modülü", "IO",
                "8 sayısal giriş, 8 sayısal çıkış; dünya çalışırken bir Modbus TCP sunucusudur, PLC, HMI ya da SCADA programı gerçek modül gibi bağlanır. " +
                "Bobin 0–7 → DO1–DO8 (yazılır), ayrık giriş 0–7 ← DI1–DI8 (okunur); giriş yazmacı 0 girişlerin, tutma yazmacı 0 çıkışların bit sözcüğüdür. " +
                "Dört 0–10 V analog giriş (AI1–AI4 → giriş yazmacı 1–4) ve iki 0–10 V analog çıkış (tutma yazmacı 1–2 → AO1–AO2), milivolt olarak, 12 bit. " +
                "Veri yolu RTU iken aynı tablo 5 V TTL seri başlıktan (RX, TX) Modbus RTU ile sunulur: bir mikrodenetleyici kablo üzerinden usta olur.",
                new ParameterDefinition("unit", "Birim kimliği", "1", ""),
                new ParameterDefinition("bus", "Veri yolu", "tcp", "", new[] { "tcp", "rtu" }),
                new ParameterDefinition("port", "TCP portu", "5020", ""),
                new ParameterDefinition("bind", "Dinlenen adres", "127.0.0.1", "", new[] { "127.0.0.1", "0.0.0.0" }),
                new ParameterDefinition("baud", "RTU baud hızı", "9600", "", new[] { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" }),
                new ParameterDefinition("format", "RTU çerçevesi", "8E1", "", new[] { "8E1", "8O1", "8N2", "8N1" }),
                new ParameterDefinition("io_v", "G/Ç gerilimi", "24", "V", new[] { "5", "24" }));
            modbus.Build = c =>
            {
                BuildIo(c, ModbusInputs, ModbusOutputs);
                // [rtu] the TTL serial header on its own 5 V logic supply
                int gnd = c.Pin("GND"), vlog = c.Internal("VLOG");
                c.Add(new VoltageSource(c.Sub("VLOG"), vlog, gnd, new DcWaveform(5)) { SeriesResistance = 0.05 });
                c.Add(new DigitalPin(c.Sub("RX"), c.Pin("RX"), vlog, gnd) { Mode = PinMode.Input });
                c.Add(new Resistor(c.Sub("RRX"), c.Pin("RX"), vlog, 10e3));   // the line idles high when nothing drives it
                c.Add(new DigitalPin(c.Sub("TX"), c.Pin("TX"), vlog, gnd) { Mode = PinMode.OutputHigh, DriveResistance = 50 });

                // [modbus] analog: 100 kΩ inputs measured against GND; outputs from a 24 V analog supply, 10 mA-class drivers
                int vana = c.Internal("VANA");
                c.Add(new VoltageSource(c.Sub("VANA"), vana, gnd, new DcWaveform(24)) { SeriesResistance = 0.1 });
                foreach (var ai in ModbusAnalogInputs)
                {
                    c.Add(new DigitalPin(c.Sub(ai), c.Pin(ai), vana, gnd) { Mode = PinMode.Input, AbsoluteMaxCurrent = 1.0 });
                    c.Add(new Resistor(c.Sub("R" + ai), c.Pin(ai), gnd, 100e3));
                }

                foreach (var ao in ModbusAnalogOutputs)
                    c.Add(new ControlledSource(c.Sub(ao), c.Pin(ao), vana, gnd, false) { Level = 0, OutputResistance = 10, SupplyCurrent = 1e-3 });
            };
            AddPins(modbus, ModbusInputs, ModbusOutputs);
            modbus.Pins.Add(new PinDefinition("RX", "19", -2, 8));
            modbus.Pins.Add(new PinDefinition("TX", "20", 2, 8));
            for (int i = 0; i < ModbusAnalogInputs.Length; i++) modbus.Pins.Add(new PinDefinition(ModbusAnalogInputs[i], (21 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), -6, 12 + i * 2));
            for (int i = 0; i < ModbusAnalogOutputs.Length; i++) modbus.Pins.Add(new PinDefinition(ModbusAnalogOutputs[i], (25 + i).ToString(System.Globalization.CultureInfo.InvariantCulture), 6, 12 + i * 2));
            modbus.HalfHeight = 19;
            Preset(lib, "modbus-io-8di8do", "Modbus TCP Uzak G/Ç · 8 DI / 8 DO", ModbusIoId, "DIN raya takılır; PLC ve SCADA için port 5020");
            Preset(lib, "modbus-rtu-io-8di8do", "Modbus RTU Uzak G/Ç · 8 DI / 8 DO", ModbusIoId, "5 V TTL seri (RX/TX), 9600 baud 8E1, adres 1", "bus=rtu");

            // ---------------------------------------------------------------- photoelectric sensor and limit switch
            var photo = Part(lib, PhotoId, "Fotoelektrik Sensör", "PE",
                "Önündeki cismi ışıkla algılar (merceği +X yönüne bakar). Difüz: cisimden yansıyan ışığı görür; menzil beyaz kart içindir, koyu cisimde kısalır " +
                "(yansıyan ışık uzaklığın karesiyle azalır: siyah kauçuk yaklaşık üçte bir menzilde görülür). Reflektörlü ve karşılıklı: ışın menzil uzaklığındaki reflektöre " +
                "ya da alıcıya gider, araya giren her cisim keser. OUT seçilen durumda yüksek olur ve tepki süresi kadar gecikir.",
                new ParameterDefinition("mode", "Algılama", PhotoDiffuse, "", new[] { PhotoDiffuse, PhotoRetro, PhotoThrough }),
                new ParameterDefinition("range", "Menzil", "0.3", "m"),
                new ParameterDefinition("logic", "Çıkış açık", PhotoOnPresent, "", new[] { PhotoOnPresent, PhotoOnAbsent }),
                new ParameterDefinition("response", "Tepki süresi", "1", "ms"),
                new ParameterDefinition("io_v", "G/Ç gerilimi", "24", "V", new[] { "5", "24" }));
            photo.Build = c => BuildIo(c, Array.Empty<string>(), new[] { "OUT" });
            AddPins(photo, Array.Empty<string>(), new[] { "OUT" });
            Preset(lib, "photo-diffuse-300", "Fotoelektrik Sensör · difüz 300 mm", PhotoId, "Cisimden yansımalı, beyaz kartta 300 mm");
            Preset(lib, "photo-retro-3000", "Fotoelektrik Sensör · reflektörlü 3 m", PhotoId, "Işın reflektöre gider, araya giren cisim keser", "mode=" + PhotoRetro + ";range=3");
            var limit = Part(lib, LimitId, "Sınır Anahtarı (makaralı kol)", "LS",
                "Makaralı kollu mekanik anahtar (makara +X yönünde): bir cisim makarayı ön stroktan fazla bastırınca C–NO kapanır, C–NC açılır; yarım milimetre geri " +
                "gelince bırakır. Kontaklar devrede gerçek anahtardır: PLC girişine, röleye ya da doğrudan motor devresine bağlanır.",
                new ParameterDefinition("pretravel", "Ön strok", "2", "mm"),
                new ParameterDefinition("reach", "Makara erişimi", "25", "mm"));
            limit.Build = c =>
            {
                int common = c.Pin("C");
                c.Add(new Switch(c.Sub("NO"), common, c.Pin("NO")));
                c.Add(new Switch(c.Sub("NC"), common, c.Pin("NC"), closed: true));
            };
            limit.Pins.Add(new PinDefinition("C", "1", -6, 0));
            limit.Pins.Add(new PinDefinition("NO", "2", 6, -2));
            limit.Pins.Add(new PinDefinition("NC", "3", 6, 2));
            limit.HalfHeight = 4;
            Preset(lib, "limit-switch-roller", "Sınır Anahtarı · makaralı kol", LimitId, "1 NO + 1 NC, 2 mm ön strok");

            // ---------------------------------------------------------------- [pneu] pneumatic cylinder with its valve
            var cylinder = Part(lib, CylinderId, "Pnömatik Silindir (5/2 valfli)", "CYL",
                "Çift etkili silindir ve üstündeki tek bobinli 5/2 valf (mil +X yönünde çıkar): SOL bobini enerjilenince piston ileri, bırakılınca geri gider. " +
                "İki hazne ayrı hava kütleleri olarak çözülür: valften ISO 6358 akışı, dolarken ısınan, genleşirken soğuyan hava; kuvvet basınç × alan, contalar sürtünür. " +
                "EXT ve RET uç konumlardaki manyetik sensörlerin çıkışlarıdır. Hız ayarı egzozu kısar (çıkış kısmalı hız kontrolü). Bobin devrede gerçek bir yüktür.",
                new ParameterDefinition("bore", "Piston çapı", "20", "mm", new[] { "12", "16", "20", "25", "32", "40", "50", "63" }),
                new ParameterDefinition("stroke", "Strok", "100", "mm"),
                new ParameterDefinition("rod", "Mil çapı", "8", "mm"),
                new ParameterDefinition("supply", "Besleme basıncı", "6", "bar"),
                new ParameterDefinition("flow", "Valf ve hortum akışı (C)", "0.4", "L/(s·bar)"),
                new ParameterDefinition("speed", "Hız ayarı (egzoz kısma)", "100", "%"),
                new ParameterDefinition("coil_v", "Bobin gerilimi", "24", "V", new[] { "12", "24" }),
                new ParameterDefinition("coil_w", "Bobin gücü", "1", "W"),
                new ParameterDefinition("io_v", "Sensör gerilimi", "24", "V", new[] { "5", "24" }));
            cylinder.Build = c =>
            {
                BuildIo(c, Array.Empty<string>(), new[] { "EXT", "RET" });
                double volts = c.Num("coil_v", 24), watts = Math.Max(0.1, c.Num("coil_w", 1));
                c.Add(new Resistor(c.Sub("COIL"), c.Pin("SOL"), c.Pin("GND"), volts * volts / watts));
            };
            AddPins(cylinder, new[] { "SOL" }, new[] { "EXT", "RET" });
            // ---------------------------------------------------------------- [plc] a PLC running Structured Text
            var plc = Part(lib, PlcId, "PLC (Yapısal Metin, IEC 61131-3)", "PLC",
                "8 sayısal giriş (I0–I7 → %IX0.0–%IX0.7), 8 sayısal çıkış (Q0–Q7 ← %QX0.0–%QX0.7). Programı Yapısal Metin'dir (ST): BOOL, INT, REAL, TIME; " +
                "TON, TOF, TP, R_TRIG, F_TRIG, CTU, CTD, CTUD, SR, RS; IF, CASE (istasyon adımları), FOR, WHILE, REPEAT. Her çevrim süresinde girişleri okur, " +
                "programı bir kez çalıştırır, çıkışları yazar. Derlenmeyen program ya da çalışırken hata (sıfıra bölme, bitmeyen döngü) PLC'yi STOP'a alır, çıkışlar düşer.",
                new ParameterDefinition("language", "Dil", PlcStructuredText, "", new[] { PlcStructuredText, PlcLadder }),
                new ParameterDefinition("cycle", "Çevrim süresi", "10", "ms"),
                new ParameterDefinition("step_var", "Adım değişkeni", "Adim", ""),
                new ParameterDefinition("io_v", "G/Ç gerilimi", "24", "V", new[] { "5", "24" }),
                new ParameterDefinition("modbus", "Modbus TCP sunucusu", "kapalı", "", new[] { "kapalı", "açık" }),
                new ParameterDefinition("modbus_port", "Modbus TCP portu", "5021", ""),
                new ParameterDefinition("bind", "Dinlenen adres", "127.0.0.1", "", new[] { "127.0.0.1", "0.0.0.0" }));
            plc.Build = c => BuildIo(c, PlcInputs, PlcOutputs);
            AddPins(plc, PlcInputs, PlcOutputs);
            Preset(lib, "plc-st-8di8do", "PLC · 8 DI / 8 DO (Yapısal Metin)", PlcId, "Örnek programla gelir: lamba, flaşör, itici istasyonu");

            Preset(lib, "cylinder-20x100", "Pnömatik Silindir · Ø20 × 100 mm", CylinderId, "Tek bobinli 5/2 valf, 6 bar, uç sensörleri");
            Preset(lib, "cylinder-32x200", "Pnömatik Silindir · Ø32 × 200 mm", CylinderId, "Tek bobinli 5/2 valf, 6 bar, uç sensörleri", "bore=32;stroke=200;rod=12;flow=0.9");

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
            ModbusIoId => 0.2,
            PhotoId => 0.06,
            LimitId => 0.12,
            PlcId => 0.35,
            CylinderId => 0.25 + 2.5 * Num(p, "stroke", 100) / 1000 * Num(p, "bore", 20) / 20,
            _ => null
        };
    }
}
