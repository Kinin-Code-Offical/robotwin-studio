using System;
using System.Collections.Generic;
using RobotTwin.CoreSim.Firmware.Debug;

namespace RobotTwin.CoreSim.Firmware.Xtensa
{
    public sealed partial class Esp32Soc
    {
        // ================================================================ calling the firmware's own functions
        //
        // What the chip's closed drivers do from their own tasks (the Wi-Fi driver posting an event, handing a received
        // frame to the network stack) the emulator does by calling the firmware's functions itself: at a point where a
        // task may call anything that does not block — the entry of the idle task's hook, which runs whenever a core has
        // nothing else to do — it makes the call the idle task would make (CALLn as the hook's own call, a return address
        // the ROM traps), lets it run like any code (interrupts, task switches, window exceptions and all), and on its
        // return puts the hook's call back as it was. One call is under way at a time.

        /// <summary>Where a call the emulator made returns to: a mask ROM address no function uses.</summary>
        public const uint HostCallReturn = 0x4006FF00;

        private sealed class HostCall
        {
            public uint Function;
            public uint[] Args = Array.Empty<uint>();
            public Action<uint>? Done;
        }

        private readonly Queue<HostCall> _hostCalls = new Queue<HostCall>();
        private HostCall? _hostCall;
        private XtensaCore? _hostCallCore;
        private readonly uint[] _hostCallSaved = new uint[12];
        private uint _hostCallResume, _hostCallCallInc;

        /// <summary>The address of the idle task's hook, once the ELF named it (0: none, calls never run).</summary>
        private uint _idleHook;

        /// <summary>Calls the firmware's function at <paramref name="function"/> with up to six arguments when a core is next idle; <paramref name="done"/> gets its result.</summary>
        public void CallFirmware(uint function, Action<uint>? done, params uint[] args)
        {
            if (args.Length > 6) throw new ArgumentException("En çok altı bağımsız değişken.", nameof(args));
            _hostCalls.Enqueue(new HostCall { Function = function, Args = args, Done = done });
        }

        /// <summary>Calls waiting for an idle core, plus the one under way.</summary>
        public int PendingFirmwareCalls => _hostCalls.Count + (_hostCall != null ? 1 : 0);

        private void BindHostCalls(ElfImage elf)
        {
            _hostCalls.Clear();
            _hostCall = null;
            _hostCallCore = null;
            _idleHook = 0;
            foreach (var symbol in elf.Symbols)
            {
                if (symbol.Name != "esp_vApplicationIdleHook" || symbol.Value < IramBase || symbol.Value >= IromEnd) continue;
                _idleHook = symbol.Value;
                foreach (var core in new[] { Pro, App }) core.AddAppTrap(symbol.Value, ServeIdleHook);
            }
        }

        /// <summary>The idle hook's entry: start the next waiting call (false runs the hook itself).</summary>
        private bool ServeIdleHook(XtensaCore c)
        {
            if (_hostCall != null || _hostCalls.Count == 0) return false;
            int n = c.CallInc;
            if (n == 0) return false;   // a CALL0 caller (not the idle task's windowed call): leave it
            _hostCallCore = c;
            _hostCallResume = c.Pc;
            _hostCallCallInc = (uint)n;
            for (int i = n * 4; i < 16; i++) _hostCallSaved[i - n * 4] = c.A(i);
            StartHostCall(c);
            return true;
        }

        private void StartHostCall(XtensaCore c)
        {
            var call = _hostCalls.Dequeue();
            _hostCall = call;
            int n = (int)_hostCallCallInc;
            // As CALLn would leave it: the return address in a(n*4) with the window increment in its top bits, the
            // arguments in a(n*4+2)…, CALLINC n for the callee's ENTRY.
            c.SetA(n * 4, ((uint)n << 30) | (HostCallReturn & 0x3FFFFFFF));
            for (int k = 0; k < call.Args.Length && n * 4 + 2 + k < 16; k++) c.SetA(n * 4 + 2 + k, call.Args[k]);
            c.Ps = (c.Ps & ~0x30000u) | ((uint)n << 16);
            c.Pc = call.Function;
        }

        /// <summary>A call the emulator made has returned (the core is at <see cref="HostCallReturn"/>, back in the hook's caller's window).</summary>
        private bool FinishHostCall(XtensaCore c)
        {
            if (_hostCall == null || c != _hostCallCore) return false;
            int n = (int)_hostCallCallInc;
            uint result = c.A(n * 4 + 2);
            var done = _hostCall.Done;
            _hostCall = null;
            done?.Invoke(result);   // it may queue the next step (allocate, fill, then hand over)
            if (_hostCalls.Count > 0)
            {
                StartHostCall(c);   // the next one from the same point
            }
            else
            {
                for (int i = n * 4; i < 16; i++) c.SetA(i, _hostCallSaved[i - n * 4]);
                c.Ps = (c.Ps & ~0x30000u) | ((uint)n << 16);
                c.Pc = _hostCallResume;
                _hostCallCore = null;
            }

            return true;
        }
    }
}
