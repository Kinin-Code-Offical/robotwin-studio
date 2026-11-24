using System.Collections.Generic;

namespace RobotWin.Studio.Editor.Completion
{
    /// <summary>
    /// [code] Turkish one-line explanations of the Arduino API (functions, constants, the members of Serial, Wire, Servo,
    /// EEPROM, SPI and the ESP32 extras), keyed by name or "Class.member", plus the language's keywords and a few
    /// snippets. Signatures always come from the real headers; these only say what a call does.
    /// </summary>
    public static class ArduinoDocs
    {
        public static readonly IReadOnlyDictionary<string, string> Docs = new Dictionary<string, string>
        {
            ["setup"] = "Kart açılınca bir kez çalışır: pinleri, seri portu, kütüphaneleri hazırlayın.",
            ["loop"] = "setup()'tan sonra durmadan tekrar tekrar çalışır.",
            ["pinMode"] = "Pini giriş (INPUT), çıkış (OUTPUT) ya da iç pull-up'lı giriş (INPUT_PULLUP) yapar.",
            ["digitalWrite"] = "Çıkış pinini HIGH (besleme gerilimi) ya da LOW (0 V) yapar.",
            ["digitalRead"] = "Pindeki mantık seviyesini okur: HIGH ya da LOW.",
            ["analogRead"] = "Analog pindeki gerilimi okur: Uno'da 0–1023 (10 bit, 0–5 V), ESP32'de 0–4095 (12 bit).",
            ["analogWrite"] = "PWM çıkışı: 0 (hep kapalı) ile 255 (hep açık) arası doluluk. Uno'da yalnızca ~ işaretli pinler (3, 5, 6, 9, 10, 11).",
            ["analogReference"] = "analogRead'in üst gerilimini seçer (DEFAULT, INTERNAL, EXTERNAL).",
            ["analogReadResolution"] = "analogRead'in bit sayısını ayarlar (ESP32: 9–12).",
            ["delay"] = "Programı verilen milisaniye kadar bekletir (bu sırada loop ilerlemez).",
            ["delayMicroseconds"] = "Mikrosaniye cinsinden bekler; kısa darbeler için (≈16 383 µs'ye kadar doğru).",
            ["millis"] = "Kart açılalı geçen süre (ms). unsigned long; Uno'da ~49,7 günde başa döner. Beklemeden zamanlama için.",
            ["micros"] = "Kart açılalı geçen süre (µs); Uno'da 4 µs çözünürlük.",
            ["tone"] = "Pinde verilen frekansta kare dalga üretir (buzzer); süre verilmezse noTone'a kadar sürer.",
            ["noTone"] = "tone() ile başlatılan sesi durdurur.",
            ["pulseIn"] = "Pinde bir darbenin süresini µs olarak ölçer (ör. HC-SR04 eko pini); zaman aşımında 0.",
            ["pulseInLong"] = "pulseIn gibi, uzun darbeler için (millis tabanlı).",
            ["shiftOut"] = "Bir baytı veri ve saat pinleriyle bit bit gönderir (74HC595).",
            ["shiftIn"] = "Veri ve saat pinleriyle bit bit bir bayt okur.",
            ["attachInterrupt"] = "Pin değişince bir fonksiyonu kesmeyle çalıştırır: RISING, FALLING, CHANGE, LOW. Pin: digitalPinToInterrupt(pin).",
            ["detachInterrupt"] = "attachInterrupt ile bağlanan kesmeyi kaldırır.",
            ["digitalPinToInterrupt"] = "Pin numarasını kesme numarasına çevirir (Uno: 2 → 0, 3 → 1).",
            ["interrupts"] = "Kesmeleri yeniden açar.",
            ["noInterrupts"] = "Kesmeleri kapatır (kısa, kritik bölümler için).",
            ["map"] = "Bir değeri bir aralıktan başka bir aralığa doğrusal çevirir: map(x, 0, 1023, 0, 180). Tamsayı bölmesi yapar.",
            ["constrain"] = "Değeri [alt, üst] aralığına sıkıştırır.",
            ["min"] = "İki değerden küçüğü.",
            ["max"] = "İki değerden büyüğü.",
            ["abs"] = "Mutlak değer.",
            ["sq"] = "Karesi.",
            ["sqrt"] = "Karekök.",
            ["pow"] = "Üs alma: pow(taban, üs).",
            ["random"] = "Sözde rastgele sayı: random(üst) 0..üst−1, random(alt, üst) alt..üst−1.",
            ["randomSeed"] = "Rastgele sayı üretecini tohumlar (ör. randomSeed(analogRead(A0))).",
            ["bitRead"] = "Sayının verilen bitini okur (0 ya da 1).",
            ["bitWrite"] = "Sayının verilen bitini yazar.",
            ["bitSet"] = "Sayının verilen bitini 1 yapar.",
            ["bitClear"] = "Sayının verilen bitini 0 yapar.",
            ["highByte"] = "16 bitlik sayının üst baytı.",
            ["lowByte"] = "Sayının alt baytı.",
            ["HIGH"] = "Mantık 1: pin besleme gerilimine çekili.",
            ["LOW"] = "Mantık 0: pin 0 V'ta.",
            ["INPUT"] = "pinMode: yüksek empedanslı giriş.",
            ["OUTPUT"] = "pinMode: çıkış.",
            ["INPUT_PULLUP"] = "pinMode: iç pull-up dirençli giriş (buton GND'ye bağlanır, basılınca LOW).",
            ["LED_BUILTIN"] = "Karttaki LED'in pini (Uno: 13, ESP32 DevKit: 2).",
            ["RISING"] = "Kesme: LOW → HIGH geçişinde.",
            ["FALLING"] = "Kesme: HIGH → LOW geçişinde.",
            ["CHANGE"] = "Kesme: her seviye değişiminde.",
            ["Serial"] = "Kartın USB seri portu (Seri monitör).",
            ["Serial.begin"] = "Seri portu verilen hızda (baud) açar; seri monitördeki hızla aynı olmalı (ör. 9600, 115200).",
            ["Serial.print"] = "Metni ya da sayıyı satır sonu olmadan gönderir. print(x, HEX) / print(f, 2) biçim verir.",
            ["Serial.println"] = "Metni ya da sayıyı gönderip satır sonu ekler.",
            ["Serial.printf"] = "printf biçimli yazdırma (ESP32).",
            ["Serial.write"] = "Ham bayt(lar) gönderir.",
            ["Serial.available"] = "Okunmayı bekleyen gelen bayt sayısı.",
            ["Serial.read"] = "Gelen ilk baytı okur; yoksa −1.",
            ["Serial.peek"] = "Gelen ilk baytı almadan bakar.",
            ["Serial.flush"] = "Giden verinin gönderilmesini bekler.",
            ["Serial.parseInt"] = "Gelen veriden sonraki tamsayıyı okur (zaman aşımına kadar bekler).",
            ["Serial.parseFloat"] = "Gelen veriden sonraki ondalık sayıyı okur.",
            ["Serial.readString"] = "Gelen veriyi zaman aşımına kadar String olarak okur.",
            ["Serial.readStringUntil"] = "Verilen karaktere (ör. '\\n') kadar okur.",
            ["Serial.setTimeout"] = "parseInt / readString'in bekleme süresi (ms, varsayılan 1000).",
            ["Wire"] = "I²C veri yolu (Uno: SDA A4, SCL A5; ESP32: SDA 21, SCL 22).",
            ["Wire.begin"] = "I²C'yi ana cihaz olarak başlatır (adres verilirse köle).",
            ["Wire.beginTransmission"] = "Verilen adresteki cihaza yazma başlatır.",
            ["Wire.write"] = "Gönderilecek bayt(lar)ı sıraya koyar.",
            ["Wire.endTransmission"] = "Sıradakileri gönderir; 0 = başarılı, 2 = adres yanıt vermedi.",
            ["Wire.requestFrom"] = "Cihazdan verilen sayıda bayt ister; gelen bayt sayısını döndürür.",
            ["Wire.read"] = "Gelen sıradaki baytı okur.",
            ["Wire.available"] = "Okunmayı bekleyen bayt sayısı.",
            ["Wire.setClock"] = "I²C saat hızı (100000 standart, 400000 hızlı).",
            ["Servo"] = "Hobi servosu sürer (50 Hz darbe, 544–2400 µs).",
            ["Servo.attach"] = "Servoyu bir pine bağlar; isteğe bağlı en kısa / en uzun darbe (µs).",
            ["Servo.write"] = "Açıyı derece olarak ayarlar (0–180).",
            ["Servo.writeMicroseconds"] = "Darbe genişliğini doğrudan µs olarak ayarlar (1500 ≈ orta).",
            ["Servo.read"] = "Son yazılan açı.",
            ["Servo.attached"] = "Servo bir pine bağlı mı.",
            ["Servo.detach"] = "Darbeleri durdurur; servo serbest kalır.",
            ["EEPROM"] = "Kalıcı bellek: güç kesilince silinmez (Uno 1024 bayt).",
            ["EEPROM.read"] = "Adresteki baytı okur.",
            ["EEPROM.write"] = "Adrese bayt yazar (yaklaşık 100 000 yazma ömrü).",
            ["EEPROM.update"] = "Değer farklıysa yazar: ömrü korur.",
            ["EEPROM.get"] = "Adresten bir değişkeni (int, float, struct) okur.",
            ["EEPROM.put"] = "Bir değişkeni adrese yazar (yalnızca değişen baytları).",
            ["EEPROM.length"] = "EEPROM boyutu (bayt).",
            ["EEPROM.begin"] = "ESP32: EEPROM öykünmesini verilen boyutla başlatır.",
            ["EEPROM.commit"] = "ESP32: yazılanları flaşa kaydeder.",
            ["SPI"] = "SPI veri yolu (Uno: MOSI 11, MISO 12, SCK 13).",
            ["SPI.begin"] = "SPI'yi başlatır.",
            ["SPI.transfer"] = "Bir bayt gönderir, aynı anda gelen baytı döndürür.",
            ["SPI.beginTransaction"] = "Hız, bit sırası ve modla bir aktarım başlatır: SPISettings(hız, MSBFIRST, SPI_MODE0).",
            ["SPI.endTransaction"] = "Aktarımı bitirir.",
            ["ledcSetup"] = "ESP32 PWM kanalını frekans ve çözünürlükle kurar (Arduino-ESP32 2.x).",
            ["ledcAttachPin"] = "Pini bir PWM kanalına bağlar (2.x).",
            ["ledcAttach"] = "Pini frekans ve çözünürlükle PWM'e bağlar (Arduino-ESP32 3.x).",
            ["ledcWrite"] = "PWM doluluk değerini yazar (kanal ya da pin, sürüme göre).",
            ["touchRead"] = "ESP32 dokunmatik pin değerini okur (dokununca düşer).",
            ["WiFi"] = "ESP32 Wi-Fi (simülasyonda taklit).",
            ["WiFi.begin"] = "Ağa bağlanmaya başlar: WiFi.begin(ad, şifre).",
            ["WiFi.status"] = "Bağlantı durumu: WL_CONNECTED bağlı.",
            ["WiFi.localIP"] = "Kartın IP adresi.",
            ["xTaskCreate"] = "FreeRTOS görevi oluşturur: fonksiyon, ad, yığın, parametre, öncelik, tutamaç.",
            ["vTaskDelay"] = "Görevi verilen tik kadar bekletir (pdMS_TO_TICKS(ms)).",
            ["String"] = "Arduino metin sınıfı (birleştirme, length(), indexOf(), toInt() …).",
        };

