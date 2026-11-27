using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RobotTwin.CoreSim.Sandbox;
using RobotWin.Studio.Shell;
using RobotWin.Studio.Simulation;

namespace RobotWin.Studio
{
    /// <summary>
    /// [guide] "İlk robotun": an interactive first-run guide that gets a new user from nothing to a working, measured and
    /// saved robot in 5–10 minutes — the ready servo arm, its Arduino code changed, the motors seen moving, the current
    /// measured, the result saved. A card in the corner says what to do and outlines the control to press; each step
    /// notices by itself when it is done (polled twice a second) and moves on; the card's button does it for you.
    /// Offered once on the first start; Başlangıç and Yardım open it again.
    /// </summary>
    public partial class MainWindow
    {
        private sealed class GuideStep
        {
            public string Title = string.Empty;
            public string Text = string.Empty;
            public string? DoText;
            public Action? Do;
            public Func<bool> Done = () => false;
            public Func<FrameworkElement?>? Target;

            /// <summary>Runs when the step is shown (puts the user where the step starts).</summary>
            public Action? Enter;

            /// <summary>[learning] A step the guide cannot see done (reading a value, an edit of one's own): "Yaptım, sonraki".</summary>
            public bool Manual;
        }

        /// <summary>[learning] The card's heading: the guide's name ("İLK ROBOTUN", "ÖĞRENME YOLU · DÜZEY 2").</summary>
        private string _guideName = "İLK ROBOTUN";

        private Border? _guideCard;
        private TextBlock _guideStepText = null!, _guideTitle = null!, _guideBody = null!;
        private Button _guideDo = null!, _guideSkip = null!;
        private StackPanel _guideDots = null!;
        private List<GuideStep> _guide = new List<GuideStep>();
        private int _guideIndex;
        private DispatcherTimer? _guideTimer;
        private GuideHighlight? _guideMark;
        private FrameworkElement? _guideMarked;
        private WorldDocument? _guideWorld;
        private bool _guideUploaded;
        private DateTime _guideStepShown;

        /// <summary>First start: offer the guide once (never during snapshots or automation).</summary>
        private void OfferGuideOnFirstStart()
        {
            if (Unattended.Active || AppSettings.Current.FirstRunGuideOffered) return;
            AppSettings.Current.FirstRunGuideOffered = true;
            AppSettings.Current.Save();
            int pick = Dialog.Show(this, "RobotWin Studio'ya hoş geldiniz",
                "5–10 dakikalık etkileşimli bir rehberle ilk robotunuzu çalıştıralım mı?\n\n" +
                "Hazır bir servo kolu açacak, Arduino kodunu değiştirip motorların hareketini görecek, akımı ölçecek ve sonucu kaydedeceksiniz. " +
                "Rehber sağ altta durur; her adımda neye basacağınızı gösterir.",
                "Rehberi başlat", "Daha sonra");
            if (pick == 0) StartGuide();
            else W.Status = "Rehbere istediğiniz zaman Başlangıç sayfasından ya da Yardım menüsünden ulaşabilirsiniz.";
        }

        /// <summary>Starts (or restarts) the first-robot guide.</summary>
        public void StartGuide() => StartGuide("İLK ROBOTUN", BuildGuide());

        /// <summary>[learning] Starts a guide on the card: the first robot's, or a level of the learning path.</summary>
        private void StartGuide(string name, List<GuideStep> steps)
        {
            _guideName = name;
            _guideWorld = null;
            _guideUploaded = false;
            _code.Uploaded -= OnGuideUpload;
            _code.Uploaded += OnGuideUpload;
            _guide = steps;
            _guideIndex = 0;
            ShowGuideCard();
            ShowGuideStep();
            _guideTimer ??= new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
            _guideTimer.Tick -= OnGuideTick;
            _guideTimer.Tick += OnGuideTick;
            _guideTimer.Start();
        }

        private void OnGuideUpload(Pages.CodeTarget target)
        {
            if (GuideUno() is { } uno && target.Key == "w:" + uno.Id) _guideUploaded = true;
        }

        /// <summary>The Arduino Uno of the guide's servo arm.</summary>
        private WorldObject? GuideUno() => _guideWorld?.Objects.FirstOrDefault(o => o.PartId == "arduino-uno");

