using System;
using System.Collections.Generic;
using System.Linq;
using RobotWin.Studio.Examples;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio
{
    /// <summary>
    /// [learning] The learning path's five levels (docs/kullanim/14-ogrenme-yolu.md) as in-app guides, on the same card as
    /// "İlk robotun": each opens its example, notices what it can (the example open, the simulation running, the page
    /// shown) and moves on by itself; a step it cannot see (reading a value, an edit of one's own) moves on with
    /// "Yaptım, sonraki". The last card is the level's own task and leads to the next level. The example that starts a
    /// level offers "Rehberi başlat" on its info screen; Yardım and the command palette list all five.
    /// </summary>
    public partial class MainWindow
    {
        /// <summary>The example that starts each level (its info screen offers the guide).</summary>
        internal static readonly IReadOnlyDictionary<string, int> LearningStarts = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["led-button"] = 1, ["arduino-blink"] = 2, ["motor-driver"] = 3, ["obstacle-avoider"] = 4
        };

        internal static readonly string[] LearningLevelNames = { "Elektronik temelleri", "Gömülü yazılım", "Mekanik ve PCB", "Robotik", "Endüstri" };

        /// <summary>Starts a level of the learning path (1–5).</summary>
        public void StartLearningGuide(int level)
        {
            level = Math.Max(1, Math.Min(5, level));
            StartGuide($"ÖĞRENME YOLU · DÜZEY {level}", LearningSteps(level));
        }

        /// <summary>
        /// --snapshot --fault learning-e2e: every level of the path walked through the real app — each step the guide can
        /// see done gets its "do it for me" and must notice it is done (the example or template opens, the simulation runs,
        /// the page shows); a step it cannot see is listed as such. Results go to a log.
        /// </summary>
        internal async void RunLearningCheck(string logPath)
        {
            var log = new System.Text.StringBuilder();
            int failures = 0;
            try
            {
                for (int level = 1; level <= LearningLevelNames.Length; level++)
                {
                    StartLearningGuide(level);
                    _guideTimer?.Stop();   // driven step by step here
                    log.AppendLine($"Düzey {level}: {LearningLevelNames[level - 1]}");
                    for (int i = 0; i < _guide.Count - 1; i++)
                    {
                        var step = _guide[_guideIndex];
                        if (step.Manual)
                        {
                            log.AppendLine($"  --   {i + 1}. {step.Title} (elle)");
                            NextGuideStep();
                            continue;
                        }

                        step.Do?.Invoke();
                        var start = DateTime.UtcNow;
                        while (!step.Done() && (DateTime.UtcNow - start).TotalSeconds < 45) await System.Threading.Tasks.Task.Delay(250);
                        bool ok = step.Done();
                        if (!ok) failures++;
                        log.AppendLine($"  {(ok ? "OK  " : "FAIL")} {i + 1}. {step.Title} ({(DateTime.UtcNow - start).TotalSeconds:F1} s)" + (ok ? string.Empty : " — " + W.Status));
                        if (!ok)   // what is open, to see why it was not noticed
                            foreach (var d in W.Documents)
                                log.AppendLine($"       açık: {d.Kind} '{d.Title}' şema '{d.Package.Schematic.Title}'" + (d.World is { } dw ? $" dünya '{dw.Title}' montaj={dw.IsAssembly} tasarımlar: {string.Join(", ", dw.Designs.Select(x => x.Package.Schematic.Title))}" : string.Empty));
                        if (W.Simulation.IsActive) W.Simulation.Stop();
                        if (_world.SimulationState.State != SimState.Stopped) _world.StopForCheck();
                        NextGuideStep();
                    }
                }
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("FAIL beklenmeyen hata: " + ex);
            }

            log.AppendLine(failures == 0 ? "TÜMÜ GEÇTİ" : failures + " HATA");
            System.IO.File.WriteAllText(logPath, log.ToString());
        }

        /// <summary>An example opened by the guide, as the start page opens it (its page, or its Montaj).</summary>
        private void OpenExampleForGuide(string id)
        {
            if (ExampleProjects.All.FirstOrDefault(e => e.Id == id) is not { } ex) return;
            W.Load(ex.Build(), null);
            if (ex.Page == "assembly") OpenProjectAssembly(ask: false);
            else Navigate(ex.Page);
        }

        /// <summary>The example is open: as a project tab, or as the project a Montaj is put together from.</summary>
        private bool ExampleOpen(string id) =>
            ExampleTitle(id) is { } title &&
            (W.Documents.Any(d => d.Kind == DocumentKind.Design && d.Package.Schematic.Title == title) ||
             W.Documents.Any(d => d.World is { IsAssembly: true } w && w.Designs.Any(x => x.Package.Schematic.Title == title)));

        /// <summary>The title an example's project is built with (built once: it need not be the card's title).</summary>
        private string? ExampleTitle(string id)
        {
            if (_exampleTitles.TryGetValue(id, out var title)) return title;
            if (ExampleProjects.All.FirstOrDefault(e => e.Id == id) is not { } ex) return null;
            return _exampleTitles[id] = ex.Build().Title;
        }

        private readonly Dictionary<string, string> _exampleTitles = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>The title each template's world is built with (built once: a world's title is not the template's name).</summary>
        private readonly Dictionary<string, string> _templateTitles = new Dictionary<string, string>(StringComparer.Ordinal);

        private string? TemplateWorldTitle(string id)
        {
            if (_templateTitles.TryGetValue(id, out var title)) return title;
            if (WorldTemplates.Find(id) is not { } t) return null;
            return _templateTitles[id] = t.Build().Title;
        }

        private void OpenTemplateForGuide(string id)
        {
            if (WorldTemplates.Find(id) is not { } t) return;
            var world = t.Build();
            _templateTitles[id] = world.Title;
            NewWorld(world);
        }

        private bool TemplateOpen(string id) => W.ActiveWorld is { } w && TemplateWorldTitle(id) is { } title && w.Title == title;

        private bool CircuitRunning(double seconds) => W.Simulation.IsActive && W.Simulation.Time >= seconds;

        private bool WorldRunning(double seconds) => W.ActiveWorld != null && _world.SimulationState.State == SimState.Running && _world.SimulationState.Time >= seconds;

        private GuideStep GuideOpen(string id, string title, string text) => new GuideStep
        {
            Title = title, Text = text, DoText = "Örneği aç", Do = () => OpenExampleForGuide(id), Done = () => ExampleOpen(id)
        };

        private GuideStep GuideTemplate(string id, string title, string text) => new GuideStep
        {
            Title = title, Text = text, DoText = "Şablonu aç", Do = () => OpenTemplateForGuide(id), Done = () => TemplateOpen(id)
        };

        private static GuideStep GuideManual(string title, string text) => new GuideStep { Title = title, Text = text, Manual = true };

        private GuideStep GuidePage(string page, string title, string text) => new GuideStep
        {
            Title = title, Text = text, DoText = "Geç", Do = () => Navigate(page), Done = () => _current == page, Target = () => Rail.StageElement(page)
        };

        /// <summary>The level's own task, and the way on to the next level.</summary>
        private GuideStep LevelTask(int level, string text) => new GuideStep
        {
            Title = "Kendi göreviniz", Text = text,
            DoText = level < 5 ? $"Düzey {level + 1}'e geç" : "Yolu bitir",
            Do = () =>
            {
                if (level < 5) StartLearningGuide(level + 1);
                else CloseGuide();
            }
        };

        private List<GuideStep> LearningSteps(int level) => level switch
        {
            1 => new List<GuideStep>
            {
                GuideOpen("led-button", "LED ve Buton örneğini açın", "9 V pil, 470 Ω direnç, buton ve kırmızı LED: akımı sınırlayan direncin işini göreceksiniz."),
                new GuideStep
                {
                    Title = "Çalıştırın ve butona basın", Text = "F5 ile simülasyonu başlatın, butona tıklayıp basılı tutun: LED yanar.",
                    DoText = "Çalıştır", Do = () => { Navigate("schematic"); W.Simulation.Start(); }, Done = () => CircuitRunning(0.3)
                },
                GuideManual("LED'in akımına bakın", "LED'e tıklayın: denetçinin üstündeki Canlı değerler akımı ve gerilimi gösterir. Beklenen: butona basılıyken yaklaşık 15 mA ve LED'in üstünde yaklaşık 2 V."),
                GuideManual("Direnci silin", "Direnci seçip Delete ile silin ve yeniden başlatın: LED'in akımı sınırını aşar ve LED yanar; nedeni Olaylar sekmesinde yazar. Sonra Ctrl+Z ile geri alın."),
                GuideOpen("rc-filter", "RC filtrede zaman sabiti", "1 kΩ ve 1 µF: RC = 1 ms. Osiloskobu çıkışa bağlayıp çalıştırın. Beklenen: kare dalganın kenarından 1 ms sonra çıkış son değerin yaklaşık %63'ünde; köşe frekansı yaklaşık 159 Hz."),
                GuideManual("İki koşuyu karşılaştırın", "Osiloskopta REF ile dalgayı saklayın, kondansatörü 2 µF yapıp yeniden çalıştırın: iki dalga üst üste görünür, durum satırı sapmayı yazar."),
                LevelTask(1, "5 V'tan 3,3 V'luk bir gerilim bölücü kurun ve 10 kΩ'luk bir yük altında çıkışın ne kadar düştüğünü multimetreyle ölçün.")
            },
            2 => new List<GuideStep>
            {
                GuideOpen("arduino-blink", "Arduino: Gerçek Firmware örneğini açın", "Uno, derlenmiş gerçek Blink + Serial yazılımını çalıştırır: D13'teki LED yanıp söner."),
                GuidePage("code", "Kod sayfasına geçin", "Soldaki Kod'a geçin: kartın Arduino kodu açılır."),
                GuideManual("Gecikmeyi değiştirin", "delay değerini değiştirin, F7 ile derleyin ve F5 ile çalıştırın: LED'in yanıp sönme hızı değişir."),
                GuideManual("Pinin nereye bağlı olduğunu görün", "İmleci koddaki bir pin numarasının üstüne getirin: alttaki satır o pinin kartta neye bağlı olduğunu yazar (\"D13 → R1\" gibi), parça devrede de seçilir."),
                GuideManual("Kesme noktası koyun", "Bir satırın numarasına tıklayıp kesme noktası koyun, çalıştırın; Değişkenler ve Yazmaçlar sekmelerinde değerleri izleyin, adım adım ilerleyin."),
                GuideOpen("esp32-dimmer", "ESP32'de PWM", "Potansiyometreyi çevirin: LED'in parlaklığı PWM görev oranıyla değişir. Beklenen: 5 kHz PWM; görev oranı potansiyometrenin konumuyla doğrusal."),
                LevelTask(2, "Butona basıldıkça bir sayacı artırıp LCD'ye yazan bir çizim yazın; butonun sıçramasını yazılımla giderin ve osiloskopta sıçramayı görün.")
            },
            3 => new List<GuideStep>
            {
                GuideOpen("motor-driver", "MOSFET ile Motor Sürme örneğini açın", "490 Hz PWM, lojik seviye MOSFET, serbest dolaşım diyodu ve bir dişli motor."),
                GuidePage("pcb", "PCB sayfasına geçin", "Soldaki PCB'ye geçin: devrenin kartı burada."),
                GuideManual("Yerleştirin, yolları çizin, DRC", "Otomatik yerleşim ve otomatik rota düğmeleriyle kartı hazırlayın, DRC'yi çalıştırın. Bir hatayı Sorunlar panelindeki anahtar düğmesiyle düzeltin ya da istisna yapın."),
                GuideManual("Ortak seçim", "Devrede bir parça seçin ve PCB'ye dönün: aynı parça seçilidir; simülasyon çalışırken panelinde canlı değerleri yazar."),
                GuideManual("Üretim dosyaları", "Üretime hazır mı özetine bakın, Gerber ve delik dosyalarını dışa aktarın."),
                GuidePage("model", "Parça sayfasında bir tutucu", "Parça sayfasında bir motor tutucu çizin (eskiz, ölçü, ekstrüzyon); kütlesine ve ağırlık merkezine bakın."),
                LevelTask(3, "50 × 30 mm'lik bir kart tasarlayın, köşelerine M3 delikleri koyun, DRC'yi temiz geçirin ve kutusunu Parça sayfasında modelleyin.")
            },
            4 => new List<GuideStep>
            {
                GuideOpen("obstacle-avoider", "Engelden Kaçan robotu açın", "Robot Montaj'da kurulu açılır: Uno, HC-SR04 ve iki motorlu bir araç."),
                new GuideStep
                {
                    Title = "Kendi koduyla sürün", Text = "▶ Dünyayı çalıştır'a basın: robot kendi Arduino koduyla engellerden kaçar.",
                    DoText = "Çalıştır", Do = RunWorld, Done = () => WorldRunning(1.0), Target = () => _world.RunButtonElement
                },
                GuideManual("Teknik vurgu", "Bir motoru seçin: kabloları, bağlantıları, ağırlık merkezi ve çalışırken torku üstünde görünür."),
                GuideTemplate("servo-arm", "Ters kinematik", "Kol 2'yi seçin, Kinematik bölümünde bir hedef verin ve Çöz'e basın; ulaşamadığı bir hedefte nedenini okuyun (eksen sınırı, uzaklık ya da çarpışma)."),
                GuideTemplate("sarkac", "Sarkacın periyodu", "Çalıştırıp periyodu ölçün. Beklenen: bileşik sarkaç için T = 2π√(I/mgh), 30°'den bırakılınca yaklaşık 1,11 s."),
                LevelTask(4, "Engelden Kaçan robotun kodundaki engel mesafesini ve dönüş süresini değiştirip parkuru en az çarpışmayla tamamlatın; Deneyler ile bu iki değeri tarayıp en iyisini seçin.")
            },
            _ => new List<GuideStep>
            {
                GuideTemplate("factory-cell", "Fabrika hücresini açın", "Bir robot kol ve bir konveyör: parçalar istasyondan istasyona akar."),
                new GuideStep
                {
                    Title = "Hücreyi çalıştırın", Text = "▶ Dünyayı çalıştır'a basın; istasyonun çevrim süresine ve darboğaza bakın.",
                    DoText = "Çalıştır", Do = RunWorld, Done = () => WorldRunning(2.0), Target = () => _world.RunButtonElement
                },
                GuideManual("PLC ekleyin", "Parçalar → Robot Hücresi → PLC ile bir PLC ekleyin, girişlerine sensörleri, çıkışlarına konveyörü bağlayın ve Yapısal Metin ile yazın; merdivende canlı değerleri izleyin."),
                GuideTemplate("scara-cell", "SCARA al-bırak", "Tutucunun zamanlamasını değiştirin ve kaçırılan parçaları sayın."),
                GuideManual("Dışarıdan izleyin", "Hücreyi Modbus TCP ya da OPC UA ile dışarıdan izleyin; bir arıza senaryosu (sensör takılması, valf gecikmesi) ekleyip hücrenin nasıl davrandığını görün."),
                LevelTask(5, "Konveyöre ikinci bir istasyon ekleyin, çevrim süresini %20 kısaltacak sıralamayı bulun ve sonucu bir deney raporuyla gösterin.")
            }
        };
    }
}
