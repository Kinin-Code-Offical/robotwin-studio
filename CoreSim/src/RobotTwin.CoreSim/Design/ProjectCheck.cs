using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RobotTwin.CoreSim.Formats;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Design
{
    public enum IssueLevel
    {
        /// <summary>Something will not work on this computer until it is mended.</summary>
        Error,

        /// <summary>It works, in a lesser way (another mode, an older build).</summary>
        Warning,

        /// <summary>It works; only changing it needs something.</summary>
        Note
    }

    /// <summary>[integrity] What mends an issue.</summary>
    public enum IssueFix
    {
        None,

        /// <summary>The installer's Arduino compiler (or the user's own arduino-cli, Settings → Compilers).</summary>
        ArduinoCompiler,

        /// <summary>Visual Studio's C++ tools (MSVC): the ESP32 fast mode builds its sketches with them.</summary>
        Msvc,

        /// <summary>Arduino-ESP32, for the hardware-compatible mode (the toolchain manager).</summary>
        Esp32Toolchain,

        /// <summary>An Arduino library; <see cref="ProjectIssue.Argument"/> is its name in the library index.</summary>
        Library,

        /// <summary>A part definition this computer lacks: its .rwpart / .rwlib imported on the Library page.</summary>
        Part
    }

    /// <summary>[integrity] One thing a project or world needs that this computer lacks.</summary>
    public sealed class ProjectIssue
    {
        public IssueLevel Level { get; set; }

        /// <summary>"Parçalar", "Derleyici", "Kütüphaneler" or "Kod".</summary>
        public string Area { get; set; } = string.Empty;

        /// <summary>What it is about: "U1 (Arduino Uno)", in a world "Robot kol · U1 (Arduino Uno)".</summary>
        public string Subject { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;
        public IssueFix Fix { get; set; }

        /// <summary>The library to install (<see cref="IssueFix.Library"/>).</summary>
        public string? Argument { get; set; }

        public override string ToString() => $"{Level} · {Area} · {Subject}: {Text}";
    }

    /// <summary>[integrity] What this computer has, as the app sees it.</summary>
    public sealed class ProjectEnvironment
    {
        /// <summary>The part definitions this computer knows (built in, the user's libraries, the open project's own).</summary>
        public PartLibrary Library { get; set; } = PartLibrary.Default;

        /// <summary>An arduino-cli with the AVR core (the installer's Arduino compiler, or the user's own).</summary>
        public bool ArduinoCompiler { get; set; }

        /// <summary>Visual Studio's C++ tools, which build ESP32 sketches for the fast mode.</summary>
        public bool Esp32Compiler { get; set; }

        /// <summary>Arduino-ESP32, which builds the hardware-compatible mode's firmware.</summary>
        public bool Esp32Toolchain { get; set; }

        /// <summary>Can a sketch include this header (the bool: an ESP32 sketch)? Null: libraries are not checked.</summary>
        public Func<string, bool, bool>? HeaderAvailable { get; set; }

        /// <summary>The Arduino library that provides a header (the bool: for an ESP32), null when not known.</summary>
        public Func<string, bool, string?>? LibraryFor { get; set; }
    }

    /// <summary>
    /// [integrity] What a project or a world needs that this computer may lack: the definitions of its parts, the compilers
    /// its boards' code needs (and whether the last build runs without them), the Arduino libraries the code includes.
    /// Opened on another computer, or with a lite installation, a project says so before anything fails. Pure: the app
    /// tells it what the computer has (<see cref="ProjectEnvironment"/>).
    /// </summary>
    public static class ProjectCheck
    {
        public const string PartsArea = "Parçalar";
        public const string CompilerArea = "Derleyici";
        public const string LibrariesArea = "Kütüphaneler";
        public const string CodeArea = "Kod";

        /// <summary>The ESP32 target of a sketch (it runs on RobotWin's Arduino-ESP32 model, built natively).</summary>
        public const string Esp32Target = "esp32-hal";

        public static List<ProjectIssue> Check(ProjectPackage project, ProjectEnvironment env) => Check(project, env, null);

        /// <summary>
        /// What the check reads of a project, copied: the parts and the boards' code. The check can then run on another
        /// thread (looking up libraries takes a while) while the project is being edited.
        /// </summary>
        public static ProjectPackage Snapshot(ProjectPackage project)
        {
            var copy = new ProjectPackage();
            copy.Schematic.Title = project.Schematic.Title;
            foreach (var p in project.Schematic.Parts)
                copy.Schematic.Parts.Add(new SchematicPart { RefDes = p.RefDes, PartId = p.PartId, PresetId = p.PresetId, Parameters = new Dictionary<string, string>(p.Parameters, StringComparer.OrdinalIgnoreCase) });
            foreach (var f in project.Firmware) copy.Firmware.Add(Copy(f));
            return copy;
        }

        /// <summary>What the check reads of a world, copied (<see cref="Snapshot(ProjectPackage)"/>).</summary>
        public static WorldDocument Snapshot(WorldDocument world)
        {
            var copy = new WorldDocument();
            foreach (var d in world.Designs) copy.Designs.Add(new WorldDesign { Id = d.Id, Name = d.Name, Package = Snapshot(d.Package) });
            foreach (var o in world.Objects)
            {
                copy.Objects.Add(new WorldObject
                {
                    Id = o.Id, Name = o.Name, Kind = o.Kind, DesignId = o.DesignId, PartId = o.PartId, PresetId = o.PresetId,
                    Parameters = new Dictionary<string, string>(o.Parameters, StringComparer.OrdinalIgnoreCase),
                    Firmware = o.Firmware != null ? Copy(o.Firmware) : null
                });
            }

            return copy;
        }

        private static FirmwareSource Copy(FirmwareSource f) => new FirmwareSource
        {
            BoardRefDes = f.BoardRefDes, Target = f.Target, FileName = f.FileName, Source = f.Source, Hex = f.Hex, BuiltAt = f.BuiltAt, BuiltFromHash = f.BuiltFromHash,
            Files = f.Files.Select(x => new FirmwareFile { Path = x.Path, Content = x.Content }).ToList()
        };

        public static List<ProjectIssue> Check(WorldDocument world, ProjectEnvironment env)
        {
            var issues = new List<ProjectIssue>();
            foreach (var o in world.Objects)
            {
                if (o.Kind == WorldObjectKind.Board && world.FindDesign(o.DesignId) == null)
                {
                    issues.Add(new ProjectIssue
                    {
                        Level = IssueLevel.Error, Area = PartsArea, Subject = o.Name,
                        Text = "Kartın tasarımı dünyada yok: kart çalışmaz. Tasarımı yeniden ekleyin."
                    });
                }
                else if (o.Kind == WorldObjectKind.Component && !string.IsNullOrEmpty(o.PartId))
                {
                    var def = env.Library.Find(o.PartId!);
                    if (def == null)
                    {
                        issues.Add(UnknownPart(o.Name, o.PartId!));
                        continue;
                    }

                    // A board placed as a part carries its own code.
                    if (o.Firmware != null)
                    {
                        string subject = o.Name;
                        CheckFirmware(issues, env, o.Firmware, subject, o.PartId!, o.Parameters);
                    }
                }
            }

            foreach (var d in world.Designs)
                if (d.Package != null) issues.AddRange(Check(d.Package, env, d.Name));
            return Ordered(issues);
        }

        private static List<ProjectIssue> Check(ProjectPackage project, ProjectEnvironment env, string? prefix)
        {
            var issues = new List<ProjectIssue>();
            string P(string subject) => string.IsNullOrEmpty(prefix) ? subject : prefix + " · " + subject;
            var sch = project.Schematic;
            foreach (var part in sch.Parts)
            {
                if (string.IsNullOrEmpty(part.PartId) || env.Library.Find(part.PartId) != null) continue;
                issues.Add(UnknownPart(P(part.RefDes), part.PartId));
            }

            foreach (var fw in project.Firmware)
            {
                var board = sch.Parts.FirstOrDefault(p => string.Equals(p.RefDes, fw.BoardRefDes, StringComparison.OrdinalIgnoreCase));
                if (board == null)
                {
                    if (!string.IsNullOrWhiteSpace(fw.Source))
                    {
                        issues.Add(new ProjectIssue
                        {
                            Level = IssueLevel.Note, Area = CodeArea, Subject = P(fw.BoardRefDes),
                            Text = "Bu kartın kodu projede duruyor ama kart şemada yok; çalışmaz. Kartı geri ekleyin ya da kodu silin."
                        });
                    }

                    continue;
                }

                CheckFirmware(issues, env, fw, P(BoardName(env.Library, board)), board.PartId, board.Parameters);
            }

            return prefix == null ? Ordered(issues) : issues;
        }

        private static ProjectIssue UnknownPart(string subject, string partId) => new ProjectIssue
        {
            Level = IssueLevel.Error, Area = PartsArea, Subject = subject, Fix = IssueFix.Part, Argument = partId,
            Text = $"'{partId}' parçasının tanımı bu bilgisayarda yok (proje de içermiyor): parça çizilir ama simüle edilmez. Parçanın .rwpart ya da .rwlib dosyasını Kütüphane sayfasından içe aktarın."
        };

        private static string BoardName(PartLibrary library, SchematicPart part)
        {
            string? name = (part.PresetId != null ? library.FindPreset(part.PresetId)?.Name : null) ?? library.Find(part.PartId)?.Name;
            return string.IsNullOrEmpty(name) ? part.RefDes : part.RefDes + " (" + name + ")";
        }

        private static void CheckFirmware(List<ProjectIssue> issues, ProjectEnvironment env, FirmwareSource fw, string subject, string partId, IReadOnlyDictionary<string, string>? parameters)
        {
            if (string.IsNullOrWhiteSpace(fw.Source)) return;
            bool esp32 = fw.Target == Esp32Target || partId.StartsWith("esp32", StringComparison.OrdinalIgnoreCase);
            bool avr = !esp32 && fw.Target.StartsWith("arduino:avr:", StringComparison.OrdinalIgnoreCase);
            if (!esp32 && !avr) return;
            bool buildNeeded;
            if (avr)
            {
                // An Uno / Nano runs its last build (the hex in the project) when it is the code's; the compiler is needed
                // only to build the code again.
                buildNeeded = !fw.IsBuildCurrent;
                if (!env.ArduinoCompiler)
                {
                    issues.Add(buildNeeded
                        ? new ProjectIssue
                        {
                            Level = IssueLevel.Error, Area = CompilerArea, Subject = subject, Fix = IssueFix.ArduinoCompiler,
                            Text = "Kod derlenmemiş ya da son derlemeden sonra değişmiş; çalışması için Arduino derleyicisi gerekli."
                        }
                        : new ProjectIssue
                        {
                            Level = IssueLevel.Note, Area = CompilerArea, Subject = subject, Fix = IssueFix.ArduinoCompiler,
                            Text = "Kod son derlemesiyle çalışır; değiştirip yeniden derlemek için Arduino derleyicisi gerekli."
                        });
                }
            }
            else
            {
                // An ESP32's sketch is built on this computer for the fast mode (with MSVC); the hardware-compatible mode
                // builds it with Arduino-ESP32 and runs in the fast mode until that is there.
                buildNeeded = true;
                bool hardware = Esp32Parts.IsHardwareMode(parameters);
                if (!env.Esp32Compiler)
                {
                    issues.Add(new ProjectIssue
                    {
                        Level = IssueLevel.Error, Area = CompilerArea, Subject = subject, Fix = IssueFix.Msvc,
                        Text = "ESP32 kodu bu bilgisayarda derlenerek çalışır: Visual Studio C++ araçları (MSVC, \"C++ ile masaüstü geliştirme\") gerekli."
                    });
                }

                if (hardware && !env.Esp32Toolchain)
                {
                    issues.Add(new ProjectIssue
                    {
                        Level = IssueLevel.Warning, Area = CompilerArea, Subject = subject, Fix = IssueFix.Esp32Toolchain,
                        Text = "Kart donanım uyumlu modda; bunun için Arduino-ESP32 araç zinciri gerekli. Kurulana kadar kart hızlı modda çalışır."
                    });
                }
            }

            // The libraries the code includes, when the headers can be looked up (for an Uno / Nano: with its compiler).
            if (env.HeaderAvailable == null || (avr && !env.ArduinoCompiler)) return;
            foreach (string header in Includes(fw))
            {
                if (Toolchain(header, esp32) || env.HeaderAvailable(header, esp32)) continue;
                string? library = env.LibraryFor?.Invoke(header, esp32);
                issues.Add(new ProjectIssue
                {
                    Level = buildNeeded ? IssueLevel.Error : IssueLevel.Note,
                    Area = LibrariesArea,
                    Subject = subject,
                    Fix = library != null ? IssueFix.Library : IssueFix.None,
                    Argument = library,
                    Text = library != null
                        ? $"<{header}> bulunamadı: '{library}' kütüphanesi kurulu değil." + (buildNeeded ? string.Empty : " Kod son derlemesiyle çalışır; yeniden derlemek için gerekli.")
                        : $"<{header}> bulunamadı ve hangi kütüphaneden geldiği bilinmiyor; Kod sayfasının Kütüphaneler sekmesinde arayın."
                });
            }
        }

        private static readonly Regex IncludeLine = new Regex(@"^[ \t]*#[ \t]*include[ \t]*[<""]([^>""\r\n]+)[>""]", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        /// <summary>The headers a sketch includes, apart from its own files (each once, in order).</summary>
        public static IReadOnlyList<string> Includes(FirmwareSource fw)
        {
            var own = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { fw.FileName };
            foreach (var f in fw.Files)
            {
                own.Add(f.Path.Replace('\\', '/'));
                own.Add(System.IO.Path.GetFileName(f.Path));
            }

            var found = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string text in new[] { fw.Source }.Concat(fw.Files.Select(f => f.Content)))
            {
                foreach (Match m in IncludeLine.Matches(StripBlockComments(text ?? string.Empty)))
                {
                    string h = m.Groups[1].Value.Trim().Replace('\\', '/');
                    if (h.Length == 0 || own.Contains(h) || own.Contains(System.IO.Path.GetFileName(h)) || h.StartsWith("../", StringComparison.Ordinal) || h.StartsWith("./", StringComparison.Ordinal)) continue;
                    if (seen.Add(h)) found.Add(h);
                }
            }

            return found;
        }

        private static string StripBlockComments(string text) => text.IndexOf("/*", StringComparison.Ordinal) < 0 ? text : Regex.Replace(text, @"/\*.*?\*/", m => new string('\n', m.Value.Count(c => c == '\n')), RegexOptions.Singleline);

        private static readonly HashSet<string> CHeaders = new HashSet<string>(StringComparer.Ordinal)
        {
            "assert.h", "complex.h", "ctype.h", "errno.h", "fenv.h", "float.h", "inttypes.h", "iso646.h", "limits.h", "locale.h", "math.h",
            "setjmp.h", "signal.h", "stdalign.h", "stdarg.h", "stdbool.h", "stddef.h", "stdint.h", "stdio.h", "stdlib.h", "string.h",
            "strings.h", "tgmath.h", "time.h", "uchar.h", "wchar.h", "wctype.h", "alloca.h", "memory.h", "unistd.h", "pgmspace.h"
        };

        /// <summary>The compiler's own headers (C and C++ libraries, avr-libc, ESP-IDF): never an Arduino library.</summary>
        private static bool Toolchain(string header, bool esp32)
        {
            if (!header.Contains('.')) return true;   // <vector>, <cmath>, <functional>
            if (CHeaders.Contains(header) || header.StartsWith("sys/", StringComparison.Ordinal) || header.StartsWith("machine/", StringComparison.Ordinal)) return true;
            if (!esp32) return header.StartsWith("avr/", StringComparison.Ordinal) || header.StartsWith("util/", StringComparison.Ordinal) || header.StartsWith("compat/", StringComparison.Ordinal);
            foreach (string p in new[] { "esp_", "driver/", "freertos/", "soc/", "hal/", "rom/", "esp32/", "lwip/", "nvs", "sdkconfig", "mbedtls/", "esp32-hal" })
                if (header.StartsWith(p, StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Errors first, then warnings and notes; by area and subject within each.</summary>
        private static List<ProjectIssue> Ordered(List<ProjectIssue> issues) =>
            issues.OrderBy(i => i.Level).ThenBy(i => i.Area, StringComparer.Ordinal).ThenBy(i => i.Subject, StringComparer.Ordinal).ToList();
    }
}