        private bool GuideWorldRunning(double seconds) =>
            _guideWorld != null && W.ActiveWorld == _guideWorld && _world.SimulationState.State == SimState.Running && _world.SimulationState.Time >= seconds;

        private static readonly Regex StepValue = new Regex(@"\bstep\s*=\s*(\d+)", RegexOptions.Compiled);

        private List<GuideStep> BuildGuide() => new List<GuideStep>
        {
            new GuideStep
            {
                Title = "Hazır servo kolunu açın",
                Text = "Arduino Uno'nun iki SG90 servoyu sürdüğü iki eklemli bir kol. Uno kendi yazılımıyla çalışır, servolar gerçek fizikle döner.",
                DoText = "Servo kolu aç",
                Do = () =>
                {
                    if (Examples.WorldTemplates.Find("servo-arm") is { } t)
                    {
                        var world = t.Build();
                        NewWorld(world);
                        _guideWorld = world;
                    }
                },
                Done = () => _guideWorld != null && W.Documents.Any(d => d.World == _guideWorld)
            },
            new GuideStep
            {
                Title = "Çalıştırın ve izleyin",
                Text = "Üstteki ▶ Dünyayı çalıştır'a basın (F5). Arduino kodu çalışır, omuz 0–180° tarar, dirsek onu izler.",
                DoText = "Çalıştır",
                Do = () =>
                {
                    if (_guideWorld != null && W.Documents.FirstOrDefault(d => d.World == _guideWorld) is { } doc) W.Activate(doc);
                    RunWorld();
                },
                Done = () => GuideWorldRunning(2.0),
                Target = () => _world.RunButtonElement
            },
            new GuideStep
            {
                Title = "Kodu açın",
                Text = "Soldaki Kod'a geçin: kolun Arduino kodu açılır. Kod, iki servoya 50 Hz'lik darbeler gönderen kısa bir çizim.",
                DoText = "Kod'a geç",
                Do = () =>
                {
                    if (GuideUno() is { } uno)
                    {
                        Navigate("code");
                        _code.ShowBoard("w:" + uno.Id);
                    }
                },
                Done = () => _current == "code" && GuideUno() is { } uno && _code.CurrentTarget?.Key == "w:" + uno.Id,
                Target = () => Rail.StageElement("code")
            },
            new GuideStep
            {
                Title = "Kodu değiştirin",
                Text = "23. satırdaki “int angle = 0, step = 2;” içinde 2 seçili: 6 yazın. Kol her adımda 6° döner, üç kat hızlı tarar.",
                DoText = "Benim yerime değiştir",
                Enter = () => _code.SelectInEditor(StepValue, 1),
                Do = () => _code.ReplaceInEditor(StepValue, "step = 6"),
                Done = () => _current == "code" && StepValue.Match(_code.EditorText) is { Success: true } m && m.Groups[1].Value != "2"
            },
            new GuideStep
            {
                Title = "Karta yükleyin",
                Text = "Karta yükle ve çalıştır'a basın (F5). Kod derlenir, Uno'ya yüklenir ve dünya baştan çalışır: kol artık hızlı tarıyor.",
                DoText = "Yükle ve çalıştır",
                Do = () => _code.Upload(),
                Done = () => _guideUploaded && GuideWorldRunning(1.0),
                Target = () => _code.UploadButtonElement
            },
            new GuideStep
            {
                Title = "Akımı ölçün",
                Text = "Dünyada alttaki 'Kablolar ve ölçümler' sekmesini açın. Her kablonun akımı canlı görünür; servoların kırmızı 5V kabloları kol hareket ederken birkaç yüz mA çeker.",
                DoText = "Sekmeyi aç",
                Do = () =>
                {
                    if (_current != "world") Navigate("world");
                    _world.ShowCables();
                },
                Done = () => _current == "world" && _world.CablesShowing && GuideWorldRunning(0.5),
                Target = () => _world.CablesChipElement
            },
            new GuideStep
            {
                Title = "Kaydedin",
                Text = "Ctrl+S ile kaydedin: dünya, değiştirdiğiniz kodla birlikte bir .rwworld dosyasında saklanır.",
                DoText = "Kaydet…",
                Do = () =>
                {
                    if (_guideWorld != null && W.Documents.FirstOrDefault(d => d.World == _guideWorld) is { } doc) W.Activate(doc);
                    OnSave(this, new RoutedEventArgs());
                },
                Done = () => _guideWorld != null && W.Documents.FirstOrDefault(d => d.World == _guideWorld) is { FilePath: not null, IsDirty: false },
                Target = () => SaveButton
            },
            new GuideStep
            {
                Title = "Tebrikler, ilk robotunuz çalıştı!",
                Text = "Hazır bir kolu açtınız, Arduino kodunu değiştirip karta yüklediniz, motorların akımını ölçtünüz ve kaydettiniz. " +
                       "Kol çalıştıkça taban yavaşça kayar: masaya vidalı değil, hızlı kolun tepkisi onu yürütür (gerçekte de öyle). Tabanı seçip Denetçi'de Sabit'i açarsanız yerinde durur. " +
                       "Sırada: kendi devrenizi Devre'de çizin, gövdesini Parça'da modelleyin, hepsini Montaj'da birleştirin.",
                DoText = "Bitir",
                Do = CloseGuide
            }
        };

