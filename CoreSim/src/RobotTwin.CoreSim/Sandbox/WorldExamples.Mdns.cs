using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Formats;

namespace RobotTwin.CoreSim.Sandbox
{
    public static partial class WorldExamples
    {
        /// <summary>
        /// [mdns] Two ESP32s finding each other by name: one opens an access point and answers to isik.local with an HTTP
        /// service, the other joins it and asks for the name and the service every two seconds (ESPmDNS, in both modes).
        /// </summary>
        public static WorldDocument MdnsPair(PartLibrary lib)
        {
            var w = new WorldDocument
            {
                Title = "mDNS ile ad bulma",
                Description = "Bir kart erişim noktası açıp isik.local adıyla HTTP servisi duyurur; öbürü bağlanıp adı ve servisi sorar."
            };
            var table = w.AddBody("box", 0.6, 0.2, 0.018, new WorldPose { X = -0.3, Y = -0.1 }, "AHŞAP");
            table.Name = "Masa";
            table.IsStatic = true;
            const double deck = 0.018;
            var host = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = -0.25, Y = -0.03, Z = OnPins(deck) });
            host.Name = "isik";
            host.Firmware = new FirmwareSource { FileName = "isik.ino", Target = "esp32:esp32:esp32", Source = MdnsHostSketch };
            var asker = w.AddComponent(lib, "esp32-devkit-v1", new WorldPose { X = 0.15, Y = -0.03, Z = OnPins(deck) });
            asker.Name = "Soran";
            asker.Firmware = new FirmwareSource { FileName = "soran.ino", Target = "esp32:esp32:esp32", Source = MdnsAskSketch };
            foreach (var o in new[] { host, asker }) w.Joints.Add(new WorldJoint { Id = w.NextId("J"), ObjectA = o.Id, ObjectB = table.Id });
            return w;
        }

        /// <summary>The named board of <see cref="MdnsPair"/>.</summary>
        public const string MdnsHostSketch = @"// mDNS: erişim noktası açar, isik.local adına ve 80. porttaki HTTP servisine yanıt verir.
#include <WiFi.h>
#include <ESPmDNS.h>

void setup() {
  Serial.begin(115200);
  WiFi.softAP(""RW-mDNS"", ""12345678"");
  if (!MDNS.begin(""isik"")) {
    Serial.println(""mDNS baslamadi"");
    return;
  }
  MDNS.addService(""http"", ""tcp"", 80);
  Serial.println(""isik.local hazir"");
}

void loop() {
  delay(100);
}
";

        /// <summary>The asking board of <see cref="MdnsPair"/>.</summary>
        public const string MdnsAskSketch = @"// mDNS: erişim noktasına bağlanır, iki saniyede bir isik.local'ı ve HTTP servislerini sorar.
#include <WiFi.h>
#include <ESPmDNS.h>

void setup() {
  Serial.begin(115200);
  WiFi.begin(""RW-mDNS"", ""12345678"");
  while (WiFi.status() != WL_CONNECTED) delay(50);
  MDNS.begin(""soran"");
  Serial.println(""baglandi"");
}

void loop() {
  IPAddress ip = MDNS.queryHost(""isik"", 1000);
  Serial.printf(""isik.local = %s\n"", ip.toString().c_str());
  int n = MDNS.queryService(""http"", ""tcp"");
  if (n > 0) Serial.printf(""http servisi: %s port %u\n"", MDNS.hostname(0).c_str(), (unsigned)MDNS.port(0));
  else Serial.println(""http servisi yok"");
  delay(2000);
}
";
    }
}
