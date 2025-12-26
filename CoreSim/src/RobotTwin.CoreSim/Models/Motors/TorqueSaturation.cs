using System;

namespace RobotTwin.CoreSim.Models.Motors
{
    public class TorqueSaturation
    {
        public double MaxTorqueNm { get; set; }

        public TorqueSaturation(double maxTorqueNm)
        {
            MaxTorqueNm = maxTorqueNm;
        }

        public double ApplyLimit(double requestedTorque)
        {
            if (Math.Abs(requestedTorque) > MaxTorqueNm)
            {
                return Math.Sign(requestedTorque) * MaxTorqueNm;
            }
            return requestedTorque;
        }

        public bool IsSaturated(double requestedTorque)
        {
            return Math.Abs(requestedTorque) > MaxTorqueNm;
        }
    }
}
