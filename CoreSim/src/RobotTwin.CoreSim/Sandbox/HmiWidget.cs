namespace RobotTwin.CoreSim.Sandbox
{
    /// <summary>
    /// [hmi] One element of the world's operator panel (HMI), bound to a PLC's variable: a lamp, a push button, a switch, a
    /// number, a set point or a bar. Kept with the world, so a saved cell keeps its panel.
    /// </summary>
    public sealed class HmiWidget
    {
        public const string Lamp = "lamba", Button = "düğme", Switch = "anahtar", Number = "sayı", SetPoint = "ayar", Bar = "çubuk";

        public static readonly string[] Kinds = { Lamp, Button, Switch, Number, SetPoint, Bar };

        public string Kind { get; set; } = Lamp;

        public string Label { get; set; } = string.Empty;

        /// <summary>The PLC (its object id) and the variable (as the program names it, or an address such as %MW3).</summary>
        public string Plc { get; set; } = string.Empty;

        public string Variable { get; set; } = string.Empty;

        /// <summary>A bar's or set point's range, and the unit shown after a number.</summary>
        public double Min { get; set; }

        public double Max { get; set; } = 100;

        public string Unit { get; set; } = string.Empty;
    }
}
