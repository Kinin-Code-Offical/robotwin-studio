using System;
using System.Collections.Generic;
using System.Globalization;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox.Sensing;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [sense] World sensors with physical models: IR reflectance line sensors (TCRT5000 module,
    /// 5-channel module, QTR-8A / QTR-8RC arrays), cameras, 2D/3D LiDARs, GNSS receivers, an
    /// HMC5883L compass and gear motors with quadrature encoders. Their electrical side lives in
    /// the circuit (phototransistors with pull-ups, comparators, encoder outputs, supply loads);
    /// what they sense is sampled from the world by <see cref="Sandbox.Sensing.WorldSensing"/>.
    /// </summary>
    public static class SensingParts
    {
        public const string LineCategory = "Sensör · Çizgi";
        public const string VisionCategory = "Sensör · Kamera & LiDAR";
        public const string NavCategory = "Sensör · Konum & Hareket";

        public const string Tcrt5000Id = "line-tcrt5000";
        public const string Line5Id = "line-5ch";
        public const string Qtr8aId = "qtr-8a";
        public const string Qtr8rcId = "qtr-8rc";
        public const string CameraId = "camera";
        public const string LidarId = "lidar";
        public const string GpsId = "gps";
        public const string CompassId = "hmc5883l";
        public const string EncoderMotorId = "motor-encoder";

        public static bool IsLineSensor(string? id) => id == Tcrt5000Id || id == Line5Id || id == Qtr8aId || id == Qtr8rcId;

        public static void Register(PartLibrary lib)
        {
            if (lib == null) throw new ArgumentNullException(nameof(lib));

            // ---------------------------------------------------------------- line sensors
            var tcrt = Part(lib, Tcrt5000Id, "TCRT5000 Çizgi Modülü (AO/DO)", LineCategory, "U", "HDR-1x4",
                "TCRT5000 yansıma sensörü + LM393 karşılaştırıcı. AO: fototransistör 10 kΩ pull-up ile (siyah = yüksek, beyaz = düşük). DO: trimpot eşiğinin altına inince LOW (beyaz), sinyal LED'i yanar. En iyi mesafe 2.5 mm, 15 mm'de sinyal çok zayıf.",
                c => BuildTcrtModule(c),
                P("model", "Model", "TCRT5000 modül"), P("threshold", "Eşik (trimpot, VCC oranı)", "0.5"), P("rled", "IR LED direnci", "150", "Ω"),
                P("rload", "Fototransistör yükü", "10k", "Ω"), P("seed", "Parça toleransı tohumu", "1"));
            tcrt.Pin("VCC", "1", -4, -3).Pin("GND", "2", -4, -1).Pin("DO", "3", -4, 1).Pin("AO", "4", -4, 3).Size(3, 4);
            Preset(lib, "tcrt5000-module", "TCRT5000 Çizgi Sensörü (gerçek optik, AO/DO)", Tcrt5000Id, LineCategory,
                "Yükseklik, eğim, bant cinsi (mat/solmuş/parlak) ve ortam IR ışığı okumayı değiştirir");

            var five = Part(lib, Line5Id, "5 Kanallı TCRT5000 Çizgi Modülü", LineCategory, "U", "HDR-1x7",
                "Beş TCRT5000, 15 mm aralıklı; her kanalda LM339 karşılaştırıcı (açık kollektör, 10 kΩ pull-up). Tek trimpot eşiği; kanallar arasında ±%3 tolerans. OUT LOW = beyaz zemin.",
                c => BuildLineArray(c, 5, digital: true),
                P("model", "Model", "5 kanal TCRT5000"), P("threshold", "Eşik (trimpot, VCC oranı)", "0.5"), P("rled", "IR LED direnci", "150", "Ω"),
                P("rload", "Fototransistör yükü", "10k", "Ω"), P("seed", "Parça toleransı tohumu", "5"));
            five.Pin("VCC", "1", -5, -6).Pin("GND", "2", -5, -4);
            for (int i = 1; i <= 5; i++) five.Pin("OUT" + i, (i + 2).ToString(CultureInfo.InvariantCulture), -5, -4 + 2 * i);
            five.Size(4, 7);
            Preset(lib, "line-5ch-module", "5 Kanallı Çizgi Sensörü (dijital)", Line5Id, LineCategory, "OUT1 (sol) … OUT5 (sağ), 15 mm aralık, dijital çıkış");

            var qtrA = Part(lib, Qtr8aId, "QTR-8A Analog Yansıma Dizisi", LineCategory, "U", "HDR-1x11",
                "Sekiz QRE1113, 9.525 mm aralıklı. Her çıkış 47 kΩ pull-up'lı fototransistör: siyahta VCC'ye yakın, beyazda düşük. En iyi 3 mm, en çok 6 mm. LEDON LOW = vericiler kapalı.",
                c => BuildLineArray(c, 8, digital: false),
                P("model", "Model", "QTR-8A"), P("rload", "Fototransistör yükü", "47k", "Ω"), P("seed", "Parça toleransı tohumu", "8"));
            qtrA.Pin("VCC", "1", -5, -10).Pin("GND", "2", -5, -8).Pin("LEDON", "3", -5, -6);
            for (int i = 1; i <= 8; i++) qtrA.Pin("OUT" + i, (i + 3).ToString(CultureInfo.InvariantCulture), -5, -6 + 2 * i);
            qtrA.Size(4, 11);
            Preset(lib, "qtr-8a-array", "QTR-8A Analog Çizgi Dizisi (8 kanal)", Qtr8aId, LineCategory, "8 analog çıkış, 9.5 mm aralık, QRE1113");

            var qtrRc = Part(lib, Qtr8rcId, "QTR-8RC Dijital (RC) Yansıma Dizisi", LineCategory, "U", "HDR-1x11",
                "Sekiz QRE1113; her kanalda 2.2 nF kondansatör ve 220 Ω. Okuma: pini HIGH sürüp kondansatörü doldurun, girişe alın ve LOW'a düşme süresini ölçün (beyaz ≈ onlarca µs, siyah ms mertebesi). En çok 9.5 mm.",
                c => BuildLineArray(c, 8, digital: false, rc: true),
                P("model", "Model", "QTR-8RC"), P("seed", "Parça toleransı tohumu", "9"));
            qtrRc.Pin("VCC", "1", -5, -10).Pin("GND", "2", -5, -8).Pin("LEDON", "3", -5, -6);
            for (int i = 1; i <= 8; i++) qtrRc.Pin("OUT" + i, (i + 3).ToString(CultureInfo.InvariantCulture), -5, -6 + 2 * i);
            qtrRc.Size(4, 11);
            Preset(lib, "qtr-8rc-array", "QTR-8RC Çizgi Dizisi (RC zamanlama)", Qtr8rcId, LineCategory, "8 kanal, kondansatör deşarj süresiyle okunur");

            // ---------------------------------------------------------------- cameras
            var cam = Part(lib, CameraId, "Kamera Modülü", VisionCategory, "CAM", "HDR-1x4",
                "Dünyayı kendi konumundan, gerçek piksel çözünürlüğü ve kare hızıyla görür: otomatik pozlama (ışık seviyesine göre enstantane + kazanç), foton gürültüsü, hareket bulanıklığı, yuvarlanan deklanşör. ESP32 çizimleri esp_camera.h ile kare alabilir.",
                c =>
                {
                    var spec = CameraModels.Find(c.Text("model"));
                    c.Add(new Resistor(c.RefDes, c.Pin("VCC"), c.Pin("GND"), spec.SupplyVoltage / Math.Max(1e-3, spec.SupplyCurrent)));
                    c.Add(new Resistor(c.Sub("RSDA"), c.Pin("SDA"), c.Pin("VCC"), 10e3));
                    c.Add(new Resistor(c.Sub("RSCL"), c.Pin("SCL"), c.Pin("VCC"), 10e3));
                },
                P("model", "Sensör", "OV2640", "", "OV7670", "OV2640", "USB720", "IMX219"),
                P("framesize", "Kare boyutu", "VGA", "", "QQVGA", "QCIF", "HQVGA", "240X240", "QVGA", "CIF", "HVGA", "VGA", "SVGA", "XGA", "HD", "SXGA", "UXGA", "FHD"),
                P("format", "Piksel biçimi", "RGB565", "", "RGB565", "GRAYSCALE", "JPEG", "YUV422", "RGB888"),
                P("fov", "Yatay görüş açısı (0 = lens varsayılanı)", "0", "°"),
                P("fps", "Kare hızı sınırı (0 = sensörün en yükseği)", "0", "fps"),
                P("quality", "JPEG kalitesi (0–63, düşük = iyi)", "12"),
                P("host", "Bağlı ESP32 (nesne kimliği, boş = otomatik)", ""),
                P("noise", "Sensör gerçekçiliği", "true", "", "true", "false"));
            cam.Pin("VCC", "1", -4, -3).Pin("GND", "2", -4, -1).Pin("SDA", "3", -4, 1).Pin("SCL", "4", -4, 3).Size(3, 4);
            Preset(lib, "cam-ov7670", "OV7670 Kamera Modülü (640×480, 30 fps)", CameraId, VisionCategory,
                "0.3 MP, 1/6\" sensör, 3.6 µm piksel, ≈35° yatay görüş", "model=OV7670;framesize=QVGA;format=RGB565");
            Preset(lib, "cam-ov2640", "OV2640 Kamera Modülü (2 MP, UXGA 15 fps)", CameraId, VisionCategory,
                "1600×1200 @15 fps, SVGA @30, CIF @60; ≈54° yatay görüş (ESP32-CAM lensi)", "model=OV2640;framesize=VGA;format=JPEG");
            Preset(lib, "cam-usb720", "USB Web Kamerası 720p (30 fps)", CameraId, VisionCategory,
                "1280×720 @30 fps, ≈48° yatay görüş, 5 V USB", "model=USB720;framesize=HD;format=RGB888");
            Preset(lib, "cam-pi-v2", "Pi Kamera v2 (IMX219, 8 MP)", CameraId, VisionCategory,
                "3280×2464 @15, 1080p30, 720p60, 640×480 @90 fps; 62.2° × 48.8°", "model=IMX219;framesize=VGA;format=RGB888");

            // ---------------------------------------------------------------- LiDAR
            var lidar = Part(lib, LidarId, "LiDAR Tarayıcı", VisionCategory, "U", "HDR-1x5",
                "Dönen lazer mesafe ölçer: her ölçüm dünyaya atılan bir ışın; mesafe gürültüsü, koyu yüzeylerde kısalan menzil ve tarama sırasında hareket eden robotun bozduğu tarama dahil. Tarama 3D görünümde ve LiDAR panelinde canlı görünür.",
                c =>
                {
                    var spec = LidarSpec.For(c.Text("model"));
                    int vcc = c.Pin("VCC"), gnd = c.Pin("GND");
                    c.Add(new Resistor(c.RefDes, vcc, gnd, spec.SupplyVoltage / Math.Max(1e-3, spec.SupplyCurrent)));
                    c.Add(new Resistor(c.Sub("RMOT"), c.Pin("MOTOCTL"), vcc, 10e3));
                    c.Add(new ControlledSource(c.Sub("TX"), c.Pin("TX"), vcc, gnd, false) { Level = 3.3, SupplyCurrent = 0 });
                },
                P("model", "Model", "A1", "", "A1", "LD06", "VLP16"), P("scanrate", "Tarama hızı", "0", "Hz"),
                P("azimuth", "Yatay adım (3D, 0 = varsayılan)", "1", "°"));
            lidar.Pin("VCC", "1", -4, -4).Pin("GND", "2", -4, -2).Pin("TX", "3", -4, 0).Pin("RX", "4", -4, 2).Pin("MOTOCTL", "5", -4, 4).Size(3, 5);
            Preset(lib, "lidar-a1", "360° 2D LiDAR (RPLIDAR A1 sınıfı)", LidarId, VisionCategory,
                "8000 ölçüm/s, 5.5 Hz (1–10), 0.15–12 m, ≈%1 mesafe hatası", "model=A1;scanrate=5.5");
            Preset(lib, "lidar-ld06", "360° ToF LiDAR (LD06 sınıfı)", LidarId, VisionCategory,
                "4500 ölçüm/s, 10 Hz, 0.02–12 m, ±45 mm", "model=LD06;scanrate=10");
            Preset(lib, "lidar-16ch", "16 Kanallı 3D LiDAR (VLP-16 sınıfı)", LidarId, VisionCategory,
                "±15° düşey 16 ışın, 10 Hz, 100 m, ±3 cm (yatay adım kısaltılmış: 1°)", "model=VLP16;scanrate=10;azimuth=1");

            // ---------------------------------------------------------------- GNSS
            var gps = Part(lib, GpsId, "GPS Alıcı Modülü", NavCategory, "U", "HDR-1x4",
                "Konum dünyanın Ortam sekmesindeki başlangıç enlem/boylamından hesaplanır; CEP'e göre yavaş gezinen hata, ilk konum süresi (soğuk/ılık/sıcak başlatma), gökyüzü görünürlüğü. NMEA cümleleri (RMC, VTG, GGA, GSA, GLL) TX pinine bağlı karta (UART RX) gönderilir.",
                c =>
                {
                    var spec = GpsSpec.For(c.Text("model"));
                    int vcc = c.Pin("VCC"), gnd = c.Pin("GND");
                    c.Add(new Resistor(c.RefDes, vcc, gnd, 5.0 / Math.Max(1e-3, spec.SupplyCurrent)));
                    c.Add(new ControlledSource(c.Sub("TX"), c.Pin("TX"), vcc, gnd, false) { Level = 3.3, SupplyCurrent = 0 });
                    c.Add(new Resistor(c.Sub("RRX"), c.Pin("RX"), vcc, 10e3));
                },
                P("model", "Model", "NEO-6M", "", "NEO-6M", "NEO-M8N"), P("rate", "Güncelleme hızı", "1", "Hz"),
                P("start", "Başlatma", "sıcak", "", "soğuk", "ılık", "sıcak"), P("baud", "Baud", "9600", "", "4800", "9600", "38400", "115200"));
            gps.Pin("VCC", "1", -4, -3).Pin("GND", "2", -4, -1).Pin("TX", "3", -4, 1).Pin("RX", "4", -4, 3).Size(3, 4);
            Preset(lib, "gps-neo6m", "NEO-6M GPS Modülü (GY-NEO6MV2)", GpsId, NavCategory, "2.5 m CEP, 1–5 Hz, 9600 baud NMEA, seramik anten", "model=NEO-6M");
            Preset(lib, "gps-neo-m8n", "NEO-M8N GNSS Modülü", GpsId, NavCategory, "≈2 m CEP, 10 Hz'e kadar, 9600 baud NMEA", "model=NEO-M8N;rate=5");

            // ---------------------------------------------------------------- compass
            var mag = Part(lib, CompassId, "HMC5883L Pusula (GY-271)", NavCategory, "U", "HDR-1x5",
                "3 eksen manyetometre, I²C 0x1E. Dünyanın manyetik alanı (Ortam: şiddet, eğim, sapma) modülün yönüne göre ölçülür; motor/mıknatıs kaynaklı sert-demir sapması parametresi.",
                c =>
                {
                    int vcc = c.Pin("VCC"), gnd = c.Pin("GND");
                    c.Add(new Resistor(c.RefDes, vcc, gnd, 5.0 / 0.1e-3 / 10));
                    c.Add(new Resistor(c.Sub("RSDA"), c.Pin("SDA"), vcc, 4.7e3));
                    c.Add(new Resistor(c.Sub("RSCL"), c.Pin("SCL"), vcc, 4.7e3));
                },
                P("model", "Model", "GY-271 HMC5883L"), P("address", "I²C adresi", "0x1E"),
                P("hardiron", "Sert-demir sapması X,Y,Z", "0,0,0", "mG"));
            mag.Pin("VCC", "1", -4, -4).Pin("GND", "2", -4, -2).Pin("SCL", "3", -4, 0).Pin("SDA", "4", -4, 2).Pin("DRDY", "5", -4, 4).Size(3, 5);
            Preset(lib, "gy-271", "GY-271 HMC5883L Pusula Modülü", CompassId, NavCategory, "3 eksen manyetometre, ±1.3 Ga varsayılan, 15 Hz");

            // ---------------------------------------------------------------- encoder motors
            var em = Part(lib, EncoderMotorId, "Enkoderli Redüktörlü Motor", StandardParts.Electromechanical, "M", "HDR-1x6",
                "DC motor + motor milinde manyetik (Hall) kuadratür enkoder. C1/C2 çıkışları 90° faz farklı; çıkış milinde tur başına darbe = enkoder PPR × dişli oranı. Yazılım kenarları sayarak hız ve konum ölçer.",
                c =>
                {
                    var m = c.Add(new DcMotor(c.RefDes, c.Pin("M1"), c.Pin("M2")));
                    m.SetFromDatasheet(c.Num("voltage", 6), c.Num("rpm", 300), c.Num("i0", 0.04), c.Num("istall", 0.36), Math.Max(1, c.Num("gear", 100)), c.Num("inductance", 0.5e-3));
                    var motor = m;
                    c.Add(new QuadratureEncoder(c.Sub("ENC"), c.Pin("C1"), c.Pin("C2"), c.Pin("VCC"), c.Pin("GND"), () => motor.OutputAngle * motor.GearRatio)
                    {
                        PulsesPerRevolution = Math.Max(1, c.Num("ppr", 7))
                    });
                },
                P("model", "Model", "N20 1:100 enkoderli"), P("voltage", "Nominal gerilim", "6", "V"), P("rpm", "Yüksüz devir (çıkış)", "300", "rpm"),
                P("i0", "Yüksüz akım", "0.04", "A"), P("istall", "Kilitlenme akımı", "0.36", "A"), P("gear", "Dişli oranı", "100"),
                P("inductance", "Endüktans", "0.5m", "H"), P("ppr", "Enkoder darbe/tur (motor mili)", "7"));
            em.Pin("M1", "1", -4, -5).Pin("GND", "2", -4, -3).Pin("C1", "3", -4, -1).Pin("C2", "4", -4, 1).Pin("VCC", "5", -4, 3).Pin("M2", "6", -4, 5).Size(3, 6);
            Preset(lib, "motor-n20-encoder", "N20 Enkoderli Motor 1:100 (7 PPR → 700/tur)", EncoderMotorId, StandardParts.Electromechanical,
                "6 V 300 rpm, Hall enkoder, çıkış milinde 700 darbe/tur (×4 = 2800 sayım)");
            Preset(lib, "motor-jga25-encoder", "JGA25-370 Enkoderli Motor 1:78 (11 PPR → 858/tur)", EncoderMotorId, StandardParts.Electromechanical,
                "12 V 100 rpm, Hall enkoder, çıkış milinde 858 darbe/tur",
                "model=JGA25-370 enkoderli;voltage=12;rpm=100;i0=0.08;istall=1.6;gear=78;inductance=2m;ppr=11");
        }

        // ================================================================ builds

        /// <summary>IR emitter: series resistor + IR LED (V_F ≈ 1.2 V at 20 mA).</summary>
        private static void Emitter(PartBuildContext c, string name, int vcc, int gnd, double rled)
        {
            int a = c.Internal(name + "A");
            c.Add(new Resistor(c.Sub("R" + name), vcc, a, rled));
            c.Add(new Diode(c.Sub(name), a, gnd) { SaturationCurrent = 7e-16, Ideality = 1.5, SeriesResistance = 1.5 });
        }

        private static void BuildTcrtModule(PartBuildContext c)
        {
            int vcc = c.Pin("VCC"), gnd = c.Pin("GND"), ao = c.Pin("AO"), dout = c.Pin("DO");
            // Signal LED (lights while DO is low) first: the world view lights it from this device.
            int sig = c.Internal("SIG");
            c.Add(new Resistor(c.Sub("RSIG"), vcc, sig, 1e3));
            c.Add(new Led(c.Sub("LEDSIG"), sig, dout, 2.0, 0.02));
            int pwr = c.Internal("PWR");
            c.Add(new Resistor(c.Sub("RPWR"), vcc, pwr, 1e3));
            c.Add(new Led(c.Sub("LEDPWR"), pwr, gnd, 2.0, 0.02));
            Emitter(c, "IR", vcc, gnd, Math.Max(10, c.Num("rled", 150)));
            c.Add(new Resistor(c.Sub("RL"), vcc, ao, Math.Max(100, c.Num("rload", 10e3))));
            c.Add(new Phototransistor(c.Sub("Q1"), ao, gnd));
            c.Add(new Resistor(c.Sub("RDO"), vcc, dout, 10e3));
            var rng = new Random((int)c.Num("seed", 1));
            c.Add(new OpenCollectorComparator(c.Sub("CMP"), ao, vcc, gnd, dout)
            {
                Threshold = Math.Max(0.02, Math.Min(0.98, c.Num("threshold", 0.5))), Offset = (rng.NextDouble() - 0.5) * 0.006
            });
        }

        private static void BuildLineArray(PartBuildContext c, int channels, bool digital, bool rc = false)
        {
            int vcc = c.Pin("VCC"), gnd = c.Pin("GND");
            var rng = new Random((int)c.Num("seed", channels));
            if (digital)
            {
                // Signal LEDs per channel are left out of the circuit to keep it small; the emitters share one resistor each.
                for (int i = 1; i <= channels; i++) Emitter(c, "IR" + i, vcc, gnd, Math.Max(10, c.Num("rled", 150)));
            }
            else
            {
                // QTR: emitters in pairs from VCC, switched by LEDON (pulled up on the board).
                int ledon = c.Pin("LEDON");
                c.Add(new Resistor(c.Sub("RLEDON"), vcc, ledon, 10e3));
                c.Add(new Resistor(c.Sub("REMIT"), ledon, gnd, 1e6));             // LEDON input (MOSFET gate)
                c.Add(new Resistor(c.RefDes, vcc, gnd, 5.0 / 0.1));              // ≈ 100 mA emitters at 5 V
            }

            for (int i = 1; i <= channels; i++)
            {
                int outp = c.Pin("OUT" + i);
                string n = i.ToString(CultureInfo.InvariantCulture);
                if (rc)
                {
                    // QTR-8RC: 220 Ω to the pin, 2.2 nF and the phototransistor to ground.
                    int node = c.Internal("C" + n);
                    c.Add(new Resistor(c.Sub("R" + n), outp, node, 220));
                    c.Add(new Capacitor(c.Sub("C" + n), node, gnd, 2.2e-9));
                    c.Add(new Phototransistor(c.Sub("Q" + n), node, gnd));
                }
                else if (digital)
                {
                    int ao = c.Internal("A" + n);
                    c.Add(new Resistor(c.Sub("RL" + n), vcc, ao, Math.Max(100, c.Num("rload", 10e3))));
                    c.Add(new Phototransistor(c.Sub("Q" + n), ao, gnd));
                    c.Add(new Resistor(c.Sub("RO" + n), vcc, outp, 10e3));
                    c.Add(new OpenCollectorComparator(c.Sub("CMP" + n), ao, vcc, gnd, outp)
                    {
                        Threshold = Math.Max(0.02, Math.Min(0.98, c.Num("threshold", 0.5) * (1 + (rng.NextDouble() - 0.5) * 0.06))),
                        Offset = (rng.NextDouble() - 0.5) * 0.006
                    });
                }
                else
                {
                    c.Add(new Resistor(c.Sub("RL" + n), vcc, outp, Math.Max(100, c.Num("rload", 47e3))));
                    c.Add(new Phototransistor(c.Sub("Q" + n), outp, gnd));
                }
            }
        }

        // ================================================================ geometry (object frame, metres)

        /// <summary>One reflective channel: centre of its optical face (object frame), looking along −Z, emitter→detector along <see cref="Baseline"/>.</summary>
        public readonly struct LineChannel
        {
            public LineChannel(Vec3 centre, Vec3 baseline)
            {
                Centre = centre;
                Baseline = baseline;
            }

            public Vec3 Centre { get; }
            public Vec3 Baseline { get; }
        }

        /// <summary>
        /// Sensing faces of a line sensor part in its object frame (metres). Modules hang under a
        /// chassis with the optics facing down: z = 0 is the optical face, so the object's height
        /// above the floor is the sensing distance. +X is forward; channel 1 is on the left (+Y).
        /// </summary>
        public static IReadOnlyList<LineChannel> LineChannels(string? partId)
        {
            var list = new List<LineChannel>();
            switch (partId)
            {
                case Tcrt5000Id:
                    list.Add(new LineChannel(new Vec3(0.0115, 0, 0), Vec3.UnitY));
                    break;
                case Line5Id:
                    for (int i = 0; i < 5; i++) list.Add(new LineChannel(new Vec3(0.006, (2 - i) * 0.015, 0), Vec3.UnitX));
                    break;
                case Qtr8aId:
                case Qtr8rcId:
                    for (int i = 0; i < 8; i++) list.Add(new LineChannel(new Vec3(0.0015, (3.5 - i) * 0.009525, 0), Vec3.UnitX));
                    break;
            }

            return list;
        }

        public static ReflectiveSensorSpec LineSpec(string? partId) => partId == Qtr8aId || partId == Qtr8rcId ? ReflectiveSensorSpec.Qre1113 : ReflectiveSensorSpec.Tcrt5000;

        /// <summary>Camera lens (object frame): centre of the lens front, looking along +X with +Z up.</summary>
        public static Vec3 CameraLens(string? model) => CameraModels.Find(model).Id switch
        {
            "OV7670" => new Vec3(0.0185, 0, 0.0175),
            "USB720" => new Vec3(0.0145, 0, 0.040),
            "IMX219" => new Vec3(0.0095, 0, 0.0125),
            _ => new Vec3(0.0085, 0, 0.0145)
        };

        /// <summary>Optical centre of a LiDAR (object frame, origin = middle of the base).</summary>
        public static Vec3 LidarCentre(string? model) => LidarSpec.For(model).Id switch
        {
            "VLP16" => new Vec3(0, 0, 0.0377),
            "LD06" => new Vec3(0, 0, 0.026),
            _ => new Vec3(0, 0, 0.045)
        };

        // ================================================================ helpers

        private static PartDefinition Part(PartLibrary lib, string id, string name, string category, string prefix, string footprint, string description,
            Action<PartBuildContext> build, params ParameterDefinition[] parameters)
        {
            var part = new PartDefinition
            {
                Id = id, Name = name, Category = category, RefPrefix = prefix, Symbol = SymbolKind.Module, Footprint = footprint,
                ValueKey = "model", Description = description, Build = build
            };
            part.Parameters.AddRange(parameters);
            lib.Add(part);
            return part;
        }

        private static PartDefinition Pin(this PartDefinition part, string name, string pad, double x, double y)
        {
            part.Pins.Add(new PinDefinition(name, pad, x, y));
            return part;
        }

        private static PartDefinition Size(this PartDefinition part, double hw, double hh)
        {
            part.HalfWidth = hw;
            part.HalfHeight = hh;
            return part;
        }

        private static ParameterDefinition P(string key, string label, string value, string unit = "", params string[] choices) =>
            new ParameterDefinition(key, label, value, unit, choices.Length > 0 ? choices : null);

        private static void Preset(PartLibrary lib, string id, string name, string partId, string category, string description, string parameters = "")
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string pair in parameters.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq > 0) map[pair.Substring(0, eq).Trim()] = pair.Substring(eq + 1).Trim();
            }

            lib.AddPreset(new PartPreset(id, name, partId, category, description, map));
        }
    }
}
