using UnityEngine;
using CoreSim.Specs;

namespace RobotTwin.Core
{
    public static class CoreSimAdapter
    {
        // Future home of GameObject instantiation logic
        public static GameObject InstantiateRobot(RobotSpec spec)
        {
            // Placeholder
            var go = new GameObject(spec.Name);
            return go;
        }
    }
}
