namespace RobotTwin.CoreSim.Models.Power
{
    public class BatteryModel
    {
        public double CapacitymAh { get; private set; }
        public double RemainingmAh { get; private set; }
        public double NominalVoltage { get; private set; }
        public double InternalResistance { get; set; } = 0.1;

        public BatteryModel(double capacitymAh, double voltage)
        {
            CapacitymAh = capacitymAh;
            RemainingmAh = capacitymAh;
            NominalVoltage = voltage;
        }

        public double GetVoltage(double loadCurrent)
        {
            // Simple model: V = V_nom * (StateOfCharge) - I*R
            // Actually Linear Drop is better for MVP:
            // 100% -> 1.1 * Nom
            // 0% -> 0.8 * Nom
            
            double stateOfCharge = RemainingmAh / CapacitymAh;
            if (stateOfCharge < 0) stateOfCharge = 0;

            double openCircuitVoltage = NominalVoltage * (0.8 + 0.3 * stateOfCharge); // 3.3V -> 2.64V to 3.63V range roughly
            
            return openCircuitVoltage - (loadCurrent * InternalResistance);
        }

        public void Drain(double currentAmps, double dtSeconds)
        {
            // mAh drained = Amps * (hours)
            double hours = dtSeconds / 3600.0;
            double drained = currentAmps * 1000.0 * hours; // Amps to mA
            
            RemainingmAh -= drained;
            if (RemainingmAh < 0) RemainingmAh = 0;
        }
    }
}
