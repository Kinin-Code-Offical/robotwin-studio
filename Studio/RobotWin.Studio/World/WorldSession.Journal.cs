using RobotTwin.CoreSim.Automation;

namespace RobotWin.Studio.World
{
    /// <summary>
    /// [replay] While a live run is being recorded, every input that reaches the world is written down with
    /// the simulation time it took effect at: controller reports (when they change), controller power and
    /// pairing, serial text, USB plugging, IR keys, conveyor commands, light-curtain tests and launch switches.
    /// The offline render replays them (<see cref="InputReplay"/>) so it computes the same experiment.
    /// </summary>
    public sealed partial class WorldSession
    {
        /// <summary>The inputs being recorded, or null when not recording.</summary>
        public InputJournal? Journal { get; private set; }

        public void BeginJournal() => Journal = new InputJournal();

        /// <summary>Stops recording and returns what was recorded (its duration set to the run time).</summary>
        public InputJournal? EndJournal()
        {
            var j = Journal;
            if (j != null) j.Duration = Time;
            Journal = null;
            return j;
        }

        private void Note(string kind, string target, double value = 0, string? text = null) => Journal?.Note(Time, kind, target, value, text);

        /// <summary>Starts or stops a conveyor belt (recorded).</summary>
        public void SetConveyor(string objectId, bool running)
        {
            if (Physics?.ConveyorOf(objectId) is not { } belt) return;
            belt.SetRunning(running);
            Note(InputEvent.Conveyor, objectId, running ? 1 : 0);
        }

        /// <summary>Simulates someone reaching through a light curtain for a while (recorded).</summary>
        public void InterruptCurtain(string objectId, double seconds)
        {
            if (Physics == null) return;
            Physics.InterruptCurtain(objectId, seconds);
            Note(InputEvent.Curtain, objectId, seconds);
        }

        /// <summary>Throws a launch / power switch (recorded).</summary>
        public bool SetSwitch(string objectId, bool closed)
        {
            if (Physics == null || !Physics.SetSwitch(objectId, closed)) return false;
            Note(InputEvent.Switch, objectId, closed ? 1 : 0);
            return true;
        }
    }
}
