using UnityEngine;
using System.IO;
using System;

namespace RobotTwin.Debugging
{
    public class ConsoleToDisk : MonoBehaviour
    {
        private static string _logPath;
        private static object _lock = new object();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.Parent?.FullName
                                 ?? Directory.GetParent(Application.dataPath).FullName;
            string logDir = Path.Combine(projectRoot, "logs", "unity");
            if (!Directory.Exists(logDir)) Directory.CreateDirectory(logDir);
            _logPath = Path.Combine(logDir, "unity_live_error.log");

            // clear on start
            try { File.WriteAllText(_logPath, $"[SESSION START] {DateTime.Now}\n"); } catch { }

            Application.logMessageReceivedThreaded += HandleLog;
        }

        private static void HandleLog(string logString, string stackTrace, LogType type)
        {
             // Log everything for the Monitor to see "SUCCESS" logic
            lock (_lock)
            {
                try
                {
                    string entry = $"\n[{DateTime.Now:HH:mm:ss}] [{type.ToString().ToUpper()}] {logString}\n";
                    File.AppendAllText(_logPath, entry);
                }
                catch { }
            }
        }
    }
}