        private void ShowGuideCard()
        {
            if (_guideCard != null)
            {
                _guideCard.Visibility = Visibility.Visible;
                return;
            }

            var close = new Button { Content = "✕", Style = (Style)FindResource("Btn.Icon"), Width = 26, Height = 26, ToolTip = "Rehberi kapat", HorizontalAlignment = HorizontalAlignment.Right };
            close.Click += (_, _) => CloseGuide();
            _guideStepText = new TextBlock { Style = (Style)FindResource("Text.Overline"), VerticalAlignment = VerticalAlignment.Center };
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(close, Dock.Right);
            head.Children.Add(close);
            head.Children.Add(_guideStepText);
            _guideTitle = new TextBlock { FontSize = Shell.TypeScale.Subtitle, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            _guideTitle.SetResourceReference(TextBlock.ForegroundProperty, "B.Text");
            _guideBody = new TextBlock { TextWrapping = TextWrapping.Wrap, Style = (Style)FindResource("Text.Body"), Margin = new Thickness(0, 0, 0, 12) };
            _guideDo = new Button { Style = (Style)FindResource("Btn.Primary"), Padding = new Thickness(12, 5, 12, 5), HorizontalAlignment = HorizontalAlignment.Left };
            _guideDo.Click += (_, _) =>
            {
                if (_guideIndex < _guide.Count) _guide[_guideIndex].Do?.Invoke();
            };
            _guideSkip = new Button { Content = "Bu adımı atla", Style = (Style)FindResource("Btn.Ghost"), Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 4, 8, 4) };
            _guideSkip.Click += (_, _) => NextGuideStep();
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(_guideDo);
            buttons.Children.Add(_guideSkip);
            _guideDots = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var body = new StackPanel();
            body.Children.Add(head);
            body.Children.Add(_guideTitle);
            body.Children.Add(_guideBody);
            body.Children.Add(buttons);
            body.Children.Add(_guideDots);
            _guideCard = new Border
            {
                Child = body,
                Width = 380,
                Padding = new Thickness(16, 12, 16, 14),
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 18, 18)
            };
            _guideCard.SetResourceReference(Border.BackgroundProperty, "B.Raised");
            _guideCard.SetResourceReference(Border.BorderBrushProperty, "B.Accent");   // flat: an accent edge, no shadow
            Panel.SetZIndex(_guideCard, 80);
            Grid.SetRow(_guideCard, 1);
            Root.Children.Add(_guideCard);
        }

