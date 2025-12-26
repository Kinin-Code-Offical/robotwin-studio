using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RobotTwin.CoreSim.Infrastructure
{
    public class ReplayEngine
    {
        private List<string> _logLines;
        private int _currentTick;
        public bool IsPlaying { get; private set; }
        public int TotalTicks => _logLines?.Count ?? 0;

        public void LoadLog(string logPath)
        {
            if (!File.Exists(logPath))
            {
                throw new FileNotFoundException("Log file not found", logPath);
            }
            _logLines = File.ReadAllLines(logPath).ToList(); // MVP: Read all into memory. Optimize for large logs later.
            _currentTick = 0;
            IsPlaying = false;
        }

        public string GetStateAtTick(int tick)
        {
            if (_logLines == null || tick < 0 || tick >= _logLines.Count) return null;
            return _logLines[tick];
        }

        public string Tick()
        {
            if (!IsPlaying) return null;
            
            if (_currentTick >= _logLines.Count)
            {
                IsPlaying = false;
                return null;
            }

            var state = _logLines[_currentTick];
            _currentTick++;
            return state;
        }

        public void Play() => IsPlaying = true;
        public void Pause() => IsPlaying = false;
        public void Seek(int tick)
        {
            _currentTick = Math.Clamp(tick, 0, TotalTicks - 1);
        }
    }
}
