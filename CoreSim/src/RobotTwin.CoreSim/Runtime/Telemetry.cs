using System;
using System.Collections.Generic;

namespace RobotTwin.CoreSim.Runtime
{


    public class EventLogEntry
    {
        public float TimeSeconds { get; set; }
        public string Code { get; set; } = "INFO";
        public string Message { get; set; } = string.Empty;
        public LogSeverity Severity { get; set; } = LogSeverity.Info;
    }

    public enum LogSeverity
    {
        Info,
        Warning,
        Error
    }
}
