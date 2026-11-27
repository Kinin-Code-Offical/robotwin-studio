using System;
using System.IO;
using System.Linq;
using RobotTwin.CoreSim.Analog;
using RobotTwin.CoreSim.Design;
using RobotTwin.CoreSim.Firmware;
using RobotTwin.CoreSim.Firmware.Debug;
using Xunit;
using Xunit.Abstractions;

namespace RobotTwin.CoreSim.Tests
{
    /// <summary>
    /// [debug] Source-level debugging of a real avr-gcc build (the servo arm's sketch, Fixtures/servo-arm-debug.*):
    /// line tables and variables from its ELF, breakpoints that stop the CPU before the line's first instruction,
    /// steps by source line, and one clock for the program and the circuit.
    /// </summary>
    [Trait("Category", "Firmware")]
    public class FirmwareDebugTests
    {
        private readonly ITestOutputHelper _out;

        public FirmwareDebugTests(ITestOutputHelper output) => _out = output;

        private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

        private static FirmwareDebugInfo Info() => FirmwareDebugInfo.FromElf(File.ReadAllBytes(Fixture("servo-arm-debug.elf")));

        private static Atmega328P Mcu()
        {
            var mcu = new Atmega328P();
            mcu.LoadHex(File.ReadAllText(Fixture("servo-arm-debug.hex")));
            return mcu;
        }

        private static int LineOf(string text) =>
            File.ReadAllLines(Fixture("servo-arm-debug.ino")).Select((l, i) => (l, i)).First(x => x.l.Contains(text, StringComparison.Ordinal)).i + 1;

        [Fact]
        public void TheElfGivesTheSketchLinesFunctionsAndVariables()
        {
            var info = Info();
            Assert.Contains("sketch.ino", info.SketchFiles);
            int addLine = LineOf("angle += step;");
            var (line, addresses) = info.Breakpoint("sketch.ino", addLine);
            Assert.Equal(addLine, line);
            Assert.NotEmpty(addresses);
            Assert.Equal(addLine, info.Locate(addresses[0])!.Value.Line);
            Assert.True(info.Locate(addresses[0])!.Value.InSketch);
            Assert.Equal("loop", info.FunctionAt(addresses[0]));

            // A line without code (the globals' declaration) stops at the next line that has some: loop's first.
            int declLine = LineOf("int angle = 0, step");
            var (snapped, _) = info.Breakpoint("sketch.ino", declLine);
            Assert.Equal(LineOf("pulse(SERVO_A"), snapped);
            Assert.Equal(-1, info.Breakpoint("sketch.ino", 400).Line);

            var angle = info.Variables.Single(v => v.Name == "angle");
            var step = info.Variables.Single(v => v.Name == "step");
            Assert.Equal("sketch.ino", angle.File);
            Assert.Equal(declLine, angle.Line);
            Assert.Equal(DebugTypeKind.Base, angle.Type.Kind);
            Assert.Equal(2, angle.Type.Size);   // int is 16 bits on the AVR
            Assert.Equal(5, angle.Type.Encoding);   // signed
            Assert.InRange(angle.Address, 0x100, 0x8FF);
            Assert.InRange(step.Address, 0x100, 0x8FF);
            Assert.DoesNotContain(info.Variables, v => v.Name == "timer0_millis");   // the core's own are not the user's
            Assert.Contains(info.AllVariables, v => v.Name == "timer0_millis");
            _out.WriteLine(string.Join(", ", info.Variables.Select(v => $"{v.DisplayName}@0x{v.Address:X} {v.Type.Name}")));
        }

        [Fact]
        public void ABreakpointStopsBeforeTheLineAndResumeGoesRoundTheLoop()
        {
            var info = Info();
            var mcu = Mcu();
            var dbg = new McuDebugger(mcu, info);
            int addLine = LineOf("angle += step;");
            var (_, addresses) = info.Breakpoint("sketch.ino", addLine);
            dbg.SetBreakpoints(addresses);
            var angle = info.Variables.Single(v => v.Name == "angle");
            string Angle() => FirmwareDebugInfo.Format(angle, mcu.ReadData);

            mcu.RunCycles(16_000_000);   // up to a second
            Assert.True(mcu.Halted);
            Assert.Equal(DebugStopReason.Breakpoint, dbg.Reason);
            Assert.Equal(addLine, dbg.Place!.Value.Line);
            Assert.Equal("0", Angle());   // stopped before "angle += step" ran
            double first = mcu.Cycles / mcu.ClockHz;
            Assert.InRange(first, 0.010, 0.2);   // after setup, two servo pulses and delay(16)

            ulong before = mcu.Cycles;
            mcu.RunCycles(1000);
            Assert.Equal(before, mcu.Cycles);   // a stopped CPU does not run

            dbg.Resume();
            mcu.RunCycles(16_000_000);
            Assert.True(mcu.Halted);
            Assert.Equal("6", Angle());   // once round loop()
            double period = mcu.Cycles / mcu.ClockHz - first;
            Assert.InRange(period, 0.016, 0.022);   // delay(16) + two pulses of 0.5–2.4 ms
            _out.WriteLine($"first stop {first * 1000:F3} ms, loop period {period * 1000:F3} ms");
        }

