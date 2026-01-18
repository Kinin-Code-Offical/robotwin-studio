using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RobotWinFirmwareMonitor
{
    internal static class Program
    {
        private static readonly string LogDirectory = ResolveLogDirectory();
        private static readonly string CrashLogPath = Path.Combine(LogDirectory, "runtime.log");
        internal static string LogDirectoryPath => LogDirectory;

        [STAThread]
        private static void Main()
        {
            SetupCrashLogging();
            LogMessage("startup", "RobotWinFirmwareMonitor starting.");
            try
            {
                ApplicationConfiguration.Initialize();
                LogMessage("startup", "Creating MainForm.");
                var form = new MainForm();
                LogMessage("startup", "MainForm created.");
                Application.ApplicationExit += (_, __) => LogMessage("exit", "ApplicationExit fired.");
                AppDomain.CurrentDomain.ProcessExit += (_, __) => LogMessage("exit", "ProcessExit fired.");
                Application.Run(form);
                LogMessage("exit", "Application.Run returned.");
            }
            catch (Exception ex)
            {
                LogException("fatal", ex);
                ShowCrashMessage(ex);
            }
        }

        private static void SetupCrashLogging()
        {
            Directory.CreateDirectory(LogDirectory);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, args) =>
            {
                LogException("ui", args.Exception);
                ShowCrashMessage(args.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                {
                    LogException("domain", ex);
                }
                else
                {
                    LogMessage("domain", $"Non-exception: {args.ExceptionObject}");
                }
            };
            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogException("task", args.Exception);
                args.SetObserved();
            };
        }

        private static string ResolveLogDirectory()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 6 && current != null; i++)
            {
                string candidateRoot = current.FullName;
                string logsRoot = Path.Combine(candidateRoot, "logs");
                if (Directory.Exists(logsRoot) || Directory.Exists(Path.Combine(candidateRoot, ".git")))
                {
                    return Path.Combine(logsRoot, "RobotWinFirmwareMonitor");
                }
                current = current.Parent;
            }
            return Path.Combine(Directory.GetCurrentDirectory(), "logs", "RobotWinFirmwareMonitor");
        }

        internal static void LogException(string tag, Exception ex)
        {
            if (ex == null) return;
            string message = $"[{DateTime.Now:O}] [{tag}] {ex}\n";
            File.AppendAllText(CrashLogPath, message);
        }

        internal static void LogMessage(string tag, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            string entry = $"[{DateTime.Now:O}] [{tag}] {message}\n";
            File.AppendAllText(CrashLogPath, entry);
        }

        private static void ShowCrashMessage(Exception ex)
        {
            string message = "RobotWinFirmwareMonitor crashed.\n"
                + $"A log was written to:\n{CrashLogPath}\n\n"
                + $"{ex.GetType().Name}: {ex.Message}";
            MessageBox.Show(message, "RobotWinFirmwareMonitor", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
