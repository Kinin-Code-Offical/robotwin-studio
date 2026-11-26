using System;
using System.Globalization;
using System.IO;
using System.Text;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Sandbox;

namespace RobotWin.Studio
{
    public partial class App
    {
        /// <summary>
        /// [experiment] --experiment spec.json [--experiment-world world.rwworld] [--experiment-out folder]: a designed
        /// experiment run headless, without a window (CoreSim Automation/Experiment.cs). The world is the one named on the
        /// command line, else the spec's WorldFile (relative to the spec). The folder gets runs.csv (every run),
        /// summary.csv (each combination's mean, standard deviation and 95 % confidence interval), report.md and
        /// experiment.json (the spec as run, with its seeds), and a progress log. 0: every run finished; 1: some failed;
        /// 2: it could not start.
        /// </summary>
        private static int ExperimentCommand(string specPath, string? worldPath, string? outFolder)
        {
            string log = Path.Combine(Path.GetTempPath(), "robotwin-experiment.log");
            try
            {
                var spec = ExperimentSpec.FromJson(File.ReadAllText(specPath));
                string specDir = Path.GetDirectoryName(Path.GetFullPath(specPath)) ?? ".";
                string? world = worldPath ?? (spec.WorldFile is { Length: > 0 } wf ? (Path.IsPathRooted(wf) ? wf : Path.Combine(specDir, wf)) : null);
                if (world == null || !File.Exists(world))
                {
                    File.WriteAllText(log, "Dünya dosyası yok: --experiment-world ile ya da deneyin worldFile alanıyla verin.\n");
                    return 2;
                }

                string stem = string.Concat(spec.Name.Split(Path.GetInvalidFileNameChars()));
                string folder = outFolder ?? Path.Combine(specDir, stem + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
                Directory.CreateDirectory(folder);
                log = Path.Combine(folder, "progress.log");
                var doc = WorldDocument.Load(world);
                var sb = new StringBuilder();
                var result = ExperimentRunner.Run(spec, doc, new LogProgress(p =>
                {
                    sb.Append(p.Done).Append('/').Append(p.Total).Append('\n');
                    File.WriteAllText(log, sb.ToString());
                }));
                var utf8 = new UTF8Encoding(false);
                File.WriteAllText(Path.Combine(folder, "runs.csv"), result.RunsCsv(), utf8);
                File.WriteAllText(Path.Combine(folder, "summary.csv"), result.SummaryCsv(), utf8);
                File.WriteAllText(Path.Combine(folder, "report.md"), result.Markdown(doc.Title), utf8);
                spec.WorldFile = Path.GetFullPath(world);
                File.WriteAllText(Path.Combine(folder, "experiment.json"), spec.ToJson(), utf8);
                return result.Runs.Exists(r => r.Error.Length > 0) ? 1 : 0;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is System.Text.Json.JsonException || ex is InvalidDataException)
            {
                File.AppendAllText(log, "Deney çalıştırılamadı: " + ex.Message + "\n");
                return 2;
            }
        }

        /// <summary>Progress written as it comes (a Progress&lt;T&gt; would post to the dispatcher, which a headless run never pumps).</summary>
        private sealed class LogProgress : IProgress<(int Done, int Total)>
        {
            private readonly Action<(int Done, int Total)> _write;

            public LogProgress(Action<(int Done, int Total)> write) => _write = write;

            public void Report((int Done, int Total) value) => _write(value);
        }
    }
}
