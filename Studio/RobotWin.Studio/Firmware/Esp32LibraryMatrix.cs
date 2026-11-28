using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Analog.Devices;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio.Firmware
{
    /// <summary>
    /// [libs] Popular third-party Arduino libraries on the ESP32, in both modes: each library's own small sketch is built
    /// for the fast mode (MSVC against the HAL model) and for the real chip (Arduino-ESP32 with arduino-cli), then run for a
    /// few seconds on a lone ESP32 DevKitC in each mode; what it printed is matched with what it should print there (a
    /// sensor library on a board with no sensor reports none). --esp32-libs writes the table (see the Code guide's
    /// "ESP32 uyumluluğu"). The libraries come from a folder (--esp32-libroot), installed there by arduino-cli.
    /// </summary>
    internal static class Esp32LibraryMatrix
    {
        private sealed record Case(string Library, string Folder, string Sketch, string Expect, string Note = "", int[]? Pins = null);

        // Each sketch prints one line starting "SONUC " once it has used the library; Expect is a regex over that line, with
        // each of Pins' final output added to it (" p18=1000 Hz %25.0", or the level) so the two modes' waves compare too.
        private static readonly Case[] Cases =
        {
            // not a library: a sketch written for the 2.x core (LEDC by channel, the 2.x timers, the IDF 4.4 task watchdog)
            new Case("Arduino-ESP32 2.x çağrıları", "",
                "#include <esp_task_wdt.h>\nvolatile int ticks=0;void IRAM_ATTR onTick(){ticks++;}\nvoid setup(){Serial.begin(115200);ledcSetup(3,5000,10);ledcAttachPin(18,3);ledcWrite(3,512);hw_timer_t*t=timerBegin(0,80,true);timerAttachInterrupt(t,&onTick,true);timerAlarmWrite(t,10000,true);timerAlarmEnable(t);int w=esp_task_wdt_init(5,false);delay(105);timerAlarmDisable(t);int n=ticks;delay(50);Serial.printf(\"SONUC duty=%u freq=%u ticks=%d after=%d wdt=%d\\n\",(unsigned)ledcRead(3),(unsigned)ledcReadFreq(3),n,ticks-n,w);}\nvoid loop(){delay(100);}\n",
                @"^SONUC duty=512 freq=5000 ticks=1[01] after=0 wdt=0$", "çekirdek 2.x için yazılmış çizim; gerçek kart derlemesinde rw_esp32_v2.h ile"),
            new Case("ArduinoJson", "ArduinoJson",
                "#include <ArduinoJson.h>\nvoid setup(){Serial.begin(115200);JsonDocument d;deserializeJson(d,\"{\\\"a\\\":1,\\\"b\\\":[2,3]}\");d[\"c\"]=4.5;String s;serializeJson(d,s);Serial.printf(\"SONUC a=%d b1=%d %s\\n\",(int)d[\"a\"],(int)d[\"b\"][1],s.c_str());}\nvoid loop(){delay(100);}\n",
                @"^SONUC a=1 b1=3 \{""a"":1,""b"":\[2,3\],""c"":4\.5\}$"),
            new Case("PubSubClient", "PubSubClient",
                "#include <WiFi.h>\n#include <PubSubClient.h>\nWiFiClient net;PubSubClient mqtt(net);\nvoid setup(){Serial.begin(115200);WiFi.begin(\"RobotWin\",\"12345678\");unsigned long t0=millis();while(WiFi.status()!=WL_CONNECTED&&millis()-t0<4000)delay(50);mqtt.setServer(\"10.0.0.1\",1883);mqtt.setSocketTimeout(1);bool ok=mqtt.connect(\"rw\");Serial.printf(\"SONUC wifi=%d connect=%d state=%d\\n\",WiFi.status()==WL_CONNECTED,ok,mqtt.state());}\nvoid loop(){delay(100);}\n",
                @"^SONUC wifi=1 connect=0 state=-[24]$", "dünyada MQTT aracısı yok: Wi-Fi'ye bağlanır, aracıya bağlanamaz"),
            new Case("Adafruit SSD1306", "Adafruit_SSD1306",
                "#include <Wire.h>\n#include <Adafruit_GFX.h>\n#include <Adafruit_SSD1306.h>\nAdafruit_SSD1306 oled(128,64,&Wire,-1);\nvoid setup(){Serial.begin(115200);bool ok=oled.begin(SSD1306_SWITCHCAPVCC,0x3C);oled.clearDisplay();oled.setTextSize(1);oled.setTextColor(SSD1306_WHITE);oled.setCursor(0,0);oled.print(\"RobotWin\");oled.display();Serial.printf(\"SONUC begin=%d w=%d\\n\",ok,oled.width());}\nvoid loop(){delay(100);}\n",
                @"^SONUC begin=[01] w=128$", "ekran takılı değil: çizim çalışır, I²C yanıtsız"),
            new Case("DHT sensor library", "DHT_sensor_library",
                "#include <DHT.h>\nDHT dht(4,DHT22);\nvoid setup(){Serial.begin(115200);dht.begin();delay(2100);float t=dht.readTemperature();Serial.printf(\"SONUC nan=%d\\n\",isnan(t)?1:0);}\nvoid loop(){delay(100);}\n",
                @"^SONUC nan=1$", "duyarga takılı değil: okuma NaN"),
            new Case("DallasTemperature (OneWire)", "DallasTemperature",
                "#include <OneWire.h>\n#include <DallasTemperature.h>\nOneWire ow(4);DallasTemperature ds(&ow);\nvoid setup(){Serial.begin(115200);ds.begin();Serial.printf(\"SONUC count=%d\\n\",ds.getDeviceCount());}\nvoid loop(){delay(100);}\n",
                @"^SONUC count=0$", "duyarga takılı değil: 0 aygıt"),
            new Case("AccelStepper", "AccelStepper",
                "#include <AccelStepper.h>\nAccelStepper st(AccelStepper::DRIVER,18,19);\nvoid setup(){Serial.begin(115200);st.setMaxSpeed(2000);st.setAcceleration(4000);st.moveTo(400);unsigned long t0=millis();while(st.distanceToGo()!=0)st.run();Serial.printf(\"SONUC pos=%ld ms=%lu\\n\",st.currentPosition(),millis()-t0);}\nvoid loop(){delay(100);}\n",
                @"^SONUC pos=400 ms=(\d+)$"),
            new Case("FastLED", "FastLED",
                "#include <FastLED.h>\nCRGB leds[8];\nvoid setup(){Serial.begin(115200);FastLED.addLeds<WS2812B,5,GRB>(leds,8);fill_rainbow(leds,8,0,32);FastLED.show();Serial.printf(\"SONUC r0=%d g0=%d\\n\",leds[0].r,leds[0].g);}\nvoid loop(){delay(100);}\n",
                @"^SONUC r0=255 g0=1$", "fill_rainbow'un doygunluğu 240: yeşil 1"),
            new Case("TinyGPSPlus", "TinyGPSPlus",
                "#include <TinyGPSPlus.h>\nTinyGPSPlus gps;\nconst char* s=\"$GPRMC,123519,A,4807.038,N,01131.000,E,022.4,084.4,230394,003.1,W*6A\\r\\n$GPGGA,123519,4807.038,N,01131.000,E,1,08,0.9,545.4,M,46.9,M,,*47\\r\\n\";\nvoid setup(){Serial.begin(115200);for(const char*p=s;*p;p++)gps.encode(*p);Serial.printf(\"SONUC lat=%.4f lng=%.4f sats=%u\\n\",gps.location.lat(),gps.location.lng(),(unsigned)gps.satellites.value());}\nvoid loop(){delay(100);}\n",
                @"^SONUC lat=48\.1173 lng=11\.5167 sats=8$"),
            new Case("Bounce2", "Bounce2",
                "#include <Bounce2.h>\nBounce b;\nvoid setup(){Serial.begin(115200);b.attach(4,INPUT_PULLUP);b.interval(5);for(int i=0;i<20;i++){b.update();delay(1);}Serial.printf(\"SONUC read=%d\\n\",b.read());}\nvoid loop(){delay(100);}\n",
                @"^SONUC read=1$", "çekme direnciyle boştaki giriş: 1"),
            new Case("Adafruit MPU6050", "Adafruit_MPU6050",
                "#include <Adafruit_MPU6050.h>\nAdafruit_MPU6050 mpu;\nvoid setup(){Serial.begin(115200);Serial.printf(\"SONUC begin=%d\\n\",mpu.begin());}\nvoid loop(){delay(100);}\n",
                @"^SONUC begin=0$", "duyarga takılı değil: bulunamaz"),
            new Case("Adafruit BME280", "Adafruit_BME280_Library",
                "#include <Adafruit_BME280.h>\nAdafruit_BME280 bme;\nvoid setup(){Serial.begin(115200);Serial.printf(\"SONUC begin=%d\\n\",bme.begin(0x76));}\nvoid loop(){delay(100);}\n",
                @"^SONUC begin=0$", "duyarga takılı değil: bulunamaz"),
            new Case("MFRC522", "MFRC522",
                "#include <SPI.h>\n#include <MFRC522.h>\nMFRC522 rfid(5,22);\nvoid setup(){Serial.begin(115200);SPI.begin();rfid.PCD_Init();byte v=rfid.PCD_ReadRegister(MFRC522::VersionReg);Serial.printf(\"SONUC ver=%02X\\n\",v);}\nvoid loop(){delay(100);}\n",
                @"^SONUC ver=(00|FF)$", "okuyucu takılı değil: sürüm 00 / FF"),
            new Case("LiquidCrystal I2C", "LiquidCrystal_I2C",
                "#include <Wire.h>\n#include <LiquidCrystal_I2C.h>\nLiquidCrystal_I2C lcd(0x27,16,2);\nvoid setup(){Serial.begin(115200);lcd.init();lcd.backlight();lcd.print(\"RobotWin\");Serial.printf(\"SONUC ok=1\\n\");}\nvoid loop(){delay(100);}\n",
                @"^SONUC ok=1$", "kütüphane yalnız AVR'yi bildirir; ekran takılı değil"),
            new Case("NewPing", "NewPing",
                "#include <NewPing.h>\nNewPing sonar(12,13,200);\nvoid setup(){Serial.begin(115200);Serial.printf(\"SONUC cm=%lu\\n\",(unsigned long)sonar.ping_cm());}\nvoid loop(){delay(100);}\n",
                @"^SONUC cm=0$", "duyarga takılı değil: yankı yok"),
            new Case("IRremote", "IRremote",
                "#include <IRremote.hpp>\nvoid setup(){Serial.begin(115200);IrSender.begin(4);IrSender.sendNEC(0x10,0x20,0);Serial.printf(\"SONUC sent=1\\n\");}\nvoid loop(){delay(100);}\n",
                @"^SONUC sent=1$"),
            new Case("PID", "PID",
                "#include <PID_v1.h>\ndouble in=20,out=0,set=25;PID pid(&in,&out,&set,2,0.5,0.1,DIRECT);\nvoid setup(){Serial.begin(115200);pid.SetMode(AUTOMATIC);pid.SetSampleTime(10);delay(20);pid.Compute();Serial.printf(\"SONUC out=%.2f\\n\",out);}\nvoid loop(){delay(100);}\n",
                @"^SONUC out=(\d+\.\d\d)$"),
            new Case("NTPClient", "NTPClient",
                "#include <WiFi.h>\n#include <WiFiUdp.h>\n#include <NTPClient.h>\nWiFiUDP udp;NTPClient ntp(udp,\"10.0.0.1\");\nvoid setup(){Serial.begin(115200);WiFi.begin(\"RobotWin\",\"12345678\");unsigned long t0=millis();while(WiFi.status()!=WL_CONNECTED&&millis()-t0<4000)delay(50);ntp.begin();bool ok=ntp.forceUpdate();Serial.printf(\"SONUC wifi=%d update=%d\\n\",WiFi.status()==WL_CONNECTED,ok);}\nvoid loop(){delay(100);}\n",
                @"^SONUC wifi=1 update=0$", "dünyada saat sunucusu yok: Wi-Fi'ye bağlanır, güncellenmez"),
            new Case("Keypad", "Keypad",
                "#include <Keypad.h>\nchar keys[2][2]={{'1','2'},{'3','4'}};byte rows[2]={13,12};byte cols[2]={14,27};\nKeypad kp(makeKeymap(keys),rows,cols,2,2);\nvoid setup(){Serial.begin(115200);char k=kp.getKey();Serial.printf(\"SONUC key=%d\\n\",(int)k);}\nvoid loop(){delay(100);}\n",
                @"^SONUC key=0$", "tuş takımı takılı değil: tuş yok"),
            new Case("RTClib", "RTClib",
                "#include <RTClib.h>\nRTC_DS3231 rtc;\nvoid setup(){Serial.begin(115200);bool ok=rtc.begin();DateTime t(2024,2,29,12,30,0);Serial.printf(\"SONUC begin=%d unix=%lu dow=%d\\n\",ok,(unsigned long)t.unixtime(),t.dayOfTheWeek());}\nvoid loop(){delay(100);}\n",
                @"^SONUC begin=0 unix=1709209800 dow=4$", "saat modülü takılı değil: bulunamaz; tarih hesabı çalışır"),
            new Case("HX711", "HX711_Arduino_Library",
                "#include <HX711.h>\nHX711 sc;\nvoid setup(){Serial.begin(115200);sc.begin(16,4);pinMode(16,INPUT_PULLDOWN);delay(1);Serial.printf(\"SONUC ready=%d raw=%ld\\n\",sc.is_ready(),sc.read());}\nvoid loop(){delay(100);}\n",
                @"^SONUC ready=1 raw=0$", "çevirici takılı değil, veri ucu aşağı çekili: 24 bit sıfır okunur"),
            new Case("U8g2", "U8g2",
                "#include <Wire.h>\n#include <U8g2lib.h>\nU8G2_SSD1306_128X64_NONAME_F_HW_I2C u8g2(U8G2_R0);\nvoid setup(){Serial.begin(115200);u8g2.begin();u8g2.clearBuffer();u8g2.setFont(u8g2_font_6x10_tf);u8g2.drawStr(0,10,\"RobotWin\");u8g2.sendBuffer();Serial.printf(\"SONUC w=%d\\n\",(int)u8g2.getDisplayWidth());}\nvoid loop(){delay(100);}\n",
                @"^SONUC w=128$", "ekran takılı değil: çizim çalışır, I²C yanıtsız"),
            new Case("WiFiManager", "WiFiManager",
                "#include <WiFiManager.h>\nWiFiManager wm;\nvoid setup(){Serial.begin(115200);wm.setConfigPortalTimeout(1);wm.setDebugOutput(false);Serial.printf(\"SONUC ok=1\\n\");}\nvoid loop(){delay(100);}\n",
                @"^SONUC ok=1$"),
        };

        private sealed class ModeResult
        {
            public bool Built;
            public string BuildNote = string.Empty;
            public string? Line;
            public string? Tail;   // with no result line: the last line it printed (a panic, an error)
            public string Serial = string.Empty;
            public bool Ran;
        }

        /// <param name="filter">Only the libraries whose name holds one of these (comma separated), or null for all.</param>
        public static int Run(string reportPath, string libraryRoot, double seconds, string? filter = null)
        {
            ArduinoCompiler.SessionLibraryRoots.Add(libraryRoot);
            var only = filter?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            // what a mode printed when it did not give the expected line, beside the report
            string serialDir = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? ".", Path.GetFileNameWithoutExtension(reportPath) + "-seri");
            var report = new StringBuilder();
            report.AppendLine($"ESP32 kütüphane uyumluluğu · Arduino-ESP32 {ArduinoCompiler.Esp32CoreVersion} · {DateTime.Now:yyyy-MM-dd HH:mm}");
            report.AppendLine();
            report.AppendLine("| Kütüphane | Sürüm | Hızlı: derleme | Hızlı: çalışma | Donanım: derleme | Donanım: çalışma | Not |");
            report.AppendLine("| --- | --- | --- | --- | --- | --- | --- |");
            int failures = 0;
            foreach (var c in Cases)
            {
                if (only != null && !only.Any(o => c.Library.Contains(o, StringComparison.OrdinalIgnoreCase))) continue;
                string version = c.Folder.Length == 0 ? "—" : VersionOf(Path.Combine(libraryRoot, c.Folder));
                var fast = Fast(c, seconds);
                var hard = ArduinoCompiler.Esp32ToolchainInstalled ? Hardware(c, seconds) : new ModeResult { BuildNote = "araç zinciri yok" };
                if (!fast.Ran || !hard.Ran) failures++;
                foreach (var (r, mode) in new[] { (fast, "hizli"), (hard, "donanim") })
                {
                    if (r.Ran || r.Serial.Length == 0) continue;
                    Directory.CreateDirectory(serialDir);
                    File.WriteAllText(Path.Combine(serialDir, Regex.Replace(c.Library, @"[^A-Za-z0-9]+", "_") + "-" + mode + ".txt"), r.Serial, new UTF8Encoding(false));
                }

                string note = c.Note;
                if (fast.Line != null && hard.Line != null && fast.Line != hard.Line) note = (note.Length > 0 ? note + "; " : string.Empty) + $"hızlı `{fast.Line}`, donanım `{hard.Line}`";
                else if ((fast.Line ?? hard.Line) is { } line) note = (note.Length > 0 ? note + "; " : string.Empty) + $"`{line}`";
                report.AppendLine($"| {c.Library} | {version} | {Mark(fast.Built, fast.BuildNote)} | {Ran(fast)} | {Mark(hard.Built, hard.BuildNote)} | {Ran(hard)} | {note.Replace("|", "\\|")} |");
                File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            }

            File.WriteAllText(reportPath, report.ToString(), new UTF8Encoding(false));
            return failures == 0 ? 0 : 1;
        }

        private static string Mark(bool ok, string note) => ok ? "✓" : "— " + note.Replace("|", "\\|");

        private static string Ran(ModeResult r) => !r.Built ? "" : r.Ran ? "✓"
            : r.Line == null ? "— çıktı yok" + (r.Tail != null ? " (son satır: `" + r.Tail.Replace("|", "\\|").Replace("`", "'") + "`)" : string.Empty)
            : "— beklenmeyen: `" + r.Line + "`";

        private static string VersionOf(string folder)
        {
            string props = Path.Combine(folder, "library.properties");
            if (!File.Exists(props)) return "?";
            var line = File.ReadAllLines(props).FirstOrDefault(l => l.StartsWith("version=", StringComparison.Ordinal));
            return line?.Substring(8).Trim() ?? "?";
        }

        private static ModeResult Fast(Case c, double seconds)
        {
            var r = new ModeResult();
            var build = ArduinoCompiler.CompileEsp32(c.Sketch);
            r.Built = build.Success;
            if (!r.Built)
            {
                r.BuildNote = FirstError(build);
                return r;
            }

            Run(c, r, Esp32Parts.FastMode, seconds);
            return r;
        }

        private static ModeResult Hardware(Case c, double seconds)
        {
            var r = new ModeResult();
            var build = ArduinoCompiler.EnsureEsp32HardwareBuildAsync(c.Sketch).GetAwaiter().GetResult();
            r.Built = build.Success && ArduinoCompiler.CachedEsp32Hardware(c.Sketch) != null;
            if (!r.Built)
            {
                r.BuildNote = FirstError(build);
                return r;
            }

            Run(c, r, Esp32Parts.HardwareMode, seconds);
            return r;
        }

        private static string FirstError(BuildResult build)
        {
            var e = build.Diagnostics.FirstOrDefault(d => d.IsError);
            string text = e != null ? e.Message : build.Log.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Contains("error", StringComparison.OrdinalIgnoreCase)) ?? "derlenmedi";
            text = Regex.Replace(text, @"\s+", " ");
            return text.Length > 120 ? text.Substring(0, 120) + "…" : text;
        }

        private static void Run(Case c, ModeResult r, string mode, double seconds)
        {
            var w = new WorldDocument();
            var esp = w.AddComponent(PartLibrary.Default, Esp32Parts.DevKitCId, new WorldPose());
            esp.IsStatic = true;
            esp.Firmware = new FirmwareSource { Target = ArduinoCompiler.Esp32Target, FileName = "kutuphane.ino", Source = c.Sketch };
            var options = new RunnerOptions
            {
                Esp32Builder = Automation.StudioApiHost.BuildEsp32,
                Esp32HardwareBuilder = source => ArduinoCompiler.CachedEsp32Hardware(source) is { } built
                    ? (File.ReadAllBytes(built.Elf), built.Merged != null ? File.ReadAllBytes(built.Merged) : null)
                    : null,
                Esp32Mode = mode
            };

            try
            {
                using var runner = new WorldRunner(w, options);
                runner.Step(seconds);
                string serial = string.Concat(runner.ReadSerial(null, false).Values);
                r.Serial = serial;
                var lines = serial.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
                r.Line = lines.FirstOrDefault(l => l.StartsWith("SONUC ", StringComparison.Ordinal));
                if (r.Line != null && c.Pins != null && runner.Compiled != null)
                {
                    var pins = runner.Compiled.Circuit.Devices.OfType<DigitalPin>().ToList();
                    foreach (int gpio in c.Pins)
                    {
                        var pin = pins.FirstOrDefault(d => d.Id.EndsWith(".IO" + gpio.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
                        r.Line += pin == null ? $" p{gpio}=?"
                            : pin.PwmActive ? string.Format(CultureInfo.InvariantCulture, " p{0}={1:0} Hz %{2:0.0}", gpio, pin.PwmFrequency, pin.PwmDuty * 100)
                            : $" p{gpio}={(pin.Mode == PinMode.OutputHigh ? 1 : 0)}";
                    }
                }

                r.Ran = r.Line != null && Regex.IsMatch(r.Line, c.Expect);
                if (r.Line == null && lines.Count > 0) r.Tail = lines[^1].Length > 90 ? lines[^1].Substring(0, 90) + "…" : lines[^1];
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is IOException || ex is ArgumentException)
            {
                r.Line = "hata: " + ex.Message;
            }
        }
    }
}