        [Fact]
        public void TheCallStackShowsInlinedFunctionsAndTheirLocals()
        {
            // [stack] stopped inside pulse(): with link-time optimisation pulse() and loop() are inlined into main(), and
            // pulse's parameters are in registers (pin, us) at that address
            var info = Info();
            var mcu = Mcu();
            var dbg = new McuDebugger(mcu, info);
            int line = LineOf("digitalWrite(pin, LOW);");   // (delayMicroseconds' line has no code of its own: it is all inlined)
            dbg.SetBreakpoints(info.Breakpoint("sketch.ino", line).Addresses);
            mcu.RunCycles(16_000_000);
            Assert.True(mcu.Halted);
            var stack = info.Stack(mcu.ProgramCounter, mcu.StackPointer, mcu.ReadData);
            foreach (var f in stack)
                _out.WriteLine($"{f.Function}{(f.Inlined ? " (inline)" : "")} {f.Place} pc=0x{f.Pc:X4}: " + string.Join(", ", f.Locals.Select(l => $"{l.Name}={l.Value} [{l.Where}]")));
            Assert.Equal("pulse", stack[0].Function);
            Assert.True(stack[0].Inlined);
            Assert.Equal(line, stack[0].Place!.Value.Line);
            Assert.Contains(stack, f => f.Function == "loop");
            Assert.Equal("main", stack[^1].Function);
            // the caller's line is where pulse() was called from
            var loop = stack.First(f => f.Function == "loop");
            Assert.Equal(LineOf("pulse(SERVO_A, angleToUs(angle));"), loop.Place!.Value.Line);
            var pulse = stack[0];
            Assert.Equal("9", pulse.Locals.Single(l => l.Name == "pin").Value);
            Assert.Equal("544", pulse.Locals.Single(l => l.Name == "us").Value);   // angleToUs(0)

            // inside a real call (digitalWrite is not inlined): the frame below is unwound from the stack with the
            // call frame information, its return address leads back into main()
            mcu.RunCycles(1);
            dbg.SetBreakpoints(Array.Empty<int>());
            var write = info.Functions.First(f => f.Name == "digitalWrite");
            dbg.SetBreakpoints(new[] { (int)write.Low + 2 });
            dbg.Resume();
            mcu.RunCycles(16_000_000);
            Assert.True(mcu.Halted);
            stack = info.Stack(mcu.ProgramCounter, mcu.StackPointer, mcu.ReadData);
            foreach (var f in stack)
                _out.WriteLine($"{f.Function}{(f.Inlined ? " (inline)" : "")} {f.Place} pc=0x{f.Pc:X4}: " + string.Join(", ", f.Locals.Select(l => $"{l.Name}={l.Value} [{l.Where}]")));
            Assert.Equal("digitalWrite", stack[0].Function);
            Assert.False(stack[0].Inlined);
            Assert.Equal("main", stack[^1].Function);
            Assert.Contains(stack, f => f.Function == "pulse" && f.Place!.Value.InSketch);
        }

        [Fact]
        public void AnInterruptHandlersStackLeadsBackToTheCodeItInterrupted()
        {
            // [stack] stopped in TIMER0_OVF's handler (millis' counter): the interrupt pushed the return address as a call
            // does, so the frame below the handler is the code it interrupted, down to main()
            var info = Info();
            var mcu = Mcu();
            var dbg = new McuDebugger(mcu, info);
            var isr = info.Functions.First(f => f.Name == "__vector_16");
            dbg.SetBreakpoints(new[] { (int)isr.Low });
            mcu.RunCycles(16_000_000);
            Assert.True(mcu.Halted);
            var stack = info.Stack(mcu.ProgramCounter, mcu.StackPointer, mcu.ReadData);
            foreach (var f in stack)
                _out.WriteLine($"{f.Function}{(f.Inlined ? " (inline)" : "")} {f.Place} pc=0x{f.Pc:X4}");
            Assert.Equal("__vector_16", stack[0].Function);
            Assert.Equal("TIMER0_OVF", stack[0].Interrupt);
            Assert.True(stack.Count >= 2);
            Assert.Equal("main", stack[^1].Function);
        }

