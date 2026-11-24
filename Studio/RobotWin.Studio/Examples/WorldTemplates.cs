using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Mechanics;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Examples
{
    public sealed class WorldTemplate
    {
        public WorldTemplate(string id, string title, string description, Func<WorldDocument> build)
        {
            Id = id;
            Title = title;
            Description = description;
            Build = () => Localized(build());
        }

        /// <summary>
        /// [lang] The example made in the language in use: its title, its objects' and boards' names, and its sketches'
        /// comments and messages (the names are the world's own data from then on, saved with it).
        /// </summary>
        private static WorldDocument Localized(WorldDocument w)
        {
            if (Shell.Lang.Current == "tr") return w;
            w.Title = Shell.Lang.T(w.Title);
            foreach (var o in w.Objects)
            {
                o.Name = Shell.Lang.T(o.Name);
                if (o.Firmware is { } fw) fw.Source = Shell.Lang.Code(fw.Source);
            }

            foreach (var d in w.Designs) d.Name = Shell.Lang.T(d.Name);
            return w;
        }

        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public Func<WorldDocument> Build { get; }
    }

    /// <summary>Ready-made worlds that show boards, parts, cables and joints working together.</summary>
    public static partial class WorldTemplates   // [objs] ready-part and light worlds in Examples/WorldTemplates.Ready.cs
    {
        private static PartLibrary Lib => PartLibrary.Default;

        public static IReadOnlyList<WorldTemplate> All { get; } = new List<WorldTemplate>
        {
            new WorldTemplate("empty", "Boş atölye", "4 × 3 m zemin, 25 °C. Kartlarınızı ve parçalarınızı ekleyin.", () => new WorldDocument { Title = "Atölye" }),
            new WorldTemplate("two-boards", "Kabloyla bağlı iki kart", "Kendi çizilmiş güç kartı (9 V pil) ile LED kartı ayrı PCB'ler; iki kabloyla birleşince LED yanar.", TwoBoards),
            new WorldTemplate("servo-arm", "Arduino ile iki eksenli servo kol", "Arduino Uno kütüphanesiz darbe üreterek iki SG90'ı sürer; ikinci servo birinci servonun koluna vidalı, kol zincirleme döner.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.ServoArm(Lib)),
            new WorldTemplate("displays", "Arduino, LCD ve OLED (I²C kablolu)", "Uno, 16×2 LCD, 128×64 OLED ve potansiyometre ayrı parçalar; dört telli I²C kablolarıyla bağlı. Gerçek derlenmiş yazılım iki ekrana da yazar.", Displays),
            new WorldTemplate("car", "İki tekerlekli araba (gerçek fizik)", "PLA şase, iki TT motor ve tekerlek, bilyeli sarhoş teker, üstte 4×AA pil. Çalıştırınca yerçekimi, sürtünme ve motor yükleriyle zeminde yol alır.", Car),
            new WorldTemplate("sonar", "Ultrasonik mesafe sensörü ve engeller", "HC-SR04 dünyadaki nesnelere gerçekten ölçüm yapar: ışını önündeki ilk nesneye kadar çizilir, mesafe renkle gösterilir. Kutuları sürükleyip yaklaştırın.", Sonar),
            new WorldTemplate("motor-wheel", "Pil, motor ve tekerlek", "4×AA pil kutusu kablolarla TT motoru sürer; tekerlek motor miline takılı, motor sehpaya vidalı.", MotorWheel),
            new WorldTemplate("factory-cell", "Fabrika hücresi (robot kol + konveyör)", "Konveyör kutuları getirir, 6 eksenli kol vakumla alıp palete 3×2×2 dizer; fotosel kolun DI1 girişine, ışık perdesi SAFE girişine kablolu. Öğretme panelinden programı değiştirin.", RobotTwin.CoreSim.Sandbox.FactoryCell.Build),   // [arm]
            new WorldTemplate("scara-cell", "SCARA al-bırak (paralel tutucu)", "SCARA robot yükselticinin üstünde; paralel tutucu dört alüminyum bloğu sürtünmeyle kavrayıp bir sıradan diğerine taşır ve geri getirir.", RobotTwin.CoreSim.Sandbox.FactoryCell.Scara),   // [arm]
            // [radio]
            // [objs]
            new WorldTemplate("prop-stand", "Pervane itki standı", "8520 coreless motorlar 55 ve 65 mm pervaneyle 1S LiPo'dan dönüyor: devir, itki (g), sürükleme torku ve motor akımı canlı okunur.", PropStand),
            new WorldTemplate("mini-drone", "Mini drone kalkışı", "Dört 8520 motor, çapraz CCW/CW pervaneler ve 1S LiPo, 40 g'lık X gövde. İtki ağırlığı geçince havalanır (kontrolcü yok, düz yükselir).", MiniDrone),
            new WorldTemplate("esp32-quad", "ESP32 dörtlü: fırçasız motorlar, ESC, IMU, kendi yazılımıyla uçuş", "5\" X gövde, dört 2204 2300KV fırçasız motor, 20 A ESC, 5045 pervaneler, 3S 1300 mAh LiPo. ESP32 üzerindeki gerçek uçuş yazılımı MPU-6050'yi I²C'den okur, açı ve hız PID'iyle ESC'lere darbe gönderir: ESC'ler kurulur, gövde kalkar ve düz tutulur. Pili öne kaydırın: ağırlık merkezi kayar, kontrolcü motorları ayrı ayrı ayarlayarak dengeler.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.Quad(Lib)),   // [esc]
            new WorldTemplate("shell-box", "Elektronik kutusu: kapaklı içi boş gövde ve sığma denetimi", "120 × 80 × 40 mm PLA kutu, 2 mm duvar, vidalı kapak: içinde Arduino Uno ve dik duran 9 V pil, dışarıda bir servo (kablolar arka delikten). Pil 48 mm: kenardan 10 mm taşar, kapak kapanmaz; sığma denetimi bunu kırmızıyla gösterir. Röntgen ve patlatılmış görünümle içine bakın.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.ShellBox(Lib)),   // [shell]
            new WorldTemplate("motor-glider", "Motorlu planör (İHA): ESP32 uçuş kartı, fırçasız motor, pil ile denge", "1 m planör gerçek bir elektrikli İHA gibi: burunda 2212 1000KV fırçasız motor ve 8×4.5\" pervane (3S'te ~5 N itki), 20 A ESC, ağırlık merkezini burun kurşunu yerine yalnız konumuyla %30'a getiren 3S 850 mAh pil, ESP32 + MPU-6050 uçuş kartı. Elde 2,5 s tutulur (ESC kurulur, IMU kalibre olur), atılır, motorla tırmanır, sonra süzülür.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.MotorGlider(Lib)),   // [uav]
            new WorldTemplate("glider", "Planör: balsa gövde, karbon kuyruk borusu, Arduino Nano + 2 servo", "1 m açıklıklı RC planör gerçek yapısıyla: balsa kabinde 2S LiPo, Arduino Nano, irtifa ve yön servoları ve burun kurşunu; karbon kuyruk borusu boyunca itme çubukları; 6° dihedral Clark Y kanat; 233 g, ağırlık merkezi veterin %33'ünde. 40 m'den 7 m/s ile elden atılır, ~10:1 süzülür; çizim 3 s düz uçup yön dümeniyle hafifçe döner.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.Glider(Lib)),   // [aero]
            new WorldTemplate("mecanum", "Mekanum tekerlekli araba (yana kayar)", "Dört TT motor ve A/B mekanum tekerlekler. Ön motorlar düz, arka motorlar ters bağlı: makaraların 45° itişi araba yana kaydırır.", Mecanum),
            new WorldTemplate("lights", "Işıklar: LED'ler ve lambalar (karanlık oda)", "Dirençli 5 mm LED'ler, RGB LED (kırmızı + mavi = mor), 3 W güç LED'i, fener ampulü ve 12 V LED şerit karanlık odayı aydınlatır.", Lights),
            new WorldTemplate("trafik-isigi", "Trafik ışığı ve yaya butonu (ESP32)", "ESP32 araç (kırmızı, sarı, yeşil) ve yaya (kırmızı, yeşil) LED'lerini 220 Ω dirençlerle sürer. Yaya butonu (iç pull-up, toprağa) geçiş ister: en az 4 s yeşilden sonra sarı, kırmızı, yayaya 5 s yeşil, yanıp sönen yeşil, kırmızı + sarı. Buton 3. saniyede basılır; uygulamada istediğiniz an tıklayabilirsiniz.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.TrafficLight(Lib)),
            new WorldTemplate("ps5-car", "Kumandalı araba (ESP32 + PS5 kumanda)", "Arabanın kendi kartı var: ESP32 ve DRV8833 vidalı klemensli taşıyıcı PCB'de, USB-C ile şarj edilen 2S 18650 paketle çalışır (bilgisayara bağlı değil). Motorlar gerçek bir kitteki gibi aynalı takılı, iki mil tek aks üzerinde. Kartı seçip 'Kartı düzenle' ile devresini, PCB'sini ve Bluepad32 kodunu açın. R2 gaz, L2 geri, sol çubuk direksiyon. Kumanda: bilgisayara USB ya da Bluetooth ile bağlı gerçek DualSense, yoksa ekrandaki sanal kumanda (klavye: W/S gaz, A/D direksiyon).", Ps5Car),
            new WorldTemplate("kapali-gezgin", "Kapalı gövdeli gezgin: kablo delikleri, ESP32 kare çizer", "Elektronik (4×AA, ESP32, DRV8833) vidalı kapaklı 120 × 70 mm PLA kutuda. Motor kabloları kutunun arka duvarındaki 10 mm'lik delikten çıkar, şasenin boş arka kenarından alttaki motorlara iner; kablolar duvarların içinden değil, delikten geçer. ESP32 süreyle kare çizer: 1,6 s ileri, yerinde çeyrek dönüş.", EnclosedRover),   // [route]
            new WorldTemplate("ps5-plane", "PS5 kumandalı uçak: pistten kalkış, ESP32 uçuş kartı", "1 m'lik eğitim uçağı asfalt pistin başında duruyor: balsa kabin, karbon kuyruk borusu, kanatçıklı Clark Y kanat, itme çubuklu irtifa ve yön dümenleri, burun ve ana tekerlekleri serbest dönen üç tekerlekli iniş takımı, 2212 fırçasız motor, 8×4,5\" pervane, 3S LiPo, ESP32 + MPU-6050 uçuş kartı (Bluepad32). Options (klavyede Enter) motoru kurar; R2 (F) gaz, sağ çubuk (I/J/K/L) yatış ve yunuslama, sol çubuk (A/D) yön dümeni, △ (R) Dengeli / Manuel mod, ✕ (Space) motoru keser. Hız ~10 m/s'yi geçince çubuğu geri çekin, uçak kalkar. Bluetooth menzili dışına çıkarsa motor kesilir ve uçak daire çizerek süzülür. Kamera: Pilot gözü.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.Ps5Plane(Lib)),   // [ps5plane]
            new WorldTemplate("water-rocket", "Su roketi: 2 L PET şişe, 0.6 L su, 4 bar", "Rampadaki şişe 0.5 s sonra bırakılır: basınçlı hava önce suyu 21.5 mm ağızdan Bernoulli hızıyla iter (~0.1 s, yüzlerce newton itki), sonra kalan hava püskürür. Kütle düşer, sürükleme ve üç kanatçığın kaldırma kuvveti roketi dik tutar. Suyu ya da basıncı değiştirin; 9 bar'ı aşan basınçta PET şişe patlar.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.WaterRocket(Lib)),   // [gas]
            new WorldTemplate("sugar-motor", "Katı yakıtlı motor test standı (KNSB)", "Alüminyum gövdede iki BATES şeker yakıt tanesi, 6 mm boğazlı genişleyen lüle. 0.1 s'de ateşlenir; yanma hızı basınca bağlı (r = a·pⁿ), basınç yanma yüzeyi ile lüle akışının dengesinden çıkar, itki ve yanma süresi buradan gelir. Boğazı küçültün: basınç yükselir, gövde patlar.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.SugarMotorStand(Lib)),   // [gas]
            new WorldTemplate("solid-rocket", "Katı yakıtlı roket: e-kibritle fırlatma ve paraşüt", "41 mm karton gövde, ogive burun, üç kontrplak kanatçık ve iki BATES tanesiyle KNSB motor (≈127 N·s, G sınıfı). Rampadaki 9 V pil ve 2 s geri sayımla kapanan anahtar e-kibriti devreden ateşler; ateşleyicinin sıcak gazı yakıt yüzeyini ısıtır, yüzey tutuşma sıcaklığına çıkınca alev yayılır. Roket ~1 km'ye çıkar, tepe noktasında 45 cm paraşüt açılır. Görünüm → Isı kamerası motorun ısınmasını gösterir.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.SolidRocket(Lib)),   // [gas][launch]
            new WorldTemplate("surfaces", "Malzeme ve yüzey vitrini", "Ahşap damarı, karbon örgü, fırçalanmış ve parlatılmış alüminyum, siyah eloksal, paslı çelik, kraft karton, kauçuk ve 0,2 mm katmanlı PLA baskı yan yana. İki özdeş alüminyum blok 150 °C'den soğumaya bırakılır: yalnız yüzeyleri farklıdır. Eloksal blok (ε 0,85) ışınımla da ısı verir ve parlatılmış bloktan (ε 0,05) belirgin biçimde hızlı soğur; Görünüm → Isı kamerası farkı gösterir.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.Surfaces(Lib)),   // [surface]
            new WorldTemplate("containers", "Kap ve bilyeler: içi boş parçalar", "Modelleyicide yapılmış 2.5 mm duvarlı bir tepsi ve dik bir boru. Fizik içi boş parçaları dışbükey parçalara (zemin ve duvarlar, boru dilimleri) ayırır: çelik bilyeler ve tahta küpler tepsinin iç zeminine düşer, borunun üstünden bırakılan bilye deliğin içinden yere iner. Görünüm → Çarpışma şekilleri parçaları gösterir.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.Containers(Lib)),   // [hull] concave
            new WorldTemplate("su-bardagi", "Su bardağı: servo suyu döker", "ESP32 yan yatırılmış bir SG90'ın kolundaki cam bardağı 3 saniyede 90° devirir; 193 ml su kâseye dökülür, bardak geri dikilir. Su parçacıklarla çözülür: gerçek yoğunluk, viskozite ve yüzey gerilimi; duvarlara basıncı ve bardağa ağırlığı gerçek. Render'da daha çok parçacıkla tam hesaplanır.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.GlassPour(Lib)),
            new WorldTemplate("sarkac", "Sarkaç: periyot ve sönüm", "280 mm alüminyum lama ve Ø40 çelik top, rulmanlı milde 30°'den bırakılır. Periyodu bileşik sarkaç formülüne (T = 2π√(I/mgh), büyük genlik düzeltmesiyle ≈ 1,11 s) uyar; hava sürüklemesi ve yatak sürtünmesiyle yavaşça söner.", () => RobotTwin.CoreSim.Sandbox.WorldExamples.Pendulum(Lib)),
            // [sense] Examples/SenseTemplates.cs
            new WorldTemplate("line-follower", "Çizgi izleyen robot (mat, solmuş, parlak bant)", "Arduino Uno + 5 kanallı TCRT5000 modülü 6 mm yükseklikte, DRV8833 ve iki TT motor. Pist mat bantta sorunsuz; solmuş bantta sensörler beyaz görür, parlak bant pencere ışığını sensöre yansıtır ve robot çizgiyi kaybeder.", SenseTemplates.LineFollower),
            new WorldTemplate("sensor-rover", "Sensör gezgini (kamera, LiDAR, GPS, IMU)", "Duvarlı odada daire çizen gezgin: OV2640 kamera kendi çözünürlüğüyle görür, 360° LiDAR taraması çizilir, GPS/IMU/pusula/enkoder okumaları Sensörler panelinde canlı.", SenseTemplates.SensorRover),
            new WorldTemplate("camera-line", "ESP32 + OV2640 kamera ile bant bulma", "Direkteki kamera pisti yukarıdan görür; ESP32 çizimi esp_camera.h ile 160×120 gri kare alır, en koyu sütunu bant sayar. Işığı azaltınca pozlama, kazanç ve gürültü değişir.", SenseTemplates.CameraLine),
            new WorldTemplate("wind-lab", "Rüzgâr, fan, konveyör ve ifadeler", "Türbülanslı rüzgâr, bir masa fanının üflediği pinpon topu, konveyör bölgesi, ifadeyle havada tutulan plaka ve mıknatısa çekilen bilye. Ortam ve Betikler sekmelerine bakın.", SdkWorldTemplates.WindLab),   // [sdk]
            // [terrain] sculpted terrain worlds (Examples/TerrainTemplates.cs)
            new WorldTemplate("arazi-parkuru", "Arazi parkuru", "10 × 6 m engebeli dış mekân: tepeler, asfalt yol, hız kesiciler, bordür, çakıl, çamur ve kum (gerçek batma ve kayma), rampa, merdiven, koniler, kayalar, ağaçlar. Araba yolun başında.", () => TerrainTemplates.Parkour(Car)),
            new WorldTemplate("kapali-atolye", "Kapalı atölye", "6 × 4 m fayans zemin; kauçuk paspas, halı, beton ve ıslak zemin bölgeleri, duvarlar, rampa ve basamaklar. Araba fayansın üstünde.", () => TerrainTemplates.Workshop(Car))
        };

        public static WorldTemplate? Find(string id)
        {
            foreach (var t in All)
            {
                if (t.Id == id) return t;
            }

            return null;
        }

        private static void Tie(Schematic s, SchematicPart p, string pin, string net) => VehicleTemplates.Tie(s, Lib, p, pin, net);

        public static ProjectPackage PowerBoard()
        {
            var s = new Schematic { Title = "Güç kartı" };
            var bat = s.AddPart(Lib, "bat-9v", new GridPoint(0, 0));
            var sw = s.AddPart(Lib, "toggle-spst", new GridPoint(14, -6));
            var j = s.AddPart(Lib, "hdr-1x2", new GridPoint(28, 0));
            Tie(s, bat, "+", "VBAT");
            Tie(s, bat, "-", "GND");
            if (s.FindPart(sw.RefDes) is { } swp && Lib.Find(swp.PartId)?.FindPin("A") != null)
            {
                Tie(s, sw, "A", "VBAT");
                Tie(s, sw, "B", "VOUT");
                sw.Parameters["closed"] = "true";
            }
            else
            {
                s.Parts.Remove(sw);
            }

            Tie(s, j, "1", s.Parts.Contains(sw) ? "VOUT" : "VBAT");
            Tie(s, j, "2", "GND");
            return new ProjectPackage { Schematic = s };
        }

        public static ProjectPackage LedBoard()
        {
            var s = new Schematic { Title = "LED kartı" };
            var j = s.AddPart(Lib, "hdr-1x2", new GridPoint(0, 0));
            var r1 = s.AddPart(Lib, "res-470", new GridPoint(16, -4));
            var r2 = s.AddPart(Lib, "res-470", new GridPoint(16, 4));
            var d1 = s.AddPart(Lib, "led5-red", new GridPoint(32, -4));
            var d2 = s.AddPart(Lib, "led5-green", new GridPoint(32, 4));
            Tie(s, j, "1", "VIN");
            Tie(s, j, "2", "GND");
            Tie(s, r1, "A", "VIN");
            Tie(s, r1, "B", "A1");
            Tie(s, d1, "A", "A1");
            Tie(s, d1, "K", "GND");
            Tie(s, r2, "A", "VIN");
            Tie(s, r2, "B", "A2");
            Tie(s, d2, "A", "A2");
            Tie(s, d2, "K", "GND");
            return new ProjectPackage { Schematic = s };
        }

        private static WorldDocument TwoBoards()
        {
            var w = new WorldDocument { Title = "İki kart" };
            var power = w.AddBoard(w.AddDesign(PowerBoard()), new WorldPose { X = -0.06 });
            var leds = w.AddBoard(w.AddDesign(LedBoard()), new WorldPose { X = 0.06, Yaw = 0 });
            w.Connect(new CableEnd { ObjectId = power.Id, Pin = "J1.1" }, new CableEnd { ObjectId = leds.Id, Pin = "J1.1" }, 24, "#E5484D");
            w.Connect(new CableEnd { ObjectId = power.Id, Pin = "J1.2" }, new CableEnd { ObjectId = leds.Id, Pin = "J1.2" }, 24, "#1F1F23");
            return w;
        }

        private static WorldDocument Displays()
        {
            var w = new WorldDocument { Title = "Ekranlar" };
            var uno = w.AddComponent(Lib, "arduino-uno", new WorldPose { X = -0.06, Y = 0 });
            uno.Parameters["firmware"] = "builtin:i2c-displays.hex";
            var lcd = w.AddComponent(Lib, "lcd1602-i2c", new WorldPose { X = 0.08, Y = 0.05, Z = 0.0 });
            var oled = w.AddComponent(Lib, "oled-128x64", new WorldPose { X = 0.08, Y = -0.04 });
            var pot = w.AddComponent(Lib, "pot-10k", new WorldPose { X = -0.06, Y = -0.08 });
            pot.Parameters["position"] = "0.7";
            foreach (var d in new[] { lcd, oled })
            {
                w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "5V" }, new CableEnd { ObjectId = d.Id, Pin = "VCC" }, 26, "#E5484D");
                w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "GND2" }, new CableEnd { ObjectId = d.Id, Pin = "GND" }, 26, "#1F1F23");
                w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "A4" }, new CableEnd { ObjectId = d.Id, Pin = "SDA" }, 26, "#3E7CC8");
                w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "A5" }, new CableEnd { ObjectId = d.Id, Pin = "SCL" }, 26, "#F2D04B");
            }

            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "5V" }, new CableEnd { ObjectId = pot.Id, Pin = "A" }, 26, "#E5484D");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "GND3" }, new CableEnd { ObjectId = pot.Id, Pin = "B" }, 26, "#1F1F23");
            w.Connect(new CableEnd { ObjectId = uno.Id, Pin = "A0" }, new CableEnd { ObjectId = pot.Id, Pin = "W" }, 26, "#F2A33B");
            return w;
        }

        /// <summary>The car, laid out for the detailed TT motor model (its shaft position and gearbox height).</summary>
        private static WorldDocument Car()
        {
            var (pivot, _, height) = World.ObjectModels.Mount(Lib, "motor-tt");
            return pivot.Y > 0.005
                ? RobotTwin.CoreSim.Sandbox.WorldExamples.Car(Lib, height, pivot.X, pivot.Z, pivot.Y)
                : RobotTwin.CoreSim.Sandbox.WorldExamples.Car(Lib);
        }

        /// <summary>
        /// [radio] The gamepad car (CoreSim WorldExamples.GamepadCar): its controller board is a real design
        /// (schematic, routed PCB, Bluepad32 firmware) running on the car's own battery, laid out for the
        /// detailed TT motor model so each wheel sits on its shaft and both shafts share one axle.
        /// </summary>
        /// <summary>[route] The enclosed rover, laid out for the detailed TT motor model like the car.</summary>
        private static WorldDocument EnclosedRover()
        {
            var (pivot, _, height) = World.ObjectModels.Mount(Lib, "motor-tt");
            return pivot.Y > 0.005
                ? RobotTwin.CoreSim.Sandbox.WorldExamples.EnclosedRover(Lib, height, pivot.X, pivot.Z, pivot.Y)
                : RobotTwin.CoreSim.Sandbox.WorldExamples.EnclosedRover(Lib);
        }

        private static WorldDocument Ps5Car()
        {
            var (pivot, _, height) = World.ObjectModels.Mount(Lib, "motor-tt");
            return RobotTwin.CoreSim.Sandbox.WorldExamples.GamepadCar(Lib, height, pivot.X, pivot.Z, pivot.Y);
        }

        private static WorldDocument Sonar()
        {
            var w = new WorldDocument { Title = "Ultrasonik mesafe" };
            var stand = w.AddBody("box", 0.06, 0.04, 0.02, new WorldPose(), "AHŞAP");
            stand.Name = "Sehpa (masaya vidalı)";   // [phys] shown as clamped: it is "Sabit"
            stand.IsStatic = true;
            var sensor = w.AddComponent(Lib, "hc-sr04", new WorldPose { Z = RobotTwin.CoreSim.Sandbox.WorldExamples.OnPins(0.02) });   // on its header pins
            sensor.Name = "HC-SR04";
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = 0.12 });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = sensor.Id, Pin = "VCC" }, 24, "#E5484D");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = sensor.Id, Pin = "GND" }, 24, "#1F1F23");
            var near = w.AddBody("box", 0.08, 0.08, 0.08, new WorldPose { X = -0.35, Y = 0.02 }, "AHŞAP");
            near.Name = "Engel";
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = sensor.Id, ObjectB = stand.Id });
            return w;
        }

        private static WorldDocument MotorWheel()
        {
            var w = new WorldDocument { Title = "Motor ve tekerlek" };
            var bat = w.AddComponent(Lib, "bat-4aa", new WorldPose { X = -0.11, Y = 0 });
            // [phys] A bench test: the stand is screwed to the table ("Sabit") and tall enough that the
            // wheel clearly spins in the air instead of hovering a few millimetres above the floor.
            const double standH = 0.05;
            var stand = w.AddBody("box", 0.06, 0.04, standH, new WorldPose { X = 0.03, Y = 0 }, "AHŞAP");
            stand.Name = "Test sehpası (masaya vidalı)";
            stand.IsStatic = true;
            var motor = w.AddComponent(Lib, "motor-tt", new WorldPose { X = 0.03, Y = 0, Z = standH });
            // The wheel hub slides onto the end of the TT output shaft (from the model).
            var (pivot, _, _) = World.ObjectModels.Mount(Lib, "motor-tt");
            var wheel = w.AddBody("wheel", 0.065, 0.026, 0.026, new WorldPose { X = motor.Pose.X + pivot.X, Y = pivot.Y + 0.015, Z = motor.Pose.Z + pivot.Z - 0.0325 }, "KAUÇUK");
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = motor.Id, ObjectB = stand.Id });
            w.Joints.Add(new WorldJoint { Id = w.NextId("J"), Kind = WorldJointKind.Fixed, ObjectA = wheel.Id, ObjectB = motor.Id, PortB = "shaft" });
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "+" }, new CableEnd { ObjectId = motor.Id, Pin = "A" }, 22, "#E5484D");
            w.Connect(new CableEnd { ObjectId = bat.Id, Pin = "-" }, new CableEnd { ObjectId = motor.Id, Pin = "B" }, 22, "#1F1F23");
            return w;
        }
    }
}