        public static string? Doc(string name, string? owner = null)
        {
            if (owner != null)
            {
                foreach (var key in new[] { owner + "." + name, OwnerAlias(owner) + "." + name })
                    if (Docs.TryGetValue(key, out var d)) return d;
            }

            return Docs.TryGetValue(name, out var doc) ? doc : null;
        }

        /// <summary>Classes documented under their usual object name ("HardwareSerial" → "Serial").</summary>
        private static string OwnerAlias(string owner) => owner switch
        {
            "HardwareSerial" or "Stream" or "Print" or "HWCDC" or "USBCDC" => "Serial",
            "TwoWire" => "Wire",
            "EEPROMClass" => "EEPROM",
            "SPIClass" => "SPI",
            "WiFiClass" or "WiFiSTAClass" => "WiFi",
            _ => owner
        };

        public static readonly string[] Keywords =
        {
            "if", "else", "for", "while", "do", "switch", "case", "default", "break", "continue", "return", "goto", "sizeof",
            "const", "static", "volatile", "unsigned", "signed", "struct", "class", "enum", "typedef", "public", "private", "protected",
            "true", "false", "nullptr", "NULL", "new", "delete", "this", "auto", "inline", "extern", "template", "typename", "namespace", "using",
            "PROGMEM", "F"
        };

