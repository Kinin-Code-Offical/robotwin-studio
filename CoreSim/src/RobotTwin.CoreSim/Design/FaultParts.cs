using System;
using RobotTwin.CoreSim.Analog.Devices;

namespace RobotTwin.CoreSim.Design
{
    /// <summary>
    /// [faults] Test instruments that put faults on a wire, to see what the firmware does with them: interference that
    /// drags a line low or high for microseconds at random, a line stuck at a level from a moment on, and a loose contact
    /// that breaks a wire now and then. They are parts of the circuit (<see cref="FaultSwitch"/>): what a fault does
    /// depends on what drives the line and how the receiver filters it, as on a bench.
    /// </summary>
    public static class FaultParts
    {
        public static void Register(PartLibrary lib)
        {
            var line = new PartDefinition
            {
                Id = "line-fault", Name = "Hat Arıza Enjektörü", Category = StandardParts.Instruments, RefPrefix = "FI", Symbol = SymbolKind.Module,
                Footprint = "HDR-1x3", ValueKey = "fault",
                Description = "Bir hatta arıza koyar: rastgele anlarda kısa parazit darbeleri (hattı GND'ye ya da VCC'ye çeker) ya da bir andan sonra hattın bir seviyede takılı kalması. HAT arızalanacak hatta, GND ve (yüksek arızalarda) VCC beslemeye bağlanır. Arıza, düşük dirençli bir kısa devre olarak devreye girer: güçlü bir sürücü seviyesini koruyabilir, alıcının süzgeci kısa darbeyi atabilir.",
                Build = c =>
                {
                    string fault = c.Text("fault");
                    bool high = fault is "glitch-high" or "stuck-high";
                    bool random = fault is "glitch-low" or "glitch-high";
                    var schedule = new FaultSchedule(random, c.Num("rate", 100), c.Num("width", 2e-6), c.Num("start", 0), c.Num("duration", 0), (int)c.Num("seed", 1));
                    c.Add(new FaultSwitch(c.RefDes, c.Pin("HAT"), c.Pin(high ? "VCC" : "GND"), schedule, false)
                    {
                        OnResistance = Math.Max(1e-3, c.Num("ron", 1)),
                    });
                },
            };
            line.Parameters.Add(new ParameterDefinition("fault", "Arıza türü", "glitch-low", "", new[] { "glitch-low", "glitch-high", "stuck-low", "stuck-high" }));
            line.Parameters.Add(new ParameterDefinition("rate", "Ortalama sıklık (parazit)", "100", "Hz", null));
            line.Parameters.Add(new ParameterDefinition("width", "Darbe genişliği (parazit)", "2u", "s", null));
            line.Parameters.Add(new ParameterDefinition("start", "Başlangıç", "0", "s", null));
            line.Parameters.Add(new ParameterDefinition("duration", "Süre (0 = sürekli)", "0", "s", null));
            line.Parameters.Add(new ParameterDefinition("ron", "Kısa devre direnci", "1", "Ω", null));
            line.Parameters.Add(new ParameterDefinition("seed", "Tohum (aynı tohum aynı arızalar)", "1", "", null));
            line.Pins.Add(new PinDefinition("HAT", "1", -4, 0));
            line.Pins.Add(new PinDefinition("VCC", "2", 4, -2));
            line.Pins.Add(new PinDefinition("GND", "3", 4, 2));
            line.HalfWidth = 3;
            line.HalfHeight = 3;
            lib.Add(line);

            var loose = new PartDefinition
            {
                Id = "loose-contact", Name = "Gevşek Temas (kesik hat)", Category = StandardParts.Instruments, RefPrefix = "FI", Symbol = SymbolKind.Switch,
                Footprint = "WIRE-2", ValueKey = "fault",
                Description = "Bir teli A ile B arasında taşır ve arada bir koparır: gevşek bir konnektör, çatlak bir lehim. Kesik kesik modunda rastgele anlarda kısa kopukluklar, kopuk modunda bir andan sonra hat tamamen açık.",
                Build = c =>
                {
                    bool random = c.Text("fault") != "open";
                    var schedule = new FaultSchedule(random, c.Num("rate", 10), c.Num("width", 1e-3), c.Num("start", 0), c.Num("duration", 0), (int)c.Num("seed", 1));
                    c.Add(new FaultSwitch(c.RefDes, c.Pin("A"), c.Pin("B"), schedule, true) { OnResistance = 0.05 });
                },
            };
            loose.Parameters.Add(new ParameterDefinition("fault", "Arıza türü", "intermittent", "", new[] { "intermittent", "open" }));
            loose.Parameters.Add(new ParameterDefinition("rate", "Ortalama sıklık (kesik kesik)", "10", "Hz", null));
            loose.Parameters.Add(new ParameterDefinition("width", "Kopukluk süresi (kesik kesik)", "1m", "s", null));
            loose.Parameters.Add(new ParameterDefinition("start", "Başlangıç", "0", "s", null));
            loose.Parameters.Add(new ParameterDefinition("duration", "Süre (0 = sürekli)", "0", "s", null));
            loose.Parameters.Add(new ParameterDefinition("seed", "Tohum (aynı tohum aynı arızalar)", "1", "", null));
            loose.Pins.Add(new PinDefinition("A", "1", -3, 0));
            loose.Pins.Add(new PinDefinition("B", "2", 3, 0));
            loose.HalfWidth = 2;
            loose.HalfHeight = 1;
            lib.Add(loose);
        }
    }
}
