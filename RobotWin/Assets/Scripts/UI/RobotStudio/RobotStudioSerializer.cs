using System;
using System.IO;
using RobotTwin.CoreSim.Serialization;
using RobotTwin.CoreSim.Specs;

namespace RobotTwin.UI
{
    public static class RobotStudioSerializer
    {
        public const string PackageExtension = ".rtrobot";

        public static void Save(RobotStudioPackage payload, string filePath)
        {
            if (payload == null || string.IsNullOrWhiteSpace(filePath)) return;
            SimulationSerializer.SaveRobotPackage(payload, filePath);
        }

        public static RobotStudioPackage Load(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return new RobotStudioPackage();
            }

            var payload = SimulationSerializer.LoadRobotPackage(filePath, true);
            return payload ?? new RobotStudioPackage();
        }
    }
}