        public static readonly string[] Types =
        {
            "void", "bool", "boolean", "byte", "char", "int", "long", "short", "float", "double", "word", "size_t", "String",
            "uint8_t", "int8_t", "uint16_t", "int16_t", "uint32_t", "int32_t", "uint64_t", "int64_t", "unsigned long", "unsigned int"
        };

        /// <summary>Snippets: label, text ('$' marks where the caret goes), description.</summary>
        public static readonly (string Label, string Text, string Doc)[] Snippets =
        {
            ("for", "for (int i = 0; i < $; i++) {\n  \n}", "Sayaçlı döngü."),
            ("if", "if ($) {\n  \n}", "Koşul."),
            ("ifelse", "if ($) {\n  \n} else {\n  \n}", "Koşul ve aksi."),
            ("while", "while ($) {\n  \n}", "Koşul sürdükçe döngü."),
            ("switch", "switch ($) {\n  case 0:\n    break;\n  default:\n    break;\n}", "Değere göre dallanma."),
            ("millistimer", "static unsigned long last = 0;\nif (millis() - last >= $) {\n  last = millis();\n  \n}", "delay() kullanmadan, beklemeden belirli aralıklarla iş yapmak (taşmaya dayanıklı)."),
            ("setuploop", "void setup() {\n  $\n}\n\nvoid loop() {\n  \n}", "Boş çizim iskeleti.")
        };
    }
}