        [Fact]
        public void StepsGoLineByLineOverCallsAndInto()
        {
            var info = Info();
            var mcu = Mcu();
            var dbg = new McuDebugger(mcu, info);
            int addLine = LineOf("angle += step;"), testLine = LineOf("if (angle >= 180");
            dbg.SetBreakpoints(info.Breakpoint("sketch.ino", addLine).Addresses);
            mcu.RunCycles(16_000_000);
            Assert.True(mcu.Halted);

            dbg.SetBreakpoints(Array.Empty<int>());
            Assert.True(dbg.Step(DebugStepKind.Over));
            mcu.RunCycles(16_000_000);
            Assert.True(mcu.Halted);
            Assert.Equal(DebugStopReason.Step, dbg.Reason);
            Assert.Equal(testLine, dbg.Place!.Value.Line);
            Assert.Equal("6", FirmwareDebugInfo.Format(info.Variables.Single(v => v.Name == "angle"), mcu.ReadData));

            // Over again: past the "if" (not taken at 6), round to the next loop: delay(16) and the pulses run through.
            ulong at = mcu.Cycles;
            Assert.True(dbg.Step(DebugStepKind.Over));
            mcu.RunCycles(16_000_000);
            Assert.True(mcu.Halted);
            Assert.True(dbg.Place!.Value.InSketch);
            Assert.NotEqual(testLine, dbg.Place!.Value.Line);
            Assert.InRange((mcu.Cycles - at) / mcu.ClockHz, 0, 0.03);

            // Into from anywhere stops on the next line of the user's code.
            Assert.True(dbg.BreakAtNextLine());
            mcu.RunCycles(16_000_000);
            Assert.True(mcu.Halted);
            Assert.True(dbg.Place!.Value.InSketch);
        }

        [Fact]
        public void TheProgramKeepsTheCircuitsClockAndStopsItAtTheBreakpoint()
        {
            var lib = PartLibrary.Default;
            var sch = new Schematic();
            sch.AddPart(lib, "arduino-uno", new GridPoint(0, 0));
            var compiled = CircuitCompiler.Compile(sch, lib);
            var sim = new AnalogSimulator(compiled.Circuit);
            sim.Initialize(InitialState.OperatingPoint);
            var mcu = Mcu();
            var cosim = new FirmwareCoSimulation(mcu, compiled, "U1", () => 5.0);

            // One second in 50 µs slices: the program's clock is the circuit's (no overrun left to pile up).
            const double slice = 50e-6;
            for (int i = 0; i < 20000; i++)
            {
                double ran = cosim.Advance(slice, sim.Time);
                Assert.Equal(slice, ran);
                sim.Step(slice);
            }

            double drift = cosim.ProgramTime - sim.Time;
            _out.WriteLine($"after {sim.Time:F6} s the program is {drift * 1e6:F3} µs ahead ({mcu.Cycles} cycles)");
            Assert.InRange(drift, 0, 5 / mcu.ClockHz);

            // A breakpoint inside a slice: the circuit is told to go exactly as far as the program did.
            var info = Info();
            var dbg = cosim.AttachDebugger(info);
            dbg.SetBreakpoints(info.Breakpoint("sketch.ino", LineOf("angle += step;")).Addresses);
            double stoppedAt = double.NaN;
            for (int i = 0; i < 20000 && double.IsNaN(stoppedAt); i++)
            {
                double now = sim.Time;
                double ran = cosim.Advance(slice, now);
                if (ran > 0) sim.Step(ran);
                if (cosim.Halted) stoppedAt = sim.Time;
            }

            Assert.False(double.IsNaN(stoppedAt));
            Assert.InRange(Math.Abs(cosim.ProgramTime - stoppedAt), 0, 1e-9);
            Assert.Equal(0, cosim.Advance(slice, sim.Time));   // stays where it stopped until resumed
            dbg.Resume();
            Assert.Equal(slice, cosim.Advance(slice, sim.Time));
        }
    }
}
