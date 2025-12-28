using System;
using System.Runtime.InteropServices;

namespace RobotTwin.Core
{
    public static class BridgeInterface
    {
        private const string PLUGIN_NAME = "NativeEngine";

        [DllImport(PLUGIN_NAME, EntryPoint = "GetSharedState")]
        private static extern IntPtr _GetSharedState();

        [DllImport(PLUGIN_NAME, EntryPoint = "SetComponentXY")]
        private static extern void _SetComponentXY(uint index, uint x, uint y);

        [DllImport(PLUGIN_NAME, EntryPoint = "LoadHexFromText")]
        private static extern int _LoadHexFromText([MarshalAs(UnmanagedType.LPStr)] string hexText);

        [DllImport(PLUGIN_NAME, EntryPoint = "LoadHexFromFile")]
        private static extern int _LoadHexFromFile([MarshalAs(UnmanagedType.LPStr)] string path);

        public const int ComponentCount = 3;
        public const int NodeCount = 4;
        public const int CurrentCount = 2;

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        public unsafe struct SharedState
        {
            public fixed uint ComponentPositions[ComponentCount * 2];
            public fixed float NodeVoltages[NodeCount];
            public fixed float Currents[CurrentCount];
            public uint ErrorFlags;
            public uint Tick;

            public void GetComponentXY(int index, out uint x, out uint y)
            {
                fixed (uint* pos = ComponentPositions)
                {
                    int baseIndex = index * 2;
                    x = pos[baseIndex];
                    y = pos[baseIndex + 1];
                }
            }
        }

        public static unsafe bool TryReadState(out SharedState state)
        {
            var ptr = _GetSharedState();
            if (ptr == IntPtr.Zero)
            {
                state = default;
                return false;
            }
            state = *(SharedState*)ptr;
            return true;
        }

        public static void SetComponentXY(int index, uint x, uint y)
        {
            if (index < 0 || index >= ComponentCount) return;
            _SetComponentXY((uint)index, x, y);
        }

        public static bool LoadHexText(string hexText)
        {
            if (string.IsNullOrWhiteSpace(hexText)) return false;
            return _LoadHexFromText(hexText) != 0;
        }

        public static bool LoadHexFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            return _LoadHexFromFile(path) != 0;
        }
    }
}
