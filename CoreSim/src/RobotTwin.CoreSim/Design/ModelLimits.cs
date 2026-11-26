using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [limits] What each part's simulation model leaves out or simplifies, said plainly next to what it does: the
    /// library's specification table and the part's inspector show them, so a result is read for what the model is.
    /// </summary>
    public static class ModelLimits
    {
        private static readonly string[] Junction = { "Ters toparlanma süresi ve yüksek frekansta paket endüktansı yok." };

        private static readonly Dictionary<string, string[]> ByPart = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["resistor"] = new[] { "Isıl gürültü ile bacakların endüktansı ve kapasitesi yok." },
            ["potentiometer"] = new[] { "Silecek temas gürültüsü ve aşınma yok." },
            ["capacitor"] = new[] { "Seramikte gerilimle kapasite düşmesi (DC bias) ve dielektrik soğurma yok." },
            ["electrolytic"] = new[] { "Yaşlanma ve sıcaklıkla ESR değişimi yok." },
            ["inductor"] = new[] { "Doyma yalnızca bir sınır denetimidir: endüktans akımla azalmaz; çekirdek kaybı ve histerezis yok." },
            ["diode"] = Junction,
            ["schottky"] = Junction,
            ["zener"] = Junction,
            ["led"] = new[] { "Işık çıkışı akımla doğrusal sayılır; ısınmayla parlaklık düşmesi yok." },
            ["npn"] = new[] { "Ebers–Moll tabanlı: yüksek akımda beta düşmesi ve taban direnci yok." },
            ["pnp"] = new[] { "Ebers–Moll tabanlı: yüksek akımda beta düşmesi ve taban direnci yok." },
            ["darlington"] = new[] { "Ebers–Moll tabanlı: yüksek akımda beta düşmesi ve taban direnci yok." },
            ["nmos"] = new[] { "Seviye 1 (Shichman–Hodges): kısa kanal etkileri, eşik altı iletim ve eşiğin sıcaklıkla kayması yok." },
            ["pmos"] = new[] { "Seviye 1 (Shichman–Hodges): kısa kanal etkileri, eşik altı iletim ve eşiğin sıcaklıkla kayması yok." },
            ["opamp"] = new[] { "Makro model: giriş kutuplama akımı, gürültü ve ikinci kutup yok." },
            ["regulator"] = new[] { "Doğrusal regülatörün geçici tepkisi ve besleme bastırması (PSRR) frekansla değişmez." },
            ["buck-module"] = new[]
            {
                "Anahtarlama periyodu üzerinden ortalamalı model: çıkış dalgalanması (ripple) ve EMI yok.",
                "Döngü çipin kendi kompanzasyonu değil, genel bir PI; kesintili iletim (DCM) yalnızca akımın sıfırda durmasıyla gösterilir."
            },
            ["boost-module"] = new[]
            {
                "Anahtarlama periyodu üzerinden ortalamalı model: çıkış dalgalanması (ripple) ve EMI yok.",
                "Döngü çipin kendi kompanzasyonu değil, genel bir PI; kesintili iletim (DCM) yalnızca akımın sıfırda durmasıyla gösterilir."
            },
            ["battery"] = new[] { "Yaşlanma, sıcaklığın kapasiteye etkisi ve gevşeme (polarizasyon) zaman sabitleri yok." },
            ["dc-motor"] = new[] { "Fırça ve komütasyon dalgalanması, mıknatısın ısınması ve sargının doyması yok." },
            ["servo"] = new[] { "İç denetleyici ve dişli boşluğu sadeleştirilmiş; potansiyometre gürültüsü yok." },
            ["stepper"] = new[] { "Mıknatısın sıcaklıkla zayıflaması ve sargıların doyması yok; rezonans yalnızca mekanikten gelir." },
            ["a4988"] = new[] { "Kıyıcı (chopper) PWM'i ortalamalı: faz akımının dalgalanması ve azalma kiplerinin (yavaş/karışık) ayrıntısı yok." },
            ["drv8825-stepper"] = new[] { "Kıyıcı (chopper) PWM'i ortalamalı: faz akımının dalgalanması ve azalma kiplerinin (yavaş/karışık) ayrıntısı yok." },
            ["tmc2209"] = new[]
            {
                "Kıyıcı (chopper) PWM'i ortalamalı: faz akımının dalgalanması ve azalma kiplerinin ayrıntısı yok.",
                "Yalnızca bağımsız kip: UART ayarları, StallGuard, CoolStep ve StealthChop'un sessizliği yok."
            },
            ["button"] = new[] { "Kontak sıçraması (bounce) yok: basış temiz bir geçiştir." },
            ["switch"] = new[] { "Kontak sıçraması (bounce) yok: geçiş temizdir." },
            ["relay"] = new[] { "Kontak sıçraması (bounce) ve kontak aşınması yok." },
            ["limit-switch"] = new[] { "Kontak sıçraması (bounce) yok: geçiş temizdir." },
            ["lamp"] = new[] { "Filamanın ısınması tek bir zaman sabitiyle; ışık rengi sıcaklıkla değişmez." },
            ["hc-sr04"] = new[] { "Ölçüm tek bir ışınla yapılır: ses konisinin genişliği ve yüzeyin açısına göre yankı kaybı sadeleştirilmiş." },
            ["arduino-uno"] = new[] { "ATmega328P komut düzeyinde çalışır; bazı çevre birimlerinin analog ayrıntıları (ADC örnekleme kapasitesi, saat sapması) sadeleştirilmiş." },
            ["arduino-nano"] = new[] { "ATmega328P komut düzeyinde çalışır; bazı çevre birimlerinin analog ayrıntıları (ADC örnekleme kapasitesi, saat sapması) sadeleştirilmiş." },
            ["esp32-devkit-v1"] = new[] { "Hızlı kipte Arduino API'si modellenir; donanım kipinde çekirdek öykünülür. Wi-Fi ve BLE radyosu iki kipte de bir modeldir." },
            ["esp32-devkitc"] = new[] { "Hızlı kipte Arduino API'si modellenir; donanım kipinde çekirdek öykünülür. Wi-Fi ve BLE radyosu iki kipte de bir modeldir." },
            ["fuse"] = new[] { "I²t ısınma modeli: üreticinin erime eğrisinin biçimi ve ark süresi yok." },
        };

        /// <summary>What the model of a part leaves out or simplifies; empty when nothing is noted.</summary>
        public static IReadOnlyList<string> Of(string? partId) =>
            partId != null && ByPart.TryGetValue(partId, out var notes) ? notes : Array.Empty<string>();
    }
}
