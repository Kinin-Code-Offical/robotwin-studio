using System;
using System.IO;
using System.Linq;
using System.Windows;

namespace RobotWin.Studio.Pages
{
    public partial class CodePage
    {
        /// <summary>
        /// Used by --snapshot: builds (optionally with an injected error) and runs the current board's code; "complete",
        /// "signature", "servo", "libraries" and "explorer" show the completion list, the call tip, member completion, the
        /// library tab and a folder of several files (docs pictures).
        /// </summary>
        public void SnapshotBuildAndRun(string? fault)
        {
            if (fault != null && fault.StartsWith("pin:", StringComparison.Ordinal))
            {
                // [crossprobe] the caret on the first place a token stands ("pin:LED_PIN"), the pin it means and its wiring
                Refresh();
                int at = Editor.Box.Text.IndexOf(fault.Substring(4), StringComparison.Ordinal);
                if (at >= 0) Editor.Box.Select(at + 1, 0);
                ProbePin();
                return;
            }

            if (fault != null && fault.StartsWith("world:", StringComparison.Ordinal) && Window.GetWindow(this) is MainWindow main
                && Examples.WorldTemplates.Find(fault.Substring(6)) is { } template)
            {
                // A world template open beside the project: its boards are listed and writable too.
                main.NewWorld(template.Build());
                main.Navigate("code");
                Refresh();
                return;
            }

            if (fault is "esp32-libs" or "esp32-libs-tab" && Window.GetWindow(this) is MainWindow m2 && Examples.WorldTemplates.Find("trafik-isigi") is { } lights)
            {
                // [libs] An ESP32 sketch using an installed Arduino library (a temporary library folder, not the user's).
                string root = Path.Combine(Path.GetTempPath(), "robotwin-kod");
                string libs = Path.Combine(root, "libraries");
                if (Directory.Exists(libs)) Directory.Delete(libs, true);
                void Put(string rel, string text)
                {
                    string p = Path.Combine(libs, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                    File.WriteAllText(p, text);
                }

                Put("Yanip/library.properties", "name=Yanip Soner\nversion=1.2.0\nauthor=RobotWin\nsentence=Bir LED'i engellemeden yakip sondurur.\narchitectures=*\nincludes=Yanip.h\n");
                Put("Yanip/src/Yanip.h", "#pragma once\n#include <Arduino.h>\n\nclass Yanip {\npublic:\n  Yanip(int pin, unsigned long aralik) : _pin(pin), _aralik(aralik) {}\n  void baslat();\n  void guncelle();\n  unsigned long sayac() const { return _sayac; }\nprivate:\n  int _pin;\n  unsigned long _aralik, _son = 0, _sayac = 0;\n  bool _acik = false;\n};\n");
                Put("Yanip/src/Yanip.cpp", "#include \"Yanip.h\"\n\nvoid Yanip::baslat() { pinMode(_pin, OUTPUT); }\n\nvoid Yanip::guncelle() {\n  if (millis() - _son < _aralik) return;\n  _son = millis();\n  _acik = !_acik;\n  digitalWrite(_pin, _acik ? HIGH : LOW);\n  _sayac++;\n}\n");
                if (!Firmware.ArduinoCompiler.SessionLibraryRoots.Contains(libs)) Firmware.ArduinoCompiler.SessionLibraryRoots.Add(libs);

                m2.NewWorld(lights.Build());
                m2.Navigate("code");
                string dir = Path.Combine(root, "yanip_soner");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "yanip_soner.ino"), "#include <Yanip.h>\n\n// Kurulu bir Arduino kütüphanesi ESP32'de: kırmızı LED 500 ms'de bir yanıp söner.\nYanip kirmizi(25, 500);\n\nvoid setup() {\n  Serial.begin(115200);\n  kirmizi.baslat();\n}\n\nvoid loop() {\n  kirmizi.guncelle();\n}\n");
                if (!SessionFolders.Contains(dir)) SessionFolders.Add(dir);
                Refresh();
                var esp = _targets.FirstOrDefault(t => Firmware.ArduinoCompiler.IsEsp32(t.Fqbn));
                if (esp != null)
                {
                    _loading = true;
                    _target = esp;
                    TargetBox.SelectedItem = esp;
                    _loading = false;
                }

                var sketch = _folders.FirstOrDefault(x => x is DiskCode d && string.Equals(d.Folder, Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));
                Open(sketch, "yanip_soner.ino", rebuild: true);
                LibrariesChanged();
                UpdateTargetText();
                if (fault == "esp32-libs-tab")
                {
                    _dock?.Reveal("libs");
                    return;
                }

                OnBuild(this, new RoutedEventArgs());
                return;
            }

