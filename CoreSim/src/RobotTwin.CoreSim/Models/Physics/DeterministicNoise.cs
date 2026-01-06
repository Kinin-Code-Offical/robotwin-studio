using System;

namespace RobotTwin.CoreSim.Models.Physics
{
    public static class DeterministicNoise
    {
        public static double SampleUnit(string key, int step = 0)
        {
            uint hash = Hash(key, step);
            return (hash & 0x7FFFFFFF) / (double)int.MaxValue;
        }

        public static double SampleSigned(string key, int step = 0)
        {
            return SampleUnit(key, step) * 2.0 - 1.0;
        }

        private static uint Hash(string key, int step)
        {
            unchecked
            {
                const uint offset = 2166136261;
                const uint prime = 16777619;
                uint hash = offset;
                if (!string.IsNullOrEmpty(key))
                {
                    for (int i = 0; i < key.Length; i++)
                    {
                        hash ^= key[i];
                        hash *= prime;
                    }
                }
                hash ^= (uint)step;
                hash *= prime;
                return hash;
            }
        }
    }
}