        private void ShowGuideStep()
        {
            if (_guideCard == null || _guideIndex >= _guide.Count) return;
            var step = _guide[_guideIndex];
            int last = _guide.Count - 1;
            _guideStepText.Text = Lang.T(_guideIndex < last ? $"{_guideName} · ADIM {_guideIndex + 1}/{last}" : _guideName);
            _guideTitle.Text = Lang.T(step.Title);
            _guideBody.Text = Lang.T(step.Text);
            _guideDo.Visibility = step.Do != null ? Visibility.Visible : Visibility.Collapsed;
            _guideDo.Content = Lang.T(step.DoText ?? "Benim yerime yap");
            _guideSkip.Visibility = _guideIndex < last ? Visibility.Visible : Visibility.Collapsed;   // the last card only closes
            _guideSkip.Content = Lang.T(step.Manual ? "Yaptım, sonraki" : "Bu adımı atla");   // [learning] a step it cannot see
            _guideDots.Children.Clear();
            for (int i = 0; i < last; i++)
            {
                var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 6, 0) };
                dot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, i < _guideIndex ? "B.Success" : i == _guideIndex ? "B.Accent" : "B.Border");
                _guideDots.Children.Add(dot);
            }

            _guideStepShown = DateTime.UtcNow;
            try
            {
                step.Enter?.Invoke();
            }
            catch (InvalidOperationException)
            {
            }

            MarkGuideTarget(step);
        }

        private void MarkGuideTarget(GuideStep step)
        {
            var target = step.Target?.Invoke();
            if (target == _guideMarked && _guideMark != null) return;
            _guideMark?.Remove();
            _guideMark = null;
            _guideMarked = target;
            if (target != null) _guideMark = GuideHighlight.Show(target);
        }

        private void OnGuideTick(object? sender, EventArgs e)
        {
            if (_guideCard == null || _guideCard.Visibility != Visibility.Visible || _guideIndex >= _guide.Count) return;
            var step = _guide[_guideIndex];
            bool done;
            try
            {
                done = step.Done();
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NullReferenceException)
            {
                done = false;
            }

            if (done && (DateTime.UtcNow - _guideStepShown).TotalSeconds > 0.6) NextGuideStep();
            else MarkGuideTarget(step);   // the control may have appeared (another page) since the step began
        }

        private void NextGuideStep()
        {
            if (_guideIndex >= _guide.Count - 1)
            {
                CloseGuide();
                return;
            }

            _guideIndex++;
            ShowGuideStep();
        }

        /// <summary>
        /// --snapshot --fault guide-e2e: walks the guide through the real app — each step's "do it for me", then waits
        /// for the step to notice it is done (the code really compiles with arduino-cli and runs; the world is saved to a
        /// temporary file). Results go to a log; the snapshot shows the card at the end.
        /// </summary>
        internal async void RunGuideCheck(string logPath, int steps = int.MaxValue)
        {
            var log = new System.Text.StringBuilder();
            int failures = 0;
            try
            {
                StartGuide();
                _guideTimer?.Stop();   // driven step by step here
                for (int i = 0; i < Math.Min(_guide.Count - 1, steps); i++)
                {
                    var step = _guide[_guideIndex];
                    if (step.Title == "Kaydedin" && _guideWorld != null && W.Documents.FirstOrDefault(d => d.World == _guideWorld) is { } doc)
                    {
                        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "robotwin-guide-e2e", "servo-kol.rwworld");
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                        W.SaveWorld(doc, path);   // what Ctrl+S does after the file dialog
                    }
                    else
                    {
                        step.Do?.Invoke();
                    }

                    var start = DateTime.UtcNow;
                    while (!step.Done() && (DateTime.UtcNow - start).TotalSeconds < 60) await System.Threading.Tasks.Task.Delay(250);
                    bool ok = step.Done();
                    if (!ok) failures++;
                    log.AppendLine($"{(ok ? "OK  " : "FAIL")} {i + 1}. {step.Title} ({(DateTime.UtcNow - start).TotalSeconds:F1} s)" + (ok ? string.Empty : " — " + W.Status));
                    if (!ok) break;
                    NextGuideStep();
                }
            }
            catch (Exception ex)
            {
                failures++;
                log.AppendLine("FAIL beklenmeyen hata: " + ex);
            }

            log.AppendLine(failures == 0 ? "TÜMÜ GEÇTİ" : failures + " HATA");
            System.IO.File.WriteAllText(logPath, log.ToString());
            if (_guideCard?.Visibility == Visibility.Visible) _guideTimer?.Start();   // stopped part-way: the card goes on as it does for the user (it outlines its control)
        }

        private void CloseGuide()
        {
            _guideTimer?.Stop();
            _guideMark?.Remove();
            _guideMark = null;
            _guideMarked = null;
            _code.Uploaded -= OnGuideUpload;
            if (_guideCard != null) _guideCard.Visibility = Visibility.Collapsed;
        }
    }
}