            if (fault is "explorer" or "explorer-upload")
            {
                // A sketch of several files in a folder on disk (a temporary workspace, not the user's).
                string dir = Path.Combine(Path.GetTempPath(), "robotwin-kod", "iki_eklemli_kol");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Directory.CreateDirectory(Path.Combine(dir, "src"));
                File.WriteAllText(Path.Combine(dir, "iki_eklemli_kol.ino"), "#include \"src/Eklem.h\"\n\nEklem omuz(9), dirsek(10);\n\nvoid setup() {\n  Serial.begin(9600);\n}\n\nvoid loop() {\n  omuz.git(90);\n  dirsek.git(45);\n}\n");
                File.WriteAllText(Path.Combine(dir, "src", "Eklem.h"), "#pragma once\n#include <Arduino.h>\n\nclass Eklem {\npublic:\n  explicit Eklem(int pin);\n  void git(int aci);\nprivate:\n  int _pin;\n};\n");
                File.WriteAllText(Path.Combine(dir, "src", "Eklem.cpp"), "#include \"Eklem.h\"\n\nEklem::Eklem(int pin) : _pin(pin) { pinMode(pin, OUTPUT); }\n\nvoid Eklem::git(int aci) {\n  int us = map(aci, 0, 180, 544, 2400);\n  digitalWrite(_pin, HIGH);\n  delayMicroseconds(us);\n  digitalWrite(_pin, LOW);\n}\n");
                File.WriteAllText(Path.Combine(dir, "BENIOKU.md"), "# İki eklemli kol\n");
                if (!SessionFolders.Contains(dir)) SessionFolders.Add(dir);   // not saved in the user's settings
                Refresh();
                var f = _folders.FirstOrDefault(x => x is DiskCode d && string.Equals(d.Folder, Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase));
                if (f != null) Open(f, "src/Eklem.cpp", rebuild: true);
                if (fault == "explorer-upload" && f != null && _target != null) UploadTo(f, _target);   // the folder onto the project's Uno
                return;
            }

            if (fault is "complete" or "signature" or "libraries" or "servo")
            {
                if (fault == "servo") Editor.Text = "#include <Servo.h>\nServo kol;\n\nvoid setup() {\n  kol.attach(9);\n}\n\nvoid loop() {\n  kol.\n}\n";
                Editor.Focus();
                string text = Editor.Text;
                int at = fault == "servo" ? text.IndexOf("kol.\n", StringComparison.Ordinal) + 4 : text.IndexOf("void loop()", StringComparison.Ordinal);
                if (fault is "complete" or "signature" && at >= 0)
                {
                    at = text.IndexOf('{', at) + 1;
                    at = text.IndexOf('\n', at) + 1;
                    string typed = fault == "complete" ? "  Serial.pr" : "  analogWrite(9, ";
                    Editor.Box.Select(at, 0);
                    Editor.Box.SelectedText = typed + "\n";
                    Editor.Box.Select(at + typed.Length, 0);
                }
                else if (at >= 0)
                {
                    Editor.Box.Select(at, 0);
                }

                if (fault == "libraries")
                {
                    _dock?.Reveal("libs");
                    _libSearch.Text = "servo";
                    return;
                }

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (fault == "signature") Editor.ShowSignatureForSnapshot();
                    else Editor.Open(true);
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                return;
            }

            if (fault == "syntax")
            {
                Editor.Text = Editor.Text.Replace("delay(500);", "delay(500)").Replace("Serial.begin", "Seriall.begin");
                OnTextChanged(this, EventArgs.Empty);
                OnBuild(this, new RoutedEventArgs());
            }
            else
            {
                OnUpload(this, new RoutedEventArgs());
            }
        }
    }
}
