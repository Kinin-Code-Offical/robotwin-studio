using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Automation;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Esp32;
using RobotTwin.CoreSim.Sandbox;

namespace RobotTwin.CoreSim.Analysis.Recording
{
    /// <summary>
    /// [unified-timeline] What a <see cref="WorldProbe"/> records from: a headless <see cref="WorldRunner"/> (renders,
    /// analyses), or a live session in the app (a recording made as the world runs, without a render). It is read on the
    /// thread that steps it, from its <see cref="Sliced"/> event.
    /// </summary>
    public interface IProbeSource
    {
        WorldDocument World { get; }

        /// <summary>Simulated seconds since the run started.</summary>
        double Time { get; }

        WorldPhysics? Physics { get; }
        AnalogSimulator? Simulator { get; }
        CompiledCircuit? Compiled { get; }

        /// <summary>The programmable boards of the run, the same object for a board each time.</summary>
        IReadOnlyList<ProbeBoard> ProbeBoards { get; }

        /// <summary>After each step of the run, with its length.</summary>
        event Action<double>? Sliced;

        /// <summary>What each board printed since the last call, by board name.</summary>
        IReadOnlyDictionary<string, string> TakeSerial();

        /// <summary>The run's events (log lines, failures, knocks) since the last call.</summary>
        IReadOnlyList<RunnerEvent> TakeEvents();
    }

    /// <summary>A programmable board as a probe sees it: its names, and its chip to trace (an AVR, or an ESP32 in either mode).</summary>
    public sealed class ProbeBoard
    {
        public string ObjectId { get; set; } = string.Empty;
        public string RefDes { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool Running { get; set; }
        public FirmwareCoSimulation? Avr { get; set; }
        public IEsp32Board? Esp32 { get; set; }
    }
}
