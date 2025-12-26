using System;

namespace RobotTwin.CoreSim.Models.Physics
{
    public class ThermalModel
    {
        public double CurrentTempC { get; private set; } = 25.0; // Ambient
        public double AmbientTempC { get; set; } = 25.0;
        
        // Properties
        public double Resistance { get; set; } = 100.0; // Ohms
        public double ThermalMass { get; set; } = 1.0; // J/K (Simplified)
        public double DissipationConstant { get; set; } = 0.5; // W/K

        public void Update(double currentAmps, double dt)
        {
            // Power = I^2 * R
            double powerInput = currentAmps * currentAmps * Resistance;
            
            // Heat Dissipation = k * (T_curr - T_amb)
            double powerDissipated = DissipationConstant * (CurrentTempC - AmbientTempC);
            
            // Net Energy = Input - Output
            double netPower = powerInput - powerDissipated;
            
            // dT = Power * dt / ThermalMass
            double tempChange = (netPower * dt) / ThermalMass;
            
            CurrentTempC += tempChange;
        }

        public bool IsOverheating(double maxTempC)
        {
            return CurrentTempC > maxTempC;
        }
    }
}
